import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'dates.dart';
import 'desktop_shell.dart';
import 'form.dart';
import 'reminders.dart';
import 'screens/whats_new.dart';
import 'finance.dart';
import 'kit.dart';
import 'lock.dart';
import 'nav_bar.dart';
import 'screens/get_started.dart';
import 'screens/income_screen.dart';
import 'screens/more_screen.dart';
import 'screens/settings_screen.dart' show UpdaterScope;
import 'screens/transaction_sheet.dart';
import 'store.dart';
import 'sync/engine.dart';
import 'sync/scope.dart';
import 'theme.dart';
import 'auto_backup.dart';
import 'updater.dart';
import 'widget_sync.dart';
import 'version.dart';
import 'widgets.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  hostOs = Platform.isMacOS
      ? HostOs.macos
      : Platform.isLinux
          ? HostOs.linux
          : HostOs.android;
  if (!isDesktop) SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);
  final store = await Store.open();
  final sync = SyncEngine(store, appVersion: appVersion)..start();
  await Reminders.init();
  Reminders.start(store);
  HomeScreenWidget.start(store);
  runApp(CoinPilotApp(store: store, sync: sync, startupTasks: true));
}

/// The app's navigator (notifications and the home screen widget open sheets with it).
final appNavigator = GlobalKey<NavigatorState>();

class CoinPilotApp extends StatefulWidget {
  const CoinPilotApp(
      {super.key, required this.store, this.sync, this.updater, this.initialTab = 0, this.initialPage = 'dashboard', this.startupTasks = false, this.showSetup = true});
  final Store store;

  /// Syncing with a WillowWallet Server (tests and screenshots get one that never syncs on its own).
  final SyncEngine? sync;

  /// Updates from GitHub (tests and screenshots pass one that never goes online).
  final Updater? updater;
  final int initialTab;

  /// The Mac and Linux app: the page to open ("dashboard", "bills"…).
  final String initialPage;

  /// Record paydays, ask about income and check for updates when it opens (off in tests).
  final bool startupTasks;

  /// Show Get started on first run (no data and not done before).
  final bool showSetup;

  @override
  State<CoinPilotApp> createState() => _CoinPilotAppState();
}

class _CoinPilotAppState extends State<CoinPilotApp> with WidgetsBindingObserver {
  late final SyncEngine sync = widget.sync ?? SyncEngine(widget.store, appVersion: appVersion, automatic: false);
  late final Updater updater = widget.updater ?? Updater(widget.store);
  late final AppLock lock = AppLock(widget.store);

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  // Coming back to the app picks up changes made on other devices (and asks for the PIN after time away).
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused || state == AppLifecycleState.hidden) lock.wentAway();
    if (state == AppLifecycleState.resumed) {
      lock.cameBack();
      if (sync.automatic) sync.syncNow();
    }
  }

  @override
  Widget build(BuildContext context) {
    final store = widget.store;
    return SyncScope(
      engine: sync,
      child: StoreScope(
        store: store,
        child: UpdaterScope(
          updater: updater,
          child: LockScope(
            lock: lock,
            child: ListenableBuilder(
              listenable: store,
              builder: (context, _) {
                final s = store.settings;
                return MaterialApp(
                  navigatorKey: appNavigator,
                  title: 'WillowWallet',
                  debugShowCheckedModeBanner: false,
                  theme: buildTheme(Palette.build(s.colorTheme, false)),
                  darkTheme: buildTheme(Palette.build(s.colorTheme, true)),
                  themeMode: switch (s.themeMode) {
                    'light' => ThemeMode.light,
                    'dark' => ThemeMode.dark,
                    _ => ThemeMode.system,
                  },
                  // The lock covers every page and sheet
                  builder: (context, child) => ListenableBuilder(
                    listenable: lock,
                    builder: (context, _) => Stack(
                      children: [
                        if (child != null) child,
                        if (lock.locked) Positioned.fill(child: Overlay(initialEntries: [OverlayEntry(builder: (_) => const LockScreen())])),
                      ],
                    ),
                  ),
                  home: isDesktop
                      ? DesktopShell(initialPage: widget.initialPage, startupTasks: widget.startupTasks, showSetup: widget.showSetup)
                      : AppShell(initialTab: widget.initialTab, startupTasks: widget.startupTasks, showSetup: widget.showSetup),
                );
              },
            ),
          ),
        ),
      ),
    );
  }
}

/// Bottom navigation: three pages you choose (Home, Transactions and Budgets to start) and More, around the add button.
class AppShell extends StatefulWidget {
  const AppShell({super.key, this.initialTab = 0, this.startupTasks = false, this.showSetup = true});
  final int initialTab;
  final bool startupTasks;
  final bool showSetup;

  @override
  State<AppShell> createState() => AppShellState();
}

class AppShellState extends State<AppShell> {
  late int _tab = widget.initialTab;
  bool? _inSetup;
  bool _started = false;

  /// The nearest bottom-bar shell (null on a computer).
  static AppShellState? of(BuildContext context) => context.findAncestorStateOfType<AppShellState>();

  void _go(int tab) => setState(() => _tab = tab);

