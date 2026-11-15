import 'dart:convert';
import 'dart:io';

import 'package:coinpilot/models.dart';
import 'package:coinpilot/store.dart';
import 'package:coinpilot/sync/api.dart';
import 'package:coinpilot/sync/crypto.dart';
import 'package:coinpilot/sync/engine.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;

/// Sync tests. The crypto ones check this app against the same reference values as the server and the
/// Windows app (server/test-vectors.json). The end-to-end ones run against a real WillowWallet Server
/// when WW_TEST_SERVER and WW_TEST_ADMIN_PASSWORD are set (the Android workflow starts one).
void main() {
  group('Sync crypto matches the server and Windows app', () {
    final v = jsonDecode(File('../server/test-vectors.json').readAsStringSync()) as Map<String, dynamic>;
    List<int> b(String k) => base64.decode(v[k] as String);

    test('password → auth secret and key', () async {
      final c = await SyncCrypto.fromPassword(v['password'], b('kdfSalt'), v['iterations']);
      expect(base64.encode(c.authSecret), v['authSecret']);
      expect(base64.encode(c.keyEncryptionKey), v['kek']);
    });

    test('wrapped data key and record payload', () async {
      expect(await SyncCrypto.seal(b('kek'), b('dataKey'), SyncCrypto.keyAad, nonce: b('wrapNonce')), v['wrappedKey']);
      expect(base64.encode(await SyncCrypto.open(b('kek'), v['wrappedKey'], SyncCrypto.keyAad)), v['dataKey']);
      final aad = SyncCrypto.recordAad('transactions', 'abc');
      expect(await SyncCrypto.seal(b('dataKey'), utf8.encode(v['record']), aad, nonce: b('recordNonce')), v['recordPayload']);
      final opened = await SyncCrypto.openRecord(b('dataKey'), 'transactions', 'abc', v['recordPayload']);
      expect(opened['Payee'], 'Kroger');
      // The wrong record id doesn't open (each payload is tied to its record)
      expect(() => SyncCrypto.openRecord(b('dataKey'), 'transactions', 'xyz', v['recordPayload']), throwsA(anything));
    });

    test('recovery key', () async {
      expect(SyncCrypto.formatRecoveryKey(b('recoveryKeyBytes')), v['recoveryKeyText']);
      expect(base64.encode(SyncCrypto.parseRecoveryKey(v['recoveryKeyText'].toString().toLowerCase().replaceAll('-', ' '))!),
          v['recoveryKeyBytes']);
      expect(SyncCrypto.parseRecoveryKey('not a key'), isNull);
      final r = await SyncCrypto.fromRecoveryKey(b('recoveryKeyBytes'));
      expect(base64.encode(r.authSecret), v['recoveryAuth']);
      expect(base64.encode(r.keyEncryptionKey), v['recoveryKek']);
    });

    test('server addresses', () {
      expect(SyncApi.normalizeUrl('192.168.1.20:8484').toString(), 'http://192.168.1.20:8484/');
      expect(SyncApi.normalizeUrl('https://ww.example.com').toString(), 'https://ww.example.com/');
      expect(() => SyncApi.normalizeUrl('ftp://x'), throwsA(isA<SyncException>()));
    });
  });

  final server = Platform.environment['WW_TEST_SERVER'];
  final adminPassword = Platform.environment['WW_TEST_ADMIN_PASSWORD'];
  group('Sync with a real server', () {
    const iterations = 100000; // the server's minimum, so the test runs quickly
    final user = 'phone${DateTime.now().millisecondsSinceEpoch % 100000000}';
    const password = 'phone-test-password';
    late Store storeA, storeB;
    late SyncEngine a, b;
    late String recovery;

    Future<String> invite() async {
      final http.Client c = http.Client();
      final base = Uri.parse(server!);
      final login = await c.post(base.resolve('/admin/api/login'),
          headers: {'Content-Type': 'application/json', 'X-WillowWallet': 'admin'},
          body: jsonEncode({'username': 'admin', 'password': adminPassword}));
      expect(login.statusCode, 200, reason: login.body);
      final cookie = login.headers['set-cookie']!.split(';').first;
      final r = await c.post(base.resolve('/admin/api/invites'),
          headers: {'Content-Type': 'application/json', 'X-WillowWallet': 'admin', 'Cookie': cookie},
          body: jsonEncode({'uses': 1, 'days': 1}));
      expect(r.statusCode, 200, reason: r.body);
      c.close();
      return (jsonDecode(r.body) as Map<String, dynamic>)['code'] as String;
    }

    Txn find(Store s, String id) => s.transactions.firstWhere((t) => t.id == id);

    setUpAll(() async {
      storeA = Store.memory()..loadSample();
      storeB = Store.memory();
      a = SyncEngine(storeA, appVersion: 'test', automatic: false);
      b = SyncEngine(storeB, appVersion: 'test', automatic: false);
    });

    test('create an account on phone A and send its data', () async {
      final code = await invite();
      recovery = (await a.connect(server!, user, password, create: true, invite: code, iterations: iterations))!;
      expect(SyncCrypto.parseRecoveryKey(recovery), isNotNull);
      expect(a.isConnected, isTrue);
      expect(await a.firstSync(useServerOnly: false), isTrue, reason: a.problem);
      expect(a.pendingCount, 0);
      expect(a.status, SyncStatus.idle);
    });

    test('wrong password is refused', () async {
      final other = SyncEngine(Store.memory(), automatic: false);
      await expectLater(other.connect(server!, user, 'not-the-password'), throwsA(isA<SyncException>()));
    });

    test('phone B signs in and gets everything', () async {
      storeB.saveTxn(Txn(payee: 'Only on B (replaced)', category: 'Dining', account: 'Checking', date: DateTime.now(), cents: -500));
      await b.connect(server!, user, password);
      expect(await b.firstSync(useServerOnly: true), isTrue, reason: b.problem);
      expect(storeB.transactions.length, storeA.transactions.length);
      expect(storeB.budgets.length, storeA.budgets.length);
      expect(storeB.transactions.any((t) => t.payee.startsWith('Only on B')), isFalse);
    });

    test('edits and deletes travel both ways', () async {
      final t = storeB.transactions.first;
      storeB.saveTxn(Txn.fromJson({...t.toJson(), 'Payee': 'Edited on B', 'UpdatedAt': DateTime.now().toUtc().toIso8601String()}));
      final added = Txn(payee: 'Added on B', category: 'Groceries', account: 'Checking', date: DateTime.now(), cents: -4210);
      storeB.saveTxn(added);
      expect(b.pendingCount, 2);
      expect(await b.syncNow(manual: true), isTrue, reason: b.problem);
      expect(await a.syncNow(manual: true), isTrue, reason: a.problem);
      expect(find(storeA, t.id).payee, 'Edited on B');
      expect(find(storeA, added.id).cents, -4210);

      storeA.deleteTxn(added.id);
      expect(await a.syncNow(manual: true), isTrue);
      expect(await b.syncNow(manual: true), isTrue);
      expect(storeB.transactions.any((x) => x.id == added.id), isFalse);
      expect(a.pendingCount, 0);
      expect(b.pendingCount, 0);
    });

    test('newest edit wins', () async {
      final t = storeA.transactions[1];
      storeA.saveTxn(Txn.fromJson({...t.toJson(), 'Payee': 'Older edit on A', 'UpdatedAt': DateTime.now().toUtc().toIso8601String()}));
      await Future<void>.delayed(const Duration(milliseconds: 20));
      storeB.saveTxn(Txn.fromJson({...t.toJson(), 'Payee': 'Newer edit on B', 'UpdatedAt': DateTime.now().toUtc().toIso8601String()}));
      // B sends first, then A's older edit is refused as stale and replaced by B's
      expect(await b.syncNow(manual: true), isTrue);
      expect(await a.syncNow(manual: true), isTrue);
      expect(find(storeA, t.id).payee, 'Newer edit on B');
      expect(await a.syncNow(manual: true), isTrue);
      expect(await b.syncNow(manual: true), isTrue);
      expect(find(storeB, t.id).payee, 'Newer edit on B');
    });

    test('records from the PC keep the fields this app doesn\'t edit', () async {
      // Push what the Windows app would: an account and a split transaction with tags
      final api = SyncApi(server!);
      final signedIn = await SyncAccount.login(api, user, password, SyncApi.device('Test PC', 'test'));
      final key = signedIn.dataKey;
      final now = DateTime.now();
      final pcTxn = {
        'Id': 'pc-split-1',
        'Payee': 'Costco',
        'Category': 'Groceries',
        'Account': 'Visa',
        'Date': '${now.toIso8601String().substring(0, 10)}T00:00:00',
        'Amount': -120.0,
        'Note': '',
        'Tags': ['bulk'],
        'Splits': [
          {'Category': 'Groceries', 'Amount': -80.0},
          {'Category': 'Household', 'Amount': -40.0}
        ],
        'Receipt': '',
        'CreatedAt': now.toIso8601String(),
        'UpdatedAt': now.toIso8601String(),
      };
      final account = {'Id': 'acct-visa', 'Name': 'Visa', 'Kind': 'Credit', 'UpdatedAt': now.toIso8601String()};
      final ms = now.millisecondsSinceEpoch;
      await api.push([
        {'collection': 'transactions', 'id': 'pc-split-1', 'modified': ms, 'deleted': false,
          'payload': await SyncCrypto.sealRecord(key, 'transactions', 'pc-split-1', pcTxn)},
        {'collection': 'accounts', 'id': 'acct-visa', 'modified': ms, 'deleted': false,
          'payload': await SyncCrypto.sealRecord(key, 'accounts', 'acct-visa', account)},
        {'collection': 'settings', 'id': 'preferences', 'modified': ms, 'deleted': false,
          'payload': await SyncCrypto.sealRecord(key, 'settings', 'preferences', {'Theme': 'Dark'})},
      ]);

      expect(await b.syncNow(manual: true), isTrue, reason: b.problem);
      final t = find(storeB, 'pc-split-1');
      expect(t.isSplit, isTrue);
      expect(storeB.accounts, contains('Visa'));

      // Editing the note on the phone keeps the splits and tags for the PC
      storeB.saveTxn(Txn.fromJson({...t.toJson(), 'Note': 'from the phone', 'UpdatedAt': DateTime.now().toUtc().toIso8601String()}));
      expect(await b.syncNow(manual: true), isTrue);
      var since = 0, found = <String, dynamic>{};
      while (true) {
        final (changes, _, more) = await api.changes(since);
        for (final c in changes) {
          if (c.collection == 'transactions' && c.id == 'pc-split-1' && c.payload != null) {
            found = await SyncCrypto.openRecord(key, c.collection, c.id, c.payload!);
          }
          since = c.seq;
        }
        if (!more) break;
      }
      expect(found['Note'], 'from the phone');
      expect(found['Tags'], ['bulk']);
      expect((found['Splits'] as List).length, 2);
      await api.logout();
      api.close();
    });

    test('forgot password: the recovery key sets a new one', () async {
      await b.recover(server!, user, recovery, 'a-brand-new-password', iterations: iterations);
      expect(b.isConnected, isTrue);
      expect(await b.syncNow(manual: true), isTrue, reason: b.problem);
      // Phone A was signed out by the reset and the old password no longer works
      expect(await a.syncNow(manual: true), isFalse);
      expect(a.status, SyncStatus.signedOut);
      final other = SyncEngine(Store.memory(), automatic: false);
      await expectLater(other.connect(server!, user, password), throwsA(isA<SyncException>()));
      await a.connect(server!, user, 'a-brand-new-password');
      expect(await a.syncNow(manual: true), isTrue, reason: a.problem);
    });

    test('stopping sync keeps the data on the phone', () async {
      final count = storeA.transactions.length;
      await a.signOut();
      expect(a.isSetUp, isFalse);
      expect(a.status, SyncStatus.off);
      expect(storeA.transactions.length, count);
      expect(storeA.syncState, isEmpty);
    });
  }, skip: server == null || adminPassword == null ? 'Set WW_TEST_SERVER and WW_TEST_ADMIN_PASSWORD to run' : null);
}
