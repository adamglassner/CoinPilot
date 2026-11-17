import 'dart:convert';
import 'dart:io';
import 'dart:isolate';
import 'dart:typed_data';

import 'package:archive/archive.dart';
import 'package:cryptography/cryptography.dart';

import 'records.dart';
import 'store.dart';

/// Backups in the same .zip format as WillowWallet for Windows (Settings → Back up): one JSON file per kind of record
/// (transactions.json, bills.json…), preferences.json and debt-plan.json, receipts/ and taxes/<year>/.
/// A backup made on the PC restores here and a backup made here restores on the PC. A PC backup made with PIN or
/// password protection on is encrypted; it opens here with the same PIN, password or recovery key.
class Backup {
  static const _files = {
    'transactions': 'transactions.json',
    'budgets': 'budgets.json',
    'accounts': 'accounts.json',
    'debts': 'debts.json',
    'bills': 'bills.json',
    'goals': 'goals.json',
    'categories': 'categories.json',
    'rules': 'rules.json',
    'income': 'income.json',
    'tax-documents': 'tax-documents.json',
  };

  static String suggestedName(DateTime now) =>
      'WillowWallet backup ${now.year}-${now.month.toString().padLeft(2, '0')}-${now.day.toString().padLeft(2, '0')}.zip';

  // ----- Making a backup -----

  /// Everything as a .zip the Windows app can restore.
  static Future<Uint8List> create(Store store) async {
    final archive = Archive();
    void add(String name, List<int> bytes) => archive.addFile(ArchiveFile(name, bytes.length, bytes));
    const indent = JsonEncoder.withIndent('  ');
    for (final MapEntry(key: collection, value: file) in _files.entries) {
      final ids = [for (final (id, _) in store.syncEntries(collection)) id];
      final list = [for (final id in ids) store.syncRecord(collection, id)!];
      add(file, utf8.encode(indent.convert(list)));
    }
    add('preferences.json', utf8.encode(indent.convert(_settingsFile(store.prefs.j))));
    add('debt-plan.json', utf8.encode(indent.convert(_settingsFile(store.debtPlan.j))));
    for (final t in store.transactions.where((t) => t.receipt.isNotEmpty)) {
      final path = store.receiptPath(t.receipt);
      if (path != null && await File(path).exists()) add('receipts/${t.receipt}', await File(path).readAsBytes());
    }
    for (final d in store.taxDocs.where((d) => d.fileName.isNotEmpty)) {
      final path = store.taxPath(d);
      if (path != null && await File(path).exists()) add('taxes/${d.year}/${d.fileName}', await File(path).readAsBytes());
    }
    return Uint8List.fromList(ZipEncoder().encode(archive)!);
  }

  /// A settings file the way the PC saves it (no Id; never-changed settings get a real date).
  static Map<String, dynamic> _settingsFile(Map<String, dynamic> j) {
    final copy = Map<String, dynamic>.from(j)..remove('Id');
    if ('${copy['UpdatedAt']}'.startsWith('0001')) copy['UpdatedAt'] = stamp();
    return copy;
  }

  // ----- Restoring -----

  /// Reads a backup. Throws [BackupLocked] when it's protected and [secret] is missing or wrong,
  /// and [FormatException] when it isn't a WillowWallet backup.
  static Future<RestoredBackup> read(Uint8List zip, {String? secret}) async {
    late Archive archive;
    try {
      archive = ZipDecoder().decodeBytes(zip);
    } catch (_) {
      throw const FormatException('That file isn\'t a valid .zip file.');
    }
    final files = <String, List<int>>{};
    for (final f in archive.files) {
      if (!f.isFile) continue;
      final name = f.name.replaceAll('\\', '/');
      if (name.contains('..') || name.startsWith('/')) throw const FormatException('That backup contains unsafe file names.');
      files[name] = f.content as List<int>;
    }
    const known = ['transactions.json', 'budgets.json', 'accounts.json', 'debts.json', 'bills.json', 'goals.json', 'categories.json', 'preferences.json', 'tax-documents.json'];
    if (!files.keys.any((k) => known.contains(k.toLowerCase()))) throw const FormatException('That file isn\'t a WillowWallet backup.');

    // Protected on the PC: unlock the data key with the PIN, password or recovery key.
    List<int>? key;
    final lock = files['lock.json'];
    if (lock != null) {
      final info = jsonDecode(utf8.decode(lock)) as Map<String, dynamic>;
      final kind = info['Kind'] as String? ?? 'Password';
      if (secret == null || secret.isEmpty) throw BackupLocked(kind, wrong: false);
      key = await _unlock(info, secret);
      if (key == null) throw BackupLocked(kind, wrong: true);
    }

    Future<List<int>> plain(List<int> bytes) async => key != null && _isEncrypted(bytes) ? await _decrypt(bytes, key) : bytes;

    final data = <String, List<Map<String, dynamic>>>{};
    for (final MapEntry(key: collection, value: file) in _files.entries) {
      final bytes = files[file];
      if (bytes == null) continue;
      final list = jsonDecode(utf8.decode(await plain(bytes), allowMalformed: true));
      if (list is List) data[collection] = [for (final r in list) if (r is Map) Map<String, dynamic>.from(r)];
    }
    final settings = <Map<String, dynamic>>[];
    for (final name in ['preferences', 'debt-plan']) {
      final bytes = files['$name.json'];
      if (bytes == null) continue;
      final j = jsonDecode(utf8.decode(await plain(bytes), allowMalformed: true));
      if (j is Map) settings.add({...Map<String, dynamic>.from(j), 'Id': name, 'UpdatedAt': stamp()});
    }
    data['settings'] = settings;

    final attachments = <String, List<int>>{};
    for (final MapEntry(:key, :value) in files.entries) {
      if (key.startsWith('receipts/') || key.startsWith('taxes/')) attachments[key] = await plain(value);
    }
    return RestoredBackup(data, attachments, protected: lock != null);
  }

