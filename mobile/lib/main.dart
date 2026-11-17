import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'dates.dart';
import 'finance.dart';
import 'lock.dart';
import 'screens/budgets_screen.dart';
import 'screens/get_started.dart';
import 'screens/home_screen.dart';
import 'screens/income_screen.dart';
import 'screens/more_screen.dart';
import 'screens/settings_screen.dart' show UpdaterScope;
import 'screens/transaction_sheet.dart';
import 'screens/transactions_screen.dart';
import 'store.dart';
import 'sync/engine.dart';
import 'sync/scope.dart';
import 'theme.dart';
import 'updater.dart';
import 'version.dart';
import 'widgets.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);
  final store = await Store.open();
  final sync = SyncEngine(store, appVersion: appVersion)..start();
  runApp(CoinPilotApp(store: store, sync: sync, startupTasks: true));
}

class CoinPilotApp extends StatefulWidget {
  const CoinPilotApp({super.key, required this.store, this.sync, this.updater, this.initialTab = 0, this.startupTasks = false, this.showSetup = true});
  final Store store;

  /// Syncing with a WillowWallet Server (tests and screenshots get one that never syncs on its own).
  final SyncEngine? sync;

  /// Updates from GitHub (tests and screenshots pass one that never goes online).
  final Updater? updater;
  final int initialTab;

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
                  home: AppShell(initialTab: widget.initialTab, startupTasks: widget.startupTasks, showSetup: widget.showSetup),
                );
              },
            ),
          ),
        ),
      ),
    );
  }
}

/// Bottom navigation (Home, Transactions, Budgets, More) around the add button.
class AppShell extends StatefulWidget {
  const AppShell({super.key, this.initialTab = 0, this.startupTasks = false, this.showSetup = true});
  final int initialTab;
  final bool startupTasks;
  final bool showSetup;

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> {
  late int _tab = widget.initialTab;
  bool? _inSetup;
  bool _started = false;

  void _go(int tab) => setState(() => _tab = tab);

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
    if (asking.isNotEmpty && mounted && !_needsSetup(store)) await showPaydaySheet(context, asking);
    if (mounted) UpdaterScope.read(context).check();
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
    final asking = store.askingPaydays(DateTime.now()).isNotEmpty;
    final overdue = store.openBills.any((b) => Bills.status(b, DateTime.now()) == BillStatus.overdue);

    Widget item(int i, IconData icon, IconData selectedIcon, String label, {bool dot = false}) {
      final on = _tab == i;
      return Expanded(
        child: Semantics(
          selected: on,
          button: true,
          label: label,
          excludeSemantics: true,
          child: InkResponse(
            onTap: () => _go(i),
            radius: 40,
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Stack(
                  clipBehavior: Clip.none,
                  children: [
                    AnimatedContainer(
                      duration: const Duration(milliseconds: 200),
                      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
                      decoration: BoxDecoration(color: on ? p.accentSoft : Colors.transparent, borderRadius: BorderRadius.circular(99)),
                      child: Icon(on ? selectedIcon : icon, size: 23, color: on ? p.accent : p.muted),
                    ),
                    if (dot)
                      Positioned(
                        right: 12,
                        top: 2,
                        child: Container(width: 8, height: 8, decoration: BoxDecoration(color: p.danger, shape: BoxShape.circle, border: Border.all(color: p.surface, width: 1.5))),
                      ),
                  ],
                ),
                const SizedBox(height: 4),
                Text(label, style: TextStyle(fontSize: 11.5, fontWeight: on ? FontWeight.w700 : FontWeight.w500, color: on ? p.text : p.muted)),
              ],
            ),
          ),
        ),
      );
    }

    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: overlay,
      child: Scaffold(
        body: IndexedStack(
          index: _tab,
          children: [
            HomeScreen(onNavigate: _go),
            const TransactionsScreen(),
            const BudgetsScreen(),
            const MoreScreen(),
          ],
        ),
        floatingActionButton: FloatingActionButton(
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
        bottomNavigationBar: BottomAppBar(
          color: p.surface,
          surfaceTintColor: Colors.transparent,
          shadowColor: Colors.black,
          elevation: 10,
          shape: const CircularNotchedRectangle(),
          notchMargin: 7,
          height: 74,
          padding: EdgeInsets.zero,
          child: Row(
            children: [
              item(0, Icons.home_outlined, Icons.home_rounded, 'Home'),
              item(1, Icons.receipt_long_outlined, Icons.receipt_long_rounded, 'Transactions'),
              const SizedBox(width: 76),
              item(2, Icons.pie_chart_outline_rounded, Icons.pie_chart_rounded, 'Budgets'),
              item(3, Icons.grid_view_outlined, Icons.grid_view_rounded, 'More', dot: asking || overdue),
            ],
          ),
        ),
      ),
    );
  }
}
