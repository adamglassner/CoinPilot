// Renders every screen to test/screenshots/*.png (run with --update-goldens). CI publishes them.
import 'dart:convert';

import 'package:coinpilot/main.dart';
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
      await tester.pumpWidget(CoinPilotApp(store: store, sync: sync, initialTab: tab));
      await tester.runAsync(() async {
        await precacheImage(const AssetImage('assets/coinpilot.png'), tester.element(find.byType(AppShell)));
      });
      await tester.pumpAndSettle();
      if (then != null) await then();
      await expectLater(find.byType(CoinPilotApp), matchesGoldenFile('screenshots/$name.png'));
    } finally {
      debugDisableShadows = true;
      EditableText.debugDeterministicCursor = false;
    }
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
    testWidgets('settings $mode', (t) => shot(t, id(4, 'settings'), mode: mode, tab: 3));
    testWidgets('add $mode', (t) => shot(t, id(5, 'add-transaction'), mode: mode, then: () => openAdd(t)));
    testWidgets('budget $mode', (t) => shot(t, id(6, 'edit-budget'), mode: mode, tab: 2, then: () async {
          await t.tap(find.text('Dining out').first);
          await t.pumpAndSettle();
        }));
    testWidgets('erase $mode', (t) => shot(t, id(7, 'confirm-erase'), mode: mode, tab: 3, then: () async {
          await t.ensureVisible(find.text('Erase everything'));
          await t.pumpAndSettle();
          await t.tap(find.text('Erase everything'));
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