  /// Replaces everything on this phone with the backup (receipts and tax documents included).
  static Future<void> restore(Store store, RestoredBackup backup) async {
    final dir = store.filesDir;
    if (dir != null) {
      for (final folder in ['receipts', 'taxes']) {
        final d = Directory('${dir.path}/$folder');
        if (await d.exists()) await d.delete(recursive: true);
      }
      for (final MapEntry(:key, :value) in backup.files.entries) {
        final f = File('${dir.path}/$key');
        await f.parent.create(recursive: true);
        await f.writeAsBytes(value, flush: true);
      }
    }
    store.replaceAll(backup.data);
  }

  // ----- The PC's protection (Data/DataLock.cs) -----

  static final _magic = ascii.encode('CPENC1');

  static bool _isEncrypted(List<int> bytes) {
    if (bytes.length < _magic.length) return false;
    for (var i = 0; i < _magic.length; i++) {
      if (bytes[i] != _magic[i]) return false;
    }
    return true;
  }

  /// "CPENC1" ‖ nonce(12) ‖ tag(16) ‖ ciphertext, AES-256-GCM.
  static Future<List<int>> _decrypt(List<int> data, List<int> key) async {
    final o = _magic.length;
    final box = SecretBox(data.sublist(o + 28), nonce: data.sublist(o, o + 12), mac: Mac(data.sublist(o + 12, o + 28)));
    return AesGcm.with256bits(nonceLength: 12).decrypt(box, secretKey: SecretKey(key));
  }

  static Future<List<int>?> _unlock(Map<String, dynamic> info, String secret) async {
    final iterations = (info['Iterations'] as num?)?.toInt() ?? 600000;
    final candidates = <(String, String, String)>[
      (secret, info['Salt'] as String? ?? '', info['WrappedKey'] as String? ?? ''),
      // The recovery key: letters and digits only, upper case
      (secret.toUpperCase().replaceAll(RegExp('[^A-Z0-9]'), ''), info['RecoverySalt'] as String? ?? '', info['RecoveryWrappedKey'] as String? ?? ''),
    ];
    for (final (s, salt, wrapped) in candidates) {
      if (s.isEmpty || salt.isEmpty || wrapped.isEmpty) continue;
      final wrapping = await Isolate.run(() => _derive(s, base64.decode(salt), iterations));
      try {
        return await _decrypt(base64.decode(wrapped), wrapping);
      } catch (_) {
        // wrong secret: try the next one
      }
    }
    return null;
  }

  static Future<List<int>> _derive(String secret, List<int> salt, int iterations) async {
    final k = await Pbkdf2(macAlgorithm: Hmac.sha256(), iterations: iterations, bits: 256).deriveKey(secretKey: SecretKey(utf8.encode(secret)), nonce: salt);
    return k.extractBytes();
  }
}

class RestoredBackup {
  RestoredBackup(this.data, this.files, {required this.protected});
  final Map<String, List<Map<String, dynamic>>> data;
  final Map<String, List<int>> files;
  final bool protected;

  int count(String collection) => data[collection]?.length ?? 0;
}

/// The backup was made with PIN/password protection on the PC.
class BackupLocked implements Exception {
  BackupLocked(this.kind, {required this.wrong});
  final String kind;
  final bool wrong;
}
