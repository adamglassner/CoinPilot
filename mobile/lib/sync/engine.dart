import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;

import '../form.dart';
import '../store.dart';
import 'api.dart';
import 'crypto.dart';

enum SyncStatus { off, idle, syncing, offline, paused, signedOut, error }

/// Keeps this phone's transactions and budgets in step with a WillowWallet Server, the same way
/// the Windows app does: changes are sent end-to-end encrypted, newest edit wins, deletes are kept
/// as tombstones. Without a server the app works exactly as before, with everything on the phone.
///
/// Saved in the store's "Sync" section: url, serverName, username, token, key, lastSeq,
/// synced ("collection/id" → the record's UpdatedAt in ms when the server last had it) and lastSync.
class SyncEngine extends ChangeNotifier {
  SyncEngine(this.store, {this.appVersion = '', this.automatic = true, this.httpClient});

  final Store store;
  final String appVersion;

  /// Makes the HTTP client (screenshots use a pretend server).
  final http.Client Function()? httpClient;
  SyncApi _newApi(String url) => SyncApi(url, client: httpClient?.call());

  /// Syncs a few seconds after each change and every minute. Off in tests, which call [syncNow].
  final bool automatic;

  SyncApi? _api;
  Uint8List? _key;
  SyncStatus status = SyncStatus.off;
  String? problem;
  Timer? _soon, _every;
  Future<bool>? _running;

  Map<String, dynamic> get _s => store.syncState;
  bool get isSetUp => (_s['url'] as String?)?.isNotEmpty == true;
  bool get isConnected => isSetUp && _key != null && _api?.token != null;
  String get serverName => _s['serverName'] as String? ?? 'WillowWallet Server';
  String get serverUrl => _s['url'] as String? ?? '';
  String get username => _s['username'] as String? ?? '';
  DateTime? get lastSync => DateTime.tryParse(_s['lastSync'] as String? ?? '');
  Map<String, dynamic> get _synced => (_s['synced'] ??= <String, dynamic>{}) as Map<String, dynamic>;
  Map<String, dynamic> get device => isDesktop
      ? SyncApi.device(_hostname(), appVersion, platform: hostOs == HostOs.macos ? 'macOS' : 'Linux')
      : SyncApi.device('Android phone', appVersion);

  static String _hostname() {
    try {
      return Platform.localHostname;
    } catch (_) {
      return hostName;
    }
  }

  /// Picks up a saved connection and starts syncing in the background.
  void start() {
    store.onLocalChange = syncSoon;
    if (!isSetUp) return _set(SyncStatus.off);
    final token = _s['token'] as String?;
    final key = _s['key'] as String?;
    _api = _newApi(serverUrl)..token = token;
    if (token == null || key == null) return _set(SyncStatus.signedOut, 'Sign in again to keep syncing.');
    _key = base64.decode(key);
    _upgrade();
    _set(SyncStatus.idle);
    if (automatic) {
      _every?.cancel();
      _every = Timer.periodic(const Duration(seconds: 60), (_) => syncNow());
      syncSoon();
    }
  }

  /// Version 0.3 keeps every kind of record (0.2 kept only transactions and budgets, and read accounts and
  /// categories): fetch everything again once, without sending back what came from the server.
  void _upgrade() {
    if ((_s['schema'] as int? ?? 0) >= 2) return;
    for (final c in ['accounts', 'categories']) {
      for (final (id, ms) in store.syncEntries(c)) {
        _synced['$c/$id'] = ms;
      }
    }
    _s['lastSeq'] = 0;
    _s['schema'] = 2;
    store.saveSyncState();
  }

  /// A change was made here: sync in a few seconds (changes made close together go in one trip).
  void syncSoon() {
    if (!automatic || !isConnected) return;
    _soon?.cancel();
    _soon = Timer(const Duration(seconds: 3), () => syncNow());
  }

  void _set(SyncStatus s, [String? message]) {
    status = s;
    problem = message;
    notifyListeners();
  }

  // ----- Connecting -----

