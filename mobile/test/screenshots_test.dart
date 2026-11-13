// Renders every screen to test/screenshots/*.png (run with --update-goldens). CI publishes them.
import 'package:coinpilot/main.dart';
import 'package:coinpilot/store.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';

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
      await tester.pumpWidget(CoinPilotApp(store: store, initialTab: tab));
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
          await t.tap(find.text('Erase everything'));
          await t.pumpAndSettle();
        }));
    testWidgets('welcome $mode', (t) => shot(t, id(8, 'welcome'), mode: mode, sample: false));
  }
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
