import 'package:coinpilot/main.dart';
import 'package:coinpilot/store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Future<void> settle(WidgetTester tester) async {
  await tester.pump(const Duration(seconds: 5)); // let any message time out
  await tester.pumpAndSettle();
}

Finder tab(String name) => find.descendant(of: find.byType(BottomAppBar), matching: find.text(name));

void main() {
  setUp(() => EditableText.debugDeterministicCursor = true);
  tearDown(() => EditableText.debugDeterministicCursor = false);

  testWidgets('add a transaction from the welcome screen', (tester) async {
    tester.view.physicalSize = const Size(1170, 2532);
    tester.view.devicePixelRatio = 3;
    addTearDown(tester.view.reset);
    final store = Store.memory();
    await tester.pumpWidget(CoinPilotApp(store: store));
    await tester.pumpAndSettle();
    expect(find.text('Welcome to CoinPilot'), findsOneWidget);

    await tester.tap(find.byTooltip('Add transaction'));
    await tester.pumpAndSettle();
    expect(find.text('Add transaction'), findsOneWidget);

    // Saving with nothing filled in explains what's missing
    await tester.ensureVisible(find.text('Add spending'));
    await tester.tap(find.text('Add spending'));
    await tester.pumpAndSettle();
    expect(find.text('Enter an amount.'), findsOneWidget);

    await tester.enterText(find.byType(TextField).at(0), '12.50');
    await tester.enterText(find.byType(TextField).at(1), 'Corner Cafe');
    await tester.ensureVisible(find.text('Dining out'));
    await tester.tap(find.text('Dining out'));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('Add spending'));
    await tester.tap(find.text('Add spending'));
    await tester.pumpAndSettle();

    expect(store.transactions.single.cents, -1250);
    expect(store.transactions.single.category, 'Dining out');
    expect(find.text('Corner Cafe'), findsWidgets);
    expect(find.text('Welcome to CoinPilot'), findsNothing);
    await settle(tester);

    // The next transaction for the same payee gets the category automatically
    await tester.tap(find.byTooltip('Add transaction'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField).at(0), '4');
    await tester.enterText(find.byType(TextField).at(1), 'Corner Cafe');
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('Add spending'));
    await tester.tap(find.text('Add spending'));
    await tester.pumpAndSettle();
    expect(store.transactions.length, 2);
    expect(store.transactions.last.category, 'Dining out');
    await settle(tester);
  });

  testWidgets('create a budget and see it on every tab', (tester) async {
    tester.view.physicalSize = const Size(1170, 2532);
    tester.view.devicePixelRatio = 3;
    addTearDown(tester.view.reset);
    final store = Store.memory()..loadSample();
    await tester.pumpWidget(CoinPilotApp(store: store));
    await tester.pumpAndSettle();
    expect(find.textContaining(RegExp('Left to spend|Over budget')), findsOneWidget);

    for (final name in ['Transactions', 'Budgets', 'Settings', 'Home']) {
      await tester.tap(tab(name));
      await tester.pumpAndSettle();
    }

    await tester.tap(tab('Budgets'));
    await tester.pumpAndSettle();
    final before = store.budgets.length;
    await tester.tap(find.byTooltip('New budget'));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('Travel').last);
    await tester.tap(find.text('Travel').last);
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField).last, '150');
    await tester.ensureVisible(find.text('Create budget'));
    await tester.tap(find.text('Create budget'));
    await tester.pumpAndSettle();
    expect(store.budgets.length, before + 1);
    expect(store.budgetFor('Travel')!.limitCents, 15000);
    await settle(tester);

    // Settings: switch to dark and Ocean
    await tester.tap(tab('Settings'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Dark'));
    await tester.tap(find.text('Ocean'));
    await tester.pumpAndSettle();
    expect(store.settings.themeMode, 'dark');
    expect(store.settings.colorTheme, 'Ocean');
  });
}