  Future<ServerInfo> checkServer(String url) async {
    final api = _newApi(url);
    try {
      return await api.info();
    } finally {
      api.close();
    }
  }

  bool get hasLocalData => !store.isEmpty;

  /// Signs in (or creates the account) and remembers the connection. Returns the recovery key for a new account.
  Future<String?> connect(String url, String username, String password,
      {bool create = false, String? invite, int iterations = SyncCrypto.defaultIterations}) async {
    final api = _newApi(url);
    try {
      final info = await api.info();
      String? recovery;
      SignedIn signedIn;
      if (create) {
        (signedIn, recovery) = await SyncAccount.register(api, username, password, invite, device, iterations: iterations);
      } else {
        signedIn = await SyncAccount.login(api, username, password, device);
      }
      _adopt(api, info, signedIn);
      return recovery;
    } catch (_) {
      api.close();
      rethrow;
    }
  }

  /// Sets a new password with the recovery key, then signs in.
  Future<void> recover(String url, String username, String recoveryKey, String newPassword,
      {int iterations = SyncCrypto.defaultIterations}) async {
    final api = _newApi(url);
    try {
      final info = await api.info();
      final signedIn = await SyncAccount.recover(api, username, recoveryKey, newPassword, device, iterations: iterations);
      _adopt(api, info, signedIn);
    } catch (_) {
      api.close();
      rethrow;
    }
  }

  void _adopt(SyncApi api, ServerInfo info, SignedIn signedIn) {
    _api?.close();
    _api = api;
    _key = signedIn.dataKey;
    final url = api.base.toString();
    final same = _s['url'] == url && _s['username'] == signedIn.session.username;
    if (!same) store.syncState = {};
    _s
      ..['url'] = url
      ..['serverName'] = info.name
      ..['username'] = signedIn.session.username
      ..['token'] = signedIn.session.token
      ..['key'] = base64.encode(signedIn.dataKey)
      ..['schema'] = 2;
    store.saveSyncState();
    start();
  }

  /// First sync after connecting. Combine merges this phone's data with the server's (newest edit wins);
  /// [useServerOnly] replaces this phone's data with the server's copy.
  Future<bool> firstSync({required bool useServerOnly}) async {
    if (!isConnected) return false;
    if (useServerOnly) {
      store.clearForServer();
      _synced.clear();
    }
    _s['lastSeq'] = 0;
    store.saveSyncState();
    return syncNow(manual: true);
  }

  Future<void> changePassword(String current, String next, {int iterations = SyncCrypto.defaultIterations}) {
    if (!isConnected) throw SyncException('unauthorized', 'Sign in first.');
    return SyncAccount.changePassword(_api!, username, current, next, _key!, iterations: iterations);
  }

  Future<ServerAccount> account() {
    if (!isConnected) throw SyncException('unauthorized', 'Sign in first.');
    return _api!.account();
  }

  Future<void> signOutDevice(String id) async {
    if (isConnected) await _api!.signOutDevice(id);
  }

  /// Stops syncing on this phone. The data stays here.
  Future<void> signOut() async {
    _soon?.cancel();
    _every?.cancel();
    if (_api?.token != null) {
      try {
        await _api!.logout();
      } catch (_) {}
    }
    _api?.close();
    _api = null;
    _key = null;
    store.syncState = {};
    store.saveSyncState();
    _set(SyncStatus.off);
  }

  /// Changes on this phone the server doesn't have yet.
  int get pendingCount => isSetUp ? _localChanges().length : 0;

  // ----- Syncing -----

  /// Sends this phone's changes, then fetches everyone else's. Returns true when it finished.
  Future<bool> syncNow({bool manual = false}) {
    if (!isConnected) return Future.value(false);
    final running = _running;
    if (running != null) return manual ? running.then((_) => syncNow(manual: true)) : Future.value(false);
    final f = _run().whenComplete(() => _running = null);
    _running = f;
    return f;
  }

