import 'package:coinpilot/forecast.dart';
import 'package:coinpilot/main.dart';
import 'package:coinpilot/screens/forecast_screen.dart';
import 'package:coinpilot/dates.dart';
import 'package:coinpilot/form.dart';
import 'package:coinpilot/kit.dart';
import 'package:coinpilot/screens/budgets_screen.dart';
import 'package:coinpilot/screens/more_screen.dart';
import 'package:coinpilot/store.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';

import 'fonts.dart';

Future<void> settle(WidgetTester tester) async {
  await tester.pump(const Duration(seconds: 5)); // let any message time out
  await tester.pumpAndSettle();
}

Finder tab(String name) => find.descendant(of: find.byType(BottomAppBar), matching: find.text(name));

void main() {
  setUpAll(loadAppFonts);
  setUp(() => EditableText.debugDeterministicCursor = true);
  tearDown(() => EditableText.debugDeterministicCursor = false);

  testWidgets('add a transaction from the welcome screen', (tester) async {
    tester.view.physicalSize = const Size(1170, 2532);
    tester.view.devicePixelRatio = 3;
    addTearDown(tester.view.reset);
    final store = Store.memory();
    await tester.pumpWidget(CoinPilotApp(store: store, showSetup: false));
    await tester.pumpAndSettle();
    expect(find.text('Welcome to WillowWallet'), findsOneWidget);

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
    expect(find.text('Welcome to WillowWallet'), findsNothing);
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

    for (final name in ['Transactions', 'Budgets', 'More', 'Home']) {
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
    await tester.tap(tab('More'));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Settings'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Dark'));
    await tester.tap(find.text('Ocean'));
    await tester.pumpAndSettle();
    expect(store.settings.themeMode, 'dark');
    expect(store.settings.colorTheme, 'Ocean');
  });

  testWidgets('Get started on first launch sets up income, budgets and bills', (tester) async {
    tester.view.physicalSize = const Size(1170, 2532);
    tester.view.devicePixelRatio = 3;
    addTearDown(tester.view.reset);
    final store = Store.memory();
    await tester.pumpWidget(CoinPilotApp(store: store));
    await tester.pumpAndSettle();
    expect(find.text('Set up step by step'), findsOneWidget);
    await tester.tap(find.text('Set up step by step'));
    await tester.pumpAndSettle();
    expect(find.text('Your accounts'), findsOneWidget);
    await tester.tap(find.text('Next'));
    await tester.pumpAndSettle();
    expect(find.text('Income and budgets'), findsOneWidget);
    await tester.enterText(find.byType(TextField).first, '2000');
    await tester.tap(find.text('Next'));
    await tester.pumpAndSettle();
    expect(find.text('Regular bills'), findsOneWidget);
    await tester.tap(find.byType(Checkbox).first);
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField).at(0), '1450');
    await tester.enterText(find.byType(TextField).at(1), '1');
    await tester.tap(find.text('Next'));
    await tester.pumpAndSettle();
    expect(find.text('Loans and cards'), findsOneWidget);
    await tester.tap(find.text('Next'));
    await tester.pumpAndSettle();
    expect(find.text('All set'), findsOneWidget);
    await tester.tap(find.text('Go to my dashboard'));
    await tester.pumpAndSettle();
    expect(store.prefs.setupDone, isTrue);
    expect(store.incomeSources.single.amountCents, 200000);
    expect(store.budgets, isNotEmpty);
    expect(store.bills.single.name, 'Rent or mortgage');
    expect(find.byTooltip('Add transaction'), findsOneWidget);
    await settle(tester);
  });

  testWidgets('every page under More opens, and a bill can be paid', (tester) async {
    tester.view.physicalSize = const Size(1170, 2532);
    tester.view.devicePixelRatio = 3;
    addTearDown(tester.view.reset);
    final store = Store.memory()..loadSample();
    await tester.pumpWidget(CoinPilotApp(store: store));
    await tester.pumpAndSettle();
    await tester.tap(tab('More'));
    await tester.pumpAndSettle();
    final more = find.byType(MoreScreen);
    for (final title in ['Forecast', 'Accounts', 'Bills', 'Income', 'Debts', 'Goals', 'Receipts', 'Household', 'Reports', 'Taxes', 'Import']) {
      final tile = find.descendant(of: more, matching: find.text(title));
      await Scrollable.ensureVisible(tester.element(tile.first), alignment: 0.3);
      await tester.pumpAndSettle();
      await tester.tap(tile.first);
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull, reason: title);
      await tester.pageBack();
      await tester.pumpAndSettle();
    }

    // Bills: pay Duke Energy
    await Scrollable.ensureVisible(tester.element(find.descendant(of: more, matching: find.text('Bills')).first), alignment: 0.3);
    await tester.pumpAndSettle();
    await tester.tap(find.descendant(of: more, matching: find.text('Bills')).first);
    await tester.pumpAndSettle();
    final rent = store.bills.firstWhere((b) => b.name == 'Duke Energy');
    await tester.scrollUntilVisible(find.text('Duke Energy'), 200, scrollable: find.descendant(of: find.byType(SubPage), matching: find.byType(Scrollable)).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Duke Energy'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Mark paid'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Mark paid').last);
    await tester.pumpAndSettle();
    final after = store.bills.firstWhere((b) => b.id == rent.id);
    expect(after.nextDue.isAfter(rent.nextDue), isTrue);
    expect(store.transactions.any((t) => t.payee == 'Duke Energy' && t.cents == -13000 && t.date == dayOnly(DateTime.now())), isTrue);
    await settle(tester);
  });

  testWidgets('skip a subscription suggestion for this month', (tester) async {
    tester.view.physicalSize = const Size(1170, 2532);
    tester.view.devicePixelRatio = 3;
    addTearDown(tester.view.reset);
    final store = Store.memory()..loadSample();
    await tester.pumpWidget(CoinPilotApp(store: store, initialTab: 2));
    await tester.pumpAndSettle();
    final skip = find.byTooltip('Skip Netflix');
    await tester.scrollUntilVisible(skip, 300, scrollable: find.descendant(of: find.byType(BudgetsScreen), matching: find.byType(Scrollable)).first);
    await tester.pumpAndSettle();
    await tester.tap(skip);
    await tester.pumpAndSettle();
    await tester.tap(find.textContaining('Skip for'));
    await tester.pumpAndSettle();
    final m = store.budgetMonth(DateTime.now());
    expect(store.prefs.skippedCuts, contains('netflix|${m.year}-${m.month.toString().padLeft(2, '0')}'));
    expect(find.byTooltip('Skip Netflix'), findsNothing);
    await settle(tester);
  });

  testWidgets('Mac and Linux: the sidebar opens every page, and adding is a dialog', (tester) async {
    hostOs = HostOs.linux;
    addTearDown(() => hostOs = HostOs.android);
    tester.view.physicalSize = const Size(1440, 1200); // tall enough for the whole sidebar
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final store = Store.memory()..loadSample();
    await tester.pumpWidget(CoinPilotApp(store: store));
    await tester.pumpAndSettle();
    expect(find.textContaining(RegExp('Left to spend|Over budget')), findsOneWidget);
    for (final page in ['Transactions', 'Budgets', 'Forecast', 'Accounts', 'Debts', 'Bills', 'Income', 'Goals', 'Receipts', 'Household', 'Reports', 'Taxes', 'Sync & Devices', 'Settings', 'Dashboard']) {
      await tester.tap(find.text(page).first);
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull, reason: page);
    }
    await tester.tap(find.text('Add transaction').first);
    await tester.pumpAndSettle();
    expect(find.byType(Dialog), findsOneWidget);
    await tester.enterText(find.byType(TextField).at(0), '9.99');
    await tester.enterText(find.byType(TextField).at(1), 'Corner Cafe');
    await tester.tap(find.text('Dining out').last);
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('Add spending'));
    await tester.tap(find.text('Add spending'));
    await tester.pumpAndSettle();
    expect(store.transactions.any((t) => t.payee == 'Corner Cafe' && t.cents == -999), isTrue);
    await settle(tester);
  });

  testWidgets('bottom bar: pick other pages, and labels stay on one line with big text', (tester) async {
    tester.view.physicalSize = const Size(1080, 2340); // a 360-wide phone, like most Androids
    tester.view.devicePixelRatio = 3;
    tester.platformDispatcher.textScaleFactorTestValue = 1.6;
    addTearDown(tester.view.reset);
    addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
    final store = Store.memory()..loadSample();
    await tester.pumpWidget(CoinPilotApp(store: store));
    await tester.pumpAndSettle();

    // Every label is one line and fits its slot
    for (final name in ['Home', 'Transactions', 'Budgets', 'More']) {
      final box = tester.getRect(tab(name));
      expect(box.height, lessThan(24), reason: '$name wrapped');
      expect(box.width, lessThan(1080 / 3 / 4), reason: '$name too wide');
    }
    expect(tester.takeException(), isNull);

    // The app name at the top isn't cut off
    final name = tester.renderObject<RenderParagraph>(find.text('WillowWallet'));
    expect(name.didExceedMaxLines, isFalse);
    expect(tester.getRect(find.text('WillowWallet')).right, lessThan(360));

    // More: tiles for pages not in the bar, and the Bottom bar setting
    await tester.tap(tab('More'));
    await tester.pumpAndSettle();
    expect(find.text('Home'), findsOneWidget); // only the bar's
    await tester.scrollUntilVisible(find.text('Bottom bar'), 200, scrollable: find.descendant(of: find.byType(MoreScreen), matching: find.byType(Scrollable)).first);
    expect(find.text('Choose the pages beside the + button'), findsOneWidget);
    await tester.longPress(tab('Transactions'));
    await tester.pumpAndSettle();
    expect(find.text('IN THE BAR · DRAG TO REORDER'), findsOneWidget);

    // Take Budgets out, put Bills in, save
    await tester.tap(find.byTooltip('Take Budgets out of the bar'));
    await tester.pumpAndSettle();
    expect(find.text('Pick 1 more'), findsOneWidget);
    await tester.ensureVisible(find.byTooltip('Put Bills in the bar'));
    await tester.tap(find.byTooltip('Put Bills in the bar'));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('Save'));
    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();
    expect(store.settings.navTabs, ['home', 'transactions', 'bills']);
    expect(tab('Bills'), findsOneWidget);
    expect(tab('Budgets'), findsNothing);

    // Bills is a tab now; Budgets moved to More
    await tester.tap(tab('Bills'));
    await tester.pumpAndSettle();
    expect(find.text('Bills'), findsWidgets);
    await tester.tap(tab('More'));
    await tester.pumpAndSettle();
    final moreList = find.descendant(of: find.byType(MoreScreen), matching: find.byType(Scrollable)).first;
    final budgetsTile = find.descendant(of: find.byType(MoreScreen), matching: find.text('Budgets'));
    await tester.drag(moreList, const Offset(0, 3000)); // back to the top
    await tester.pumpAndSettle();
    await tester.ensureVisible(budgetsTile);
    await tester.pumpAndSettle();
    await tester.tap(budgetsTile);
    await tester.pumpAndSettle();
    expect(find.byType(BudgetsScreen), findsOneWidget);
    expect(tester.takeException(), isNull);

    // Settings that aren't three real pages fall back to the default bar
    Navigator.of(tester.element(find.byType(AppShell, skipOffstage: false))).pop();
    await tester.pumpAndSettle();
    store.updateSettings((s) => s.navTabs = ['bills', 'goals', 'nonsense']);
    await tester.pumpAndSettle();
    expect(tab('Home'), findsOneWidget, reason: 'bad settings fall back to the default');
  });

  testWidgets('forecast: the dashboard card opens the forecast, and what-ifs change it', (tester) async {
    tester.view.physicalSize = const Size(1080, 2340);
    tester.view.devicePixelRatio = 3;
    addTearDown(tester.view.reset);
    final store = Store.memory()..loadSample();
    await tester.pumpWidget(CoinPilotApp(store: store));
    await tester.pumpAndSettle();
    final card = find.byKey(const Key('forecast-card'));
    await tester.scrollUntilVisible(card, 200, scrollable: find.byType(Scrollable).first);
    expect(find.descendant(of: card, matching: find.textContaining('Safe to spend')), findsOneWidget);
    await tester.tap(card);
    await tester.pumpAndSettle();
    expect(find.byType(ForecastScreen), findsOneWidget);
    final before = store.forecast(DateTime.now()).endExpected;

    final list = find.descendant(of: find.byType(ForecastScreen), matching: find.byType(Scrollable)).first;
    await tester.scrollUntilVisible(find.text('One-time expense'), 200, scrollable: list);
    await tester.ensureVisible(find.text('One-time expense'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('One-time expense'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField).at(0), 'New tires');
    await tester.enterText(find.byType(TextField).at(1), '600');
    await tester.tap(find.text('Try it'));
    await tester.pumpAndSettle();
    expect(find.textContaining('New tires'), findsWidgets);
    expect(find.byType(InputChip), findsOneWidget);
    await tester.drag(list, const Offset(0, 4000)); // back to the top
    await tester.pumpAndSettle();
    expect(find.textContaining('with your what-ifs'), findsOneWidget);
    // Nothing was saved
    expect(store.forecast(DateTime.now()).endExpected, before);
    expect(tester.takeException(), isNull);
  });
}
