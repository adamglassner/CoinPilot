import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:http/http.dart' as http;

import 'crypto.dart';

/// A problem talking to the server, with a message that can be shown.
class SyncException implements Exception {
  SyncException(this.error, this.message, {this.status});
  final String error;
  final String message;
  final int? status;
  bool get isAuthProblem => status == 401;
  @override
  String toString() => message;
}

class ServerInfo {
  ServerInfo(this.name, this.version, this.openSignup, this.syncing);
  final String name, version;
  final bool openSignup, syncing;
}

class Session {
  Session(this.token, this.username, this.wrappedKey, this.kdfSalt, this.iterations, this.latestSeq);
  final String token, username, wrappedKey, kdfSalt;
  final int iterations, latestSeq;
  factory Session.fromJson(Map<String, dynamic> j) =>
      Session(j['token'], j['username'], j['wrappedKey'], j['kdfSalt'], j['iterations'], j['latestSeq'] ?? 0);
}

class Change {
  Change(this.collection, this.id, this.seq, this.modified, this.deleted, this.payload);
  final String collection, id;
  final int seq, modified;
  final bool deleted;
  final String? payload;
}

class Device {
  Device(this.id, this.name, this.platform, this.appVersion, this.lastSeen, this.isThisDevice);
  final String id, name, platform, appVersion;
  final DateTime lastSeen;
  final bool isThisDevice;
}

class ServerAccount {
  ServerAccount(this.username, this.records, this.fileBytes, this.devices);
  final String username;
  final int records, fileBytes;
  final List<Device> devices;
}

/// Talks to a WillowWallet Server's /api/v1 (server/PROTOCOL.md).
class SyncApi {
  SyncApi(String url, {http.Client? client}) : base = normalizeUrl(url), _http = client ?? http.Client();

  final Uri base;
  final http.Client _http;
  String? token;

  /// "myserver:8484" → http://myserver:8484/
  static Uri normalizeUrl(String url) {
    var u = url.trim();
    if (!u.contains('://')) u = 'http://$u';
    if (!u.endsWith('/')) u = '$u/';
    final uri = Uri.tryParse(u);
    if (uri == null || !(uri.scheme == 'http' || uri.scheme == 'https') || uri.host.isEmpty) {
      throw SyncException('bad_url', 'That doesn\'t look like a server address. Try something like http://192.168.1.20:8484');
    }
    return uri;
  }

  static Map<String, dynamic> device(String name, String version) => {'name': name, 'platform': 'Android', 'appVersion': version};

  Future<ServerInfo> info() async {
    final j = await _json('GET', 'api/v1/info');
    return ServerInfo(j['name'] ?? 'WillowWallet Server', j['version'] ?? '', j['openSignup'] == true, j['syncing'] != false);
  }

  Future<Map<String, dynamic>> prelogin(String username) => _json('POST', 'api/v1/auth/prelogin', {'username': username});
  Future<Session> register(Map<String, dynamic> body) async => Session.fromJson(await _json('POST', 'api/v1/auth/register', body));
  Future<Session> login(Map<String, dynamic> body) async => Session.fromJson(await _json('POST', 'api/v1/auth/login', body));
  Future<Map<String, dynamic>> recoverStart(Map<String, dynamic> body) => _json('POST', 'api/v1/auth/recover/start', body);
  Future<Session> recoverFinish(Map<String, dynamic> body) async => Session.fromJson(await _json('POST', 'api/v1/auth/recover/finish', body));
  Future<void> changePassword(Map<String, dynamic> body) => _json('POST', 'api/v1/auth/password', body);
  Future<void> logout() => _json('POST', 'api/v1/auth/logout', {});
  Future<void> signOutDevice(String id) => _json('DELETE', 'api/v1/account/devices/${Uri.encodeComponent(id)}');

  Future<ServerAccount> account() async {
    final j = await _json('GET', 'api/v1/account');
    return ServerAccount(j['username'], j['records'] ?? 0, j['fileBytes'] ?? 0, [
      for (final d in (j['devices'] as List? ?? const []))
        Device(d['id'], d['name'] ?? '', d['platform'] ?? '', d['appVersion'] ?? '', DateTime.tryParse(d['lastSeen'] ?? '') ?? DateTime.now(),
            d['isThisDevice'] == true),
    ]);
  }

  Future<(List<Change>, int latest, bool more)> changes(int since) async {
    final j = await _json('GET', 'api/v1/sync/changes?since=$since');
    final list = [
      for (final c in (j['changes'] as List? ?? const []))
        Change(c['collection'], c['id'], c['seq'], c['modified'], c['deleted'] == true, c['payload'] as String?),
    ];
    return (list, j['latest'] as int? ?? 0, j['more'] == true);
  }

