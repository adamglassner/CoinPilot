// Renders every screen to test/screenshots/*.png (run with --update-goldens). CI publishes them.
import 'dart:convert';

import 'package:coinpilot/main.dart';
import 'package:coinpilot/records.dart';
import 'package:coinpilot/screens/accounts_screen.dart';
import 'package:coinpilot/screens/bills_screen.dart';
import 'package:coinpilot/screens/categories_screen.dart';
import 'package:coinpilot/screens/debts_screen.dart';
import 'package:coinpilot/screens/goals_screen.dart';
import 'package:coinpilot/screens/import_screen.dart';
import 'package:coinpilot/screens/income_screen.dart';
import 'package:coinpilot/screens/reports_screen.dart';
import 'package:coinpilot/screens/settings_screen.dart';
import 'package:coinpilot/screens/taxes_screen.dart';
import 'package:coinpilot/updater.dart';
import 'package:coinpilot/screens/sync_screen.dart';
import 'package:coinpilot/store.dart';
import 'package:coinpilot/sync/engine.dart';
import 'package:coinpilot/sync/scope.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'fonts.dart';

void main() {
  setUpAll(loadAppFonts);

  Future<void> shot(
    WidgetTester tester,
    String name, {
    String mode = 'light',
    String theme = 'Pine',
    int tab = 0,
    bool sample = true,
    bool synced = false,
    bool setup = false,
    void Function(Store store)? prepare,
    UpdateInfo? update,
    Future<void> Function()? then,
  }) async {
    debugDisableShadows = false;
    EditableText.debugDeterministicCursor = true;
    try {
      tester.view.physicalSize = const Size(1170, 2532); // 390 × 844 at 3×
      tester.view.devicePixelRatio = 3;
      addTearDown(tester.view.reset);
      final store = Store.memory();
      store.settings
        ..themeMode = mode
        ..colorTheme = theme;
      if (sample) store.loadSample();
      prepare?.call(store);
      final updater = Updater(store, client: MockClient((_) async => http.Response('[]', 200)));
      updater.available = update;
      final sync = SyncEngine(store, appVersion: '0.2.0', automatic: false, httpClient: () => _pretendServer());
      if (synced) {
        store.syncState = {
          'url': 'http://192.168.1.20:8484/',
          'serverName': 'Glassner Family Server',
          'username': 'adam',
          'token': 'screenshot',
          'key': base64.encode(List.filled(32, 7)),
          'lastSeq': 120,
          'lastSync': DateTime.now().toIso8601String(),
          'synced': {
            for (final c in Store.editable)
              for (final (id, ms) in store.syncEntries(c)) '$c/$id': ms,
          },
        };
      }
      sync.start();
      await tester.pumpWidget(CoinPilotApp(store: store, sync: sync, updater: updater, initialTab: tab, showSetup: setup));
      await tester.runAsync(() async {
        await precacheImage(const AssetImage('assets/coinpilot.png'), tester.element(find.byType(MaterialApp)));
      });
      await tester.pumpAndSettle();
      if (then != null) await then();
      await expectLater(find.byType(CoinPilotApp), matchesGoldenFile('screenshots/$name.png'));
    } finally {
      debugDisableShadows = true;
      EditableText.debugDeterministicCursor = false;
    }
  }

  Future<void> push(WidgetTester tester, Widget page) async {
    Navigator.of(tester.element(find.byType(AppShell))).push(MaterialPageRoute<void>(builder: (_) => page));
    await tester.pumpAndSettle();
  }

  Future<void> scrollTo(WidgetTester tester, Finder f) async {
    await tester.scrollUntilVisible(f, 250, scrollable: find.byType(Scrollable).last);
    await tester.pumpAndSettle();
  }

  Future<void> openSync(WidgetTester tester) async {
    await tester.tap(find.byType(SyncPill));
    await tester.pumpAndSettle();
  }

  Future<void> openAdd(WidgetTester tester) async {
    await tester.tap(find.byTooltip('Add transaction'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField).at(0), '42.18');
    await tester.enterText(find.byType(TextField).at(1), 'Fresh Thyme Market');
    await tester.pumpAndSettle();
  }

  for (final mode in ['light', 'dark']) {
    final n = mode == 'light' ? 0 : 10;
    String id(int i, String what) => '${(n + i).toString().padLeft(2, '0')}-$what-$mode';
    testWidgets('home $mode', (t) => shot(t, id(1, 'home'), mode: mode));
    testWidgets('transactions $mode', (t) => shot(t, id(2, 'transactions'), mode: mode, tab: 1));
    testWidgets('budgets $mode', (t) => shot(t, id(3, 'budgets'), mode: mode, tab: 2));
    testWidgets('more $mode', (t) => shot(t, id(4, 'more'), mode: mode, tab: 3));
    testWidgets('add $mode', (t) => shot(t, id(5, 'add-transaction'), mode: mode, then: () => openAdd(t)));
    testWidgets('budget $mode', (t) => shot(t, id(6, 'edit-budget'), mode: mode, tab: 2, then: () async {
          await t.tap(find.text('Dining out').first);
          await t.pumpAndSettle();
        }));
    testWidgets('erase $mode', (t) => shot(t, id(7, 'confirm-delete-all'), mode: mode, then: () async {
          await push(t, const SettingsScreen());
          await t.scrollUntilVisible(find.text('Delete all data'), 300, scrollable: find.byType(Scrollable).last);
          await t.pumpAndSettle();
          await t.tap(find.text('Delete all data'));
          await t.pumpAndSettle();
        }));
    testWidgets('welcome $mode', (t) => shot(t, id(8, 'welcome'), mode: mode, sample: false));
    testWidgets('sync connect $mode', (t) => shot(t, id(31, 'sync-connect'), mode: mode, then: () async {
          await openSync(t);
          await t.enterText(find.byType(TextField).first, '192.168.1.20:8484');
          await t.pump(const Duration(seconds: 1));
          await t.pumpAndSettle();
          await t.enterText(find.byType(TextField).at(1), 'adam');
          await t.enterText(find.byType(TextField).at(2), 'correct horse battery');
          FocusManager.instance.primaryFocus?.unfocus();
          await t.pumpAndSettle();
        }));
    testWidgets('sync connected $mode', (t) => shot(t, id(32, 'sync-connected'), mode: mode, synced: true, then: () => openSync(t)));
    testWidgets('home synced $mode', (t) => shot(t, id(33, 'home-synced'), mode: mode, synced: true));
  }
  // Everything added in 0.3 (light 1xx, dark 2xx)
  void prepareExtras(Store s) {
    final y = DateTime.now().year - 1;
    s.put(TaxDoc.create(year: y, title: 'W-2 from Acme Corp', kind: 'W-2', fileName: 'w2.pdf', originalName: 'Acme W-2 $y.pdf', size: 184320));
    s.put(TaxDoc.create(year: y, title: '1099-INT from Ally', kind: '1099', fileName: 'int.pdf', originalName: 'ally-1099-int.pdf', size: 92160));
    s.put(TaxDoc.create(year: y, title: 'Donation to the food bank', kind: 'Charitable donation', fileName: 'd.jpg', originalName: 'receipt.jpg', size: 1258291));
    s.put(Rule.create(contains: 'STARBUCKS', category: 'Dining out'));
  }

  for (final mode in ['light', 'dark']) {
    final n = mode == 'light' ? 100 : 200;
    String id(int i, String what) => '${n + i}-$what-$mode';
    Future<void> page(WidgetTester t, String name, int i, Widget w, {Future<void> Function()? then, void Function(Store)? prepare}) =>
        shot(t, id(i, name), mode: mode, prepare: (s) {
          prepareExtras(s);
          prepare?.call(s);
        }, then: () async {
          await push(t, w);
          if (then != null) await then();
        });

    testWidgets('accounts $mode', (t) => page(t, 'accounts', 1, const AccountsScreen()));
    testWidgets('account sheet $mode', (t) => page(t, 'add-account', 2, const AccountsScreen(), then: () async {
          await t.tap(find.text('Add account'));
          await t.pumpAndSettle();
        }));
    testWidgets('bills $mode', (t) => page(t, 'bills', 3, const BillsScreen()));
    testWidgets('bills lower $mode', (t) => page(t, 'bills-found', 4, const BillsScreen(), then: () => scrollTo(t, find.text('Found in your transactions'))));
    testWidgets('bills calendar $mode', (t) => page(t, 'bills-calendar', 5, const BillsScreen(), then: () async {
          await t.tap(find.text('Calendar'));
          await t.pumpAndSettle();
        }));
    testWidgets('bill actions $mode', (t) => page(t, 'bill-actions', 6, const BillsScreen(), then: () async {
          await t.tap(find.text('Water & sewer'));
          await t.pumpAndSettle();
        }));
    testWidgets('pay bill $mode', (t) => page(t, 'pay-bill', 7, const BillsScreen(), then: () async {
          await t.tap(find.text('Water & sewer'));
          await t.pumpAndSettle();
          await t.tap(find.text('Mark paid'));
          await t.pumpAndSettle();
        }));
    testWidgets('add bill $mode', (t) => page(t, 'add-bill', 8, const BillsScreen(), then: () async {
          await t.tap(find.text('Add bill'));
          await t.pumpAndSettle();
        }));
    testWidgets('income $mode', (t) => page(t, 'income', 9, const IncomeScreen()));
    testWidgets('payday $mode', (t) => page(t, 'payday', 10, const IncomeScreen(), prepare: (s) {
          final d = s.incomeSources.firstWhere((x) => x.varies);
          s.put(d.copy()..nextDate = DateTime.now().subtract(const Duration(days: 1)));
        }, then: () async {
          await t.tap(find.textContaining('Payday:'));
          await t.pumpAndSettle();
        }));
    testWidgets('add income $mode', (t) => page(t, 'add-income', 11, const IncomeScreen(), then: () async {
          await t.tap(find.text('Add income').last);
          await t.pumpAndSettle();
        }));
    testWidgets('debts $mode', (t) => page(t, 'debts', 12, const DebtsScreen()));
    testWidgets('debts lower $mode', (t) => page(t, 'debts-list', 13, const DebtsScreen(), then: () => scrollTo(t, find.text('In plan order'))));
    testWidgets('add debt $mode', (t) => page(t, 'add-debt', 14, const DebtsScreen(), then: () async {
          await t.tap(find.text('Add debt'));
          await t.pumpAndSettle();
        }));
    testWidgets('goals $mode', (t) => page(t, 'goals', 15, const GoalsScreen()));
    testWidgets('goal actions $mode', (t) => page(t, 'goal-actions', 16, const GoalsScreen(), then: () async {
          await t.tap(find.text('Hawaii trip'));
          await t.pumpAndSettle();
        }));
    testWidgets('reports $mode', (t) => page(t, 'reports', 17, const ReportsScreen()));
    testWidgets('reports lower $mode', (t) => page(t, 'reports-categories', 18, const ReportsScreen(), then: () => scrollTo(t, find.text('Spending by category'))));
    testWidgets('year $mode', (t) => page(t, 'year-in-review', 19, YearReviewScreen(year: DateTime.now().year)));
    testWidgets('taxes $mode', (t) => page(t, 'taxes', 20, const TaxesScreen()));
    testWidgets('import $mode', (t) => page(t, 'import', 21, const ImportScreen(initialName: 'Chase-Visa.csv', initialText: _csv), then: () => t.pumpAndSettle()));
    testWidgets('cuts $mode', (t) => shot(t, id(22, 'budgets-cuts'), mode: mode, tab: 2, then: () => scrollTo(t, find.text('Subscriptions you could cut'))));
    testWidgets('skip menu $mode', (t) => shot(t, id(23, 'budgets-skip-menu'), mode: mode, tab: 2, then: () async {
          await scrollTo(t, find.byTooltip('Skip Netflix'));
          await t.tap(find.byTooltip('Skip Netflix'));
          await t.pumpAndSettle();
        }));
    testWidgets('template $mode', (t) => shot(t, id(24, 'budget-template'), mode: mode, tab: 2, then: () async {
          await t.tap(find.byTooltip('More'));
          await t.pumpAndSettle();
          await t.tap(find.text('Use a template…'));
          await t.pumpAndSettle();
        }));
    testWidgets('split $mode', (t) => shot(t, id(25, 'split-transaction'), mode: mode, then: () async {
          await openAdd(t);
          await t.tap(find.text('Split'));
          await t.pumpAndSettle();
        }));
    testWidgets('transfer $mode', (t) => shot(t, id(26, 'transfer'), mode: mode, then: () async {
          await t.tap(find.byTooltip('Add transaction'));
          await t.pumpAndSettle();
          await t.tap(find.text('Transfer'));
          await t.pumpAndSettle();
          await t.enterText(find.byType(TextField).first, '400');
          await t.tap(find.text('High-Yield Savings').last);
          await t.pumpAndSettle();
        }));
    testWidgets('settings $mode', (t) => page(t, 'settings', 27, const SettingsScreen()));
    testWidgets('settings lower $mode', (t) => page(t, 'settings-data', 28, const SettingsScreen(), then: () => scrollTo(t, find.text('Check for updates'))));
    testWidgets('categories $mode', (t) => page(t, 'categories', 29, const CategoriesScreen()));
    testWidgets('get started $mode', (t) => shot(t, id(30, 'get-started'), mode: mode, sample: false, setup: true));
    testWidgets('get started income $mode', (t) => shot(t, id(31, 'get-started-income'), mode: mode, sample: false, setup: true, then: () async {
          await t.tap(find.text('Set up step by step'));
          await t.pumpAndSettle();
          await t.tap(find.text('Next'));
          await t.pumpAndSettle();
          await t.enterText(find.byType(TextField).first, '2186.40');
          FocusManager.instance.primaryFocus?.unfocus();
          await t.pumpAndSettle();
        }));
    testWidgets('lock $mode', (t) => shot(t, id(32, 'lock'), mode: mode, prepare: (s) => s.updateSettings((x) => x
      ..lockKind = 'PIN'
      ..lockSalt = 'AAAA'
      ..lockHash = 'x'), then: () async {
          await t.tap(find.text('1').last);
          await t.tap(find.text('2').last);
          await t.pumpAndSettle();
        }));
    testWidgets('update $mode', (t) => shot(t, id(33, 'update-available'), mode: mode, tab: 3,
        update: UpdateInfo('0.3.1', 'https://example.com/a.apk', 'WillowWallet-Android-0.3.1.apk', 31457280, '', 'https://github.com')));
  }

  testWidgets('sync recovery key', (t) => shot(t, '34-sync-recovery-key-light', then: () async {
        await openSync(t);
        showRecoveryKey(t.element(find.byType(SyncScreen)), 'GIZT-INJW-G44D-SOR3-HQ6T-4P2A-IFBE-GRCF');
        await t.pumpAndSettle();
      }));
  testWidgets('sync combine', (t) => shot(t, '35-sync-combine-dark', mode: 'dark', then: () async {
        await openSync(t);
        showMergeChoice(t.element(find.byType(SyncScreen)));
        await t.pumpAndSettle();
      }));
  for (final (i, theme, mode) in [
    (21, 'Ocean', 'light'),
    (22, 'Plum', 'light'),
    (23, 'Ember', 'light'),
    (24, 'Lagoon', 'dark'),
    (25, 'Rose', 'dark'),
    (26, 'Graphite', 'dark'),
  ]) {
    testWidgets('theme $theme', (t) => shot(t, '$i-theme-${theme.toLowerCase()}-$mode', mode: mode, theme: theme));
  }
}