  Future<bool> _run() async {
    try {
      _set(SyncStatus.syncing);
      await _push();
      await _pull();
      await _syncFiles();
      _s['lastSync'] = DateTime.now().toIso8601String();
      store.saveSyncState();
      _set(SyncStatus.idle);
      return true;
    } on SyncException catch (e) {
      store.saveSyncState();
      if (e.isAuthProblem) {
        _api?.token = null;
        _key = null;
        _s.remove('token');
        _s.remove('key');
        store.saveSyncState();
        _set(SyncStatus.signedOut,
            'You were signed out of sync (your password changed or the server\'s admin signed this phone out). Sign in again to keep syncing.');
      } else if (e.error == 'paused') {
        _set(SyncStatus.paused, e.message);
      } else if (e.error == 'offline') {
        _set(SyncStatus.offline, e.message);
      } else {
        _set(SyncStatus.error, e.message);
      }
      return false;
    } catch (e) {
      store.saveSyncState();
      _set(SyncStatus.error, 'Sync stopped: $e');
      return false;
    }
  }

  List<({String collection, String id, bool deleted, int modified})> _localChanges() {
    final list = <({String collection, String id, bool deleted, int modified})>[];
    for (final c in Store.editable) {
      final present = <String>{};
      for (final (id, modified) in store.syncEntries(c)) {
        present.add(id);
        if ((_synced['$c/$id'] as num?)?.toInt() == modified) continue;
        list.add((collection: c, id: id, deleted: false, modified: modified));
      }
      final now = DateTime.now().millisecondsSinceEpoch;
      for (final MapEntry(:key, :value) in _synced.entries.where((e) => e.key.startsWith('$c/'))) {
        final id = key.substring(c.length + 1);
        if (present.contains(id)) continue;
        // A delete is newer than the last version this phone had, even if that edit's clock ran ahead
        final last = (value as num?)?.toInt() ?? 0;
        list.add((collection: c, id: id, deleted: true, modified: now > last ? now : last + 1));
      }
    }
    return list;
  }

  Future<void> _push() async {
    final changes = _localChanges();
    for (var i = 0; i < changes.length; i += 200) {
      final batch = changes.sublist(i, i + 200 > changes.length ? changes.length : i + 200);
      final request = <Map<String, dynamic>>[];
      for (final c in batch) {
        if (c.deleted) {
          request.add({'collection': c.collection, 'id': c.id, 'modified': c.modified, 'deleted': true});
          continue;
        }
        final record = store.syncRecord(c.collection, c.id);
        if (record == null) continue;
        request.add({
          'collection': c.collection,
          'id': c.id,
          'modified': c.modified,
          'deleted': false,
          'payload': await SyncCrypto.sealRecord(_key!, c.collection, c.id, record),
        });
      }
      if (request.isEmpty) continue;
      final (results, staleSeqs) = await _api!.push(request);
      // The server has a newer version of these: fetch them again on the pull that follows
      for (final seq in staleSeqs) {
        if (seq > 0 && seq <= (_s['lastSeq'] as int? ?? 0)) _s['lastSeq'] = seq - 1;
      }
      for (final c in batch) {
        final key = '${c.collection}/${c.id}';
        if (results[key] != 'ok') continue;
        if (c.deleted) {
          _synced.remove(key);
        } else {
          _synced[key] = c.modified;
        }
      }
      store.saveSyncState();
    }
  }

  Future<void> _pull() async {
    while (true) {
      final since = _s['lastSeq'] as int? ?? 0;
      final (changes, latest, more) = await _api!.changes(since);
      await _apply(changes);
      _s['lastSeq'] = changes.isNotEmpty ? changes.last.seq : (latest > since ? latest : since);
      store.saveSyncState();
      if (!more) break;
    }
  }