  /// Returns "collection/id" → "ok" or "stale", and the server's seq of each stale record (to fetch again).
  Future<(Map<String, String>, List<int>)> push(List<Map<String, dynamic>> changes) async {
    final j = await _json('POST', 'api/v1/sync/push', {'changes': changes});
    final results = (j['results'] as List? ?? const []).cast<Map<String, dynamic>>();
    return (
      {for (final r in results) '${r['collection']}/${r['id']}': '${r['status']}'},
      [for (final r in results) if (r['status'] == 'stale') (r['seq'] as num?)?.toInt() ?? 0],
    );
  }

  static String _escape(String name) => name.split('/').map(Uri.encodeComponent).join('/');

  /// Receipts and tax documents (already sealed with the data key).
  Future<void> uploadFile(String name, List<int> sealedBytes) async {
    final req = http.Request('PUT', base.resolve('api/v1/files/${_escape(name)}'))
      ..headers['Content-Type'] = 'application/octet-stream'
      ..bodyBytes = sealedBytes;
    await _bytes(req);
  }

  /// The sealed file, or null when the server doesn't have it.
  Future<Uint8List?> downloadFile(String name) async {
    try {
      return await _bytes(http.Request('GET', base.resolve('api/v1/files/${_escape(name)}')));
    } on SyncException catch (e) {
      if (e.status == 404) return null;
      rethrow;
    }
  }

  Future<void> deleteFile(String name) async {
    try {
      await _bytes(http.Request('DELETE', base.resolve('api/v1/files/${_escape(name)}')));
    } on SyncException catch (e) {
      if (e.status != 404) rethrow;
    }
  }

  Future<Uint8List> _bytes(http.Request req) async {
    if (token != null) req.headers['Authorization'] = 'Bearer $token';
    http.Response res;
    try {
      res = await http.Response.fromStream(await _http.send(req).timeout(const Duration(minutes: 3)));
    } on TimeoutException {
      throw SyncException('offline', 'The server didn\'t answer in time.');
    } on SocketException catch (e) {
      throw SyncException('offline', 'Couldn\'t reach the server (${e.osError?.message ?? e.message}).');
    } on http.ClientException catch (e) {
      throw SyncException('offline', 'Couldn\'t reach the server (${e.message}).');
    }
    if (res.statusCode >= 200 && res.statusCode < 300) return res.bodyBytes;
    var message = 'The server said ${res.statusCode}.';
    var error = 'error';
    try {
      final j = jsonDecode(res.body) as Map<String, dynamic>;
      message = j['message'] as String? ?? message;
      error = j['error'] as String? ?? error;
    } catch (_) {}
    throw SyncException(error, message, status: res.statusCode);
  }

  Future<Map<String, dynamic>> _json(String method, String path, [Object? body]) async {
    final req = http.Request(method, base.resolve(path));
    req.headers['Accept'] = 'application/json';
    if (token != null) req.headers['Authorization'] = 'Bearer $token';
    if (body != null) {
      req.headers['Content-Type'] = 'application/json';
      req.body = jsonEncode(body);
    }
    http.Response res;
    try {
      res = await http.Response.fromStream(await _http.send(req).timeout(const Duration(seconds: 45)));
    } on TimeoutException {
      throw SyncException('offline', 'The server didn\'t answer in time.');
    } on SocketException catch (e) {
      throw SyncException('offline', 'Couldn\'t reach the server (${e.osError?.message ?? e.message}).');
    } on http.ClientException catch (e) {
      throw SyncException('offline', 'Couldn\'t reach the server (${e.message}).');
    } on HandshakeException {
      throw SyncException('offline', 'The server\'s HTTPS certificate couldn\'t be checked.');
    }
    Map<String, dynamic> j = {};
    try {
      if (res.body.isNotEmpty) j = jsonDecode(res.body) as Map<String, dynamic>;
    } catch (_) {}
    if (res.statusCode >= 200 && res.statusCode < 300) return j;
    throw SyncException(j['error'] as String? ?? 'error', j['message'] as String? ?? 'The server said ${res.statusCode}.', status: res.statusCode);
  }

  void close() => _http.close();
}

/// A signed-in phone: the session and the opened data key (never sent anywhere).
class SignedIn {
  SignedIn(this.session, this.dataKey);
  final Session session;
  final Uint8List dataKey;
}

/// The same account steps as the Windows app (create, sign in, recover, change password).
class SyncAccount {
  static void checkPassword(String p) {
    if (p.length < 8) throw SyncException('weak_password', 'Use at least 8 characters for your password.');
  }

  static String? normalizeUsername(String name) {
    final n = name.trim().toLowerCase();
    if (n.length < 3 || n.length > 32 || !RegExp(r'^[a-z0-9._-]+$').hasMatch(n)) return null;
    return n;
  }