/// Answers like a WillowWallet Server would, for screenshots.
http.Client _pretendServer() => MockClient((request) async {
      final now = DateTime.now().toUtc();
      final body = switch (request.url.path) {
        '/api/v1/info' => {'name': 'Glassner Family Server', 'version': '0.1.0', 'openSignup': false, 'syncing': true, 'protocolVersion': 1},
        '/api/v1/account' => {
            'username': 'adam',
            'records': 412,
            'fileBytes': 0,
            'devices': [
              {'id': 'd1', 'name': 'Android phone', 'platform': 'Android', 'appVersion': '0.2.0', 'lastSeen': now.toIso8601String(), 'isThisDevice': true},
              {'id': 'd2', 'name': 'ADAM-DESKTOP', 'platform': 'Windows', 'appVersion': '1.1.0',
                'lastSeen': now.subtract(const Duration(minutes: 4)).toIso8601String(), 'isThisDevice': false},
              {'id': 'd3', 'name': 'SURFACE-LAPTOP', 'platform': 'Windows', 'appVersion': '1.1.0',
                'lastSeen': now.subtract(const Duration(days: 1, hours: 2)).toIso8601String(), 'isThisDevice': false},
            ],
          },
        _ => {'error': 'not_found', 'message': 'Not found'},
      };
      return http.Response(jsonEncode(body), body['error'] == null ? 200 : 404, headers: {'content-type': 'application/json'});
    });

const _csv = 'Transaction Date,Post Date,Description,Category,Type,Amount,Memo\n'
    '11/14/2026,11/15/2026,STARBUCKS STORE 1182,Food & Drink,Sale,-6.45,\n'
    '11/13/2026,11/14/2026,KROGER #0457,Groceries,Sale,-84.12,\n'
    '11/12/2026,11/13/2026,SHELL OIL 5512,Gas,Sale,-41.30,\n'
    '11/11/2026,11/12/2026,NETFLIX.COM,Bills & Utilities,Sale,-15.49,\n'
    '11/10/2026,11/11/2026,TARGET T-1034,Shopping,Sale,-62.18,\n'
    '11/09/2026,11/10/2026,Payment Thank You-Mobile,,Payment,500.00,\n'
    '11/08/2026,11/09/2026,CHIPOTLE 2210,Food & Drink,Sale,-14.85,\n';