  Future<void> _apply(List<Change> changes) async {
    if (changes.isEmpty) return;
    // The newest change per record
    final latest = <String, Change>{};
    for (final c in changes) {
      latest['${c.collection}/${c.id}'] = c;
    }
    final upserts = <String, List<Map<String, dynamic>>>{};
    final deletes = <String, List<String>>{};
    for (final MapEntry(key: key, value: c) in latest.entries) {
      if (!store.keeps(c.collection)) continue; // settings, receipts and the like stay on the PC
      final editable = Store.editable.contains(c.collection);
      final localModified = store.syncModified(c.collection, c.id);
      if (editable) {
        final changedHere = localModified != null && _synced[key] != localModified;
        if (changedHere && localModified > c.modified) continue; // ours is newer: it gets sent next
      }
      if (c.deleted) {
        if (localModified != null) (deletes[c.collection] ??= []).add(c.id);
        _synced.remove(key);
      } else if (c.payload != null) {
        if (editable && localModified != null && _synced[key] == localModified && localModified == c.modified) continue;
        final record = await SyncCrypto.openRecord(_key!, c.collection, c.id, c.payload!);
        record['Id'] ??= c.id; // settings files from the PC have no Id: it's the file's name
        (upserts[c.collection] ??= []).add(record);
      }
    }
    for (final MapEntry(key: c, value: ids) in deletes.entries) {
      store.removeRemote(c, ids);
    }
    for (final MapEntry(key: c, value: records) in upserts.entries) {
      store.applyRemote(c, records);
      if (!Store.editable.contains(c)) continue;
      for (final r in records) {
        final id = r['Id'] as String? ?? '';
        final m = store.syncModified(c, id);
        if (m != null) _synced['$c/$id'] = m;
      }
    }
  }

  // ----- Receipts and tax documents -----

  List<String> get _files => ((_s['files'] ??= <dynamic>[]) as List).cast<String>();

  static final _validName = RegExp(r'^[A-Za-z0-9._/-]{1,200}$');

  /// Uploads files the server doesn't have, downloads ones this phone is missing, and removes ones no record uses.
  Future<void> _syncFiles() async {
    final files = [..._files];
    final wanted = store.wantedFiles();
    for (final (name, path) in wanted) {
      if (!_validName.hasMatch(name) || name.contains('..')) continue;
      final file = File(path);
      final here = await file.exists();
      if (here && !files.contains(name)) {
        final sealed = await SyncCrypto.sealBytes(_key!, await file.readAsBytes(), fileAad(name));
        await _api!.uploadFile(name, sealed);
        files.add(name);
      } else if (!here) {
        final sealed = await _api!.downloadFile(name);
        if (sealed == null) continue;
        await file.parent.create(recursive: true);
        await file.writeAsBytes(await SyncCrypto.openBytes(_key!, sealed, fileAad(name)), flush: true);
        if (!files.contains(name)) files.add(name);
      }
    }
    final keep = {for (final (name, _) in wanted) name};
    for (final name in files.where((f) => !keep.contains(f)).toList()) {
      await _api!.deleteFile(name);
      files.remove(name);
    }
    _s['files'] = files;
    store.saveSyncState();
  }

  static String fileAad(String name) => 'ww-file-v1:$name';

  // ----- Words -----

  String get statusText => switch (status) {
        SyncStatus.off => 'Saved on this phone',
        SyncStatus.syncing => 'Syncing…',
        SyncStatus.idle => lastSync == null ? 'Synced with $serverName' : 'Synced ${ago(lastSync!)}',
        SyncStatus.offline => 'Offline · changes are saved here',
        SyncStatus.paused => 'Sync is paused by the server',
        SyncStatus.signedOut => 'Signed out of sync',
        SyncStatus.error => 'Sync problem',
      };

  static String ago(DateTime t) {
    final d = DateTime.now().difference(t);
    if (d.inSeconds < 45) return 'just now';
    if (d.inMinutes < 60) return '${d.inMinutes} min ago';
    if (d.inHours < 24) return '${d.inHours} h ago';
    if (d.inDays == 1) return 'yesterday';
    return '${d.inDays} days ago';
  }

  @override
  void dispose() {
    _soon?.cancel();
    _every?.cancel();
    _api?.close();
    super.dispose();
  }
}