  static Future<(SignedIn, String)> register(SyncApi api, String username, String password, String? invite, Map<String, dynamic> device,
      {int iterations = SyncCrypto.defaultIterations}) async {
    final name = normalizeUsername(username) ??
        (throw SyncException('bad_username', 'Usernames are 3 to 32 characters: letters, numbers, dots, dashes and underscores.'));
    checkPassword(password);
    final salt = SyncCrypto.randomBytes(16);
    final creds = await SyncCrypto.fromPassword(password, salt, iterations);
    final dataKey = SyncCrypto.randomBytes(32);
    final (recoveryBytes, recoveryText) = SyncCrypto.newRecoveryKey();
    final recovery = await SyncCrypto.fromRecoveryKey(recoveryBytes);
    final session = await api.register({
      'username': name,
      if (invite != null && invite.trim().isNotEmpty) 'inviteCode': invite.trim(),
      'authSecret': base64.encode(creds.authSecret),
      'kdfSalt': base64.encode(salt),
      'iterations': iterations,
      'wrappedKey': await SyncCrypto.seal(creds.keyEncryptionKey, dataKey, SyncCrypto.keyAad),
      'recoveryAuthSecret': base64.encode(recovery.authSecret),
      'recoveryWrappedKey': await SyncCrypto.seal(recovery.keyEncryptionKey, dataKey, SyncCrypto.keyAad),
      'device': device,
    });
    api.token = session.token;
    return (SignedIn(session, dataKey), recoveryText);
  }

  static Future<SignedIn> login(SyncApi api, String username, String password, Map<String, dynamic> device) async {
    final pre = await api.prelogin(username.trim());
    final creds = await SyncCrypto.fromPassword(password, base64.decode(pre['kdfSalt']), pre['iterations']);
    final session = await api.login({'username': username.trim(), 'authSecret': base64.encode(creds.authSecret), 'device': device});
    api.token = session.token;
    try {
      return SignedIn(session, await SyncCrypto.open(creds.keyEncryptionKey, session.wrappedKey, SyncCrypto.keyAad));
    } catch (_) {
      throw SyncException('bad_key', 'Signed in, but your data key couldn\'t be opened. Try again.');
    }
  }

  static Future<SignedIn> recover(SyncApi api, String username, String recoveryKey, String newPassword, Map<String, dynamic> device,
      {int iterations = SyncCrypto.defaultIterations}) async {
    final bytes = SyncCrypto.parseRecoveryKey(recoveryKey) ??
        (throw SyncException('bad_recovery', 'That isn\'t a recovery key. It looks like ABCD-EFGH-… (32 letters and numbers).'));
    checkPassword(newPassword);
    final recovery = await SyncCrypto.fromRecoveryKey(bytes);
    final start = await api.recoverStart({'username': username.trim(), 'recoveryAuthSecret': base64.encode(recovery.authSecret)});
    late Uint8List dataKey;
    try {
      dataKey = await SyncCrypto.open(recovery.keyEncryptionKey, start['recoveryWrappedKey'], SyncCrypto.keyAad);
    } catch (_) {
      throw SyncException('bad_recovery', 'That recovery key doesn\'t open this account\'s data.');
    }
    final salt = SyncCrypto.randomBytes(16);
    final creds = await SyncCrypto.fromPassword(newPassword, salt, iterations);
    final session = await api.recoverFinish({
      'username': username.trim(),
      'recoveryAuthSecret': base64.encode(recovery.authSecret),
      'newAuthSecret': base64.encode(creds.authSecret),
      'kdfSalt': base64.encode(salt),
      'iterations': iterations,
      'wrappedKey': await SyncCrypto.seal(creds.keyEncryptionKey, dataKey, SyncCrypto.keyAad),
      'device': device,
    });
    api.token = session.token;
    return SignedIn(session, dataKey);
  }

  static Future<void> changePassword(SyncApi api, String username, String current, String next, List<int> dataKey,
      {int iterations = SyncCrypto.defaultIterations}) async {
    checkPassword(next);
    final pre = await api.prelogin(username);
    final cur = await SyncCrypto.fromPassword(current, base64.decode(pre['kdfSalt']), pre['iterations']);
    final salt = SyncCrypto.randomBytes(16);
    final nxt = await SyncCrypto.fromPassword(next, salt, iterations);
    await api.changePassword({
      'currentAuthSecret': base64.encode(cur.authSecret),
      'newAuthSecret': base64.encode(nxt.authSecret),
      'kdfSalt': base64.encode(salt),
      'iterations': iterations,
      'wrappedKey': await SyncCrypto.seal(nxt.keyEncryptionKey, dataKey, SyncCrypto.keyAad),
    });
  }
}