  /// Goes to a page: its tab when it's in the bar, or opens it over the current tab.
  void openPage(String key) {
    final tabs = barTabs(StoreScope.read(context).settings);
    final i = tabs.indexWhere((t) => t.key == key);
    if (i >= 0) {
      Navigator.of(context).popUntil((r) => r.isFirst);
      _go(i);
      return;
    }
    final page = barPage(key);
    if (page != null) pushPage(context, page.build(openPage));
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_started || !widget.startupTasks) return;
    _started = true;
    WidgetsBinding.instance.addPostFrameCallback((_) => _startup());
  }

  /// Same-amount paychecks are added on payday; ones that change are asked about. Updates are checked once a day.
  Future<void> _startup() async {
    if (!mounted) return;
    final store = StoreScope.read(context);
    final today = dayOnly(DateTime.now());
    final added = store.addDuePaydays(today);
    if (added.isNotEmpty && mounted) {
      toast(context, added.length == 1 ? 'Payday: added ${added.first.payee}.' : 'Added ${added.length} paydays to Transactions.');
    }
    final asking = store.askingPaydays(today);
    if (mounted && !_needsSetup(store)) await showWhatsNewAfterUpdate(context);
    if (asking.isNotEmpty && mounted && !_needsSetup(store)) await showPaydaySheet(context, asking);
    if (mounted) UpdaterScope.read(context).check();
    if (mounted) AutoBackup.run(store);
    // Opened from the widget's + or "Spent anything today?": add a transaction
    Reminders.onQuickAdd = (input) => quickAdd(input);
    HomeScreenWidget.clicks().listen((host) => host == 'add' ? quickAdd('') : null);
    final typed = await Reminders.launchQuickAdd();
    if (typed != null) {
      quickAdd(typed);
    } else if (await HomeScreenWidget.launchedWith() == 'add') {
      quickAdd('');
    }
  }

  /// Opens Add transaction, filled in from what was typed into the notification ("12.50 coffee").
  void quickAdd(String input) {
    final c = appNavigator.currentState?.overlay?.context;
    if (c == null) return;
    final (cents, payee) = Reminders.parseQuickAdd(input);
    showTransactionSheet(c, prefill: cents == null && payee == null ? null : TxnPrefill(cents: cents == null ? null : -cents, payee: payee));
  }

  /// Decided once when the app opens, so adding the first account doesn't end Get started halfway.
  bool _needsSetup(Store store) => _inSetup ??= widget.showSetup && store.isEmpty && !store.prefs.setupDone;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final store = StoreScope.of(context);
    final overlay = (p.dark ? SystemUiOverlayStyle.light : SystemUiOverlayStyle.dark).copyWith(
      statusBarColor: Colors.transparent,
      systemNavigationBarColor: p.surface,
      systemNavigationBarIconBrightness: p.dark ? Brightness.light : Brightness.dark,
    );
    if (_needsSetup(store)) {
      return AnnotatedRegion<SystemUiOverlayStyle>(
        value: overlay,
        child: GetStartedScreen(onDone: () => setState(() => _inSetup = false)),
      );
    }
    final now = DateTime.now();
    final tabs = barTabs(store.settings);
    final tab = _tab.clamp(0, tabs.length);
    final asking = store.askingPaydays(now).isNotEmpty;
    final overdue = store.openBills.any((b) => Bills.status(b, now) == BillStatus.overdue);
    bool dotFor(String key) => switch (key) { 'bills' => overdue, 'income' => asking, _ => false };
    // More gets the dot for whatever isn't in the bar
    final moreDot = (asking && !tabs.any((t) => t.key == 'income')) || (overdue && !tabs.any((t) => t.key == 'bills'));

    Widget item(int i) {
      if (i == tabs.length) {
        return NavItem(
            icon: Icons.grid_view_outlined, selectedIcon: Icons.grid_view_rounded, label: 'More', selected: tab == i, dot: moreDot,
            onTap: () => _go(i), onLongPress: () => showBottomBarSheet(context));
      }
      final t = tabs[i];
      return NavItem(
          icon: t.icon, selectedIcon: t.selectedIcon, label: t.label, selected: tab == i, dot: dotFor(t.key),
          onTap: () => _go(i), onLongPress: () => showBottomBarSheet(context));
    }

    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: overlay,
      child: Scaffold(
        body: IndexedStack(
          index: tab,
          children: [
            for (final t in tabs) KeyedSubtree(key: ValueKey('tab-${t.key}'), child: t.build(openPage)),
            const MoreScreen(),
          ],
        ),
        floatingActionButton: FloatingActionButton(
          heroTag: 'add-transaction',
          tooltip: 'Add transaction',
          onPressed: () => showTransactionSheet(context),
          backgroundColor: p.accent,
          foregroundColor: p.onAccent,
          elevation: 3,
          highlightElevation: 6,
          shape: const CircleBorder(),
          child: const Icon(Icons.add_rounded, size: 32),
        ),
        floatingActionButtonLocation: FloatingActionButtonLocation.centerDocked,
        bottomNavigationBar: MediaQuery.withClampedTextScaling(
          // Like Android's own bars: labels grow a little with the phone's text size, but never wrap or get cut off
          maxScaleFactor: 1.15,
          child: BottomAppBar(
            color: p.surface,
            surfaceTintColor: Colors.transparent,
            shadowColor: Colors.black,
            elevation: 10,
            shape: const CircularNotchedRectangle(),
            notchMargin: 7,
            height: 74,
            padding: EdgeInsets.zero,
            child: Row(
              children: [item(0), item(1), const SizedBox(width: 76), item(2), item(3)],
            ),
          ),
        ),
      ),
    );
  }
}
