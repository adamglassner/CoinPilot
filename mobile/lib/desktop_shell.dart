import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'dates.dart';
import 'finance.dart';
import 'form.dart';
import 'screens/accounts_screen.dart';
import 'screens/bills_screen.dart';
import 'screens/budgets_screen.dart';
import 'screens/debts_screen.dart';
import 'auto_backup.dart';
import 'calculator.dart';
import 'screens/forecast_screen.dart';
import 'screens/documents_screen.dart';
import 'screens/gifts_screen.dart';
import 'screens/household_screen.dart';
import 'screens/owed_screen.dart';
import 'screens/wishlist_screen.dart';
import 'screens/receipts_screen.dart';
import 'screens/get_started.dart';
import 'screens/goals_screen.dart';
import 'screens/home_screen.dart';
import 'screens/income_screen.dart';
import 'screens/reports_screen.dart';
import 'screens/settings_screen.dart';
import 'screens/sync_screen.dart';
import 'screens/taxes_screen.dart';
import 'screens/transaction_sheet.dart';
import 'screens/transactions_screen.dart';
import 'screens/whats_new.dart';
import 'store.dart';
import 'sync/scope.dart';
import 'theme.dart';
import 'widgets.dart';

class _Section {
  const _Section(this.key, this.title, this.icon, this.selectedIcon);
  final String key, title;
  final IconData icon, selectedIcon;
}

/// The Mac and Linux app: a sidebar like WillowWallet for Windows (Dashboard, Transactions, Budgets, Accounts, Debts,
/// Bills, Income, Goals, Reports, Taxes, Sync & Devices, Settings), with each page in the main area and dialogs for
/// adding and editing. Ctrl/⌘+N adds a transaction; Ctrl/⌘+1…9 switch pages.
class DesktopShell extends StatefulWidget {
  const DesktopShell({super.key, this.initialPage = 'dashboard', this.startupTasks = false, this.showSetup = true});
  final String initialPage;
  final bool startupTasks;
  final bool showSetup;

  @override
  State<DesktopShell> createState() => DesktopShellState();
}

class DesktopShellState extends State<DesktopShell> {
  static const sections = [
    _Section('dashboard', 'Dashboard', Icons.space_dashboard_outlined, Icons.space_dashboard_rounded),
    _Section('transactions', 'Transactions', Icons.receipt_long_outlined, Icons.receipt_long_rounded),
    _Section('budgets', 'Budgets', Icons.pie_chart_outline_rounded, Icons.pie_chart_rounded),
    _Section('forecast', 'Forecast', Icons.timeline_outlined, Icons.timeline_rounded),
    _Section('accounts', 'Accounts', Icons.account_balance_outlined, Icons.account_balance_rounded),
    _Section('debts', 'Debts', Icons.credit_card_outlined, Icons.credit_card_rounded),
    _Section('bills', 'Bills', Icons.event_note_outlined, Icons.event_note_rounded),
    _Section('income', 'Income', Icons.payments_outlined, Icons.payments_rounded),
    _Section('goals', 'Goals', Icons.flag_outlined, Icons.flag_rounded),
    _Section('receipts', 'Receipts', Icons.receipt_outlined, Icons.receipt_rounded),
    _Section('household', 'Household', Icons.people_outline_rounded, Icons.people_rounded),
    _Section('owed', 'Owed to you', Icons.handshake_outlined, Icons.handshake_rounded),
    _Section('wishlist', 'Wishlist', Icons.favorite_border_rounded, Icons.favorite_rounded),
    _Section('gifts', 'Gifts', Icons.card_giftcard_outlined, Icons.card_giftcard_rounded),
    _Section('documents', 'Documents', Icons.folder_special_outlined, Icons.folder_special_rounded),
    _Section('reports', 'Reports', Icons.insights_outlined, Icons.insights_rounded),
    _Section('taxes', 'Taxes', Icons.folder_copy_outlined, Icons.folder_copy_rounded),
    _Section('sync', 'Sync & Devices', Icons.sync_rounded, Icons.sync_rounded),
  ];

  late String _page = widget.initialPage;
  final _navigators = <String, GlobalKey<NavigatorState>>{};
  bool? _inSetup;
  bool _started = false;

  void go(String page) {
    if (page == 'theme') {
      final store = StoreScope.read(context);
      final dark = Theme.of(context).brightness == Brightness.dark;
      store.updateSettings((s) => s.themeMode = dark ? 'light' : 'dark');
      return;
    }
    if (page == 'calculator') {
      showCalculator(context);
      return;
    }
    if (page == _page) {
      _navigators[page]?.currentState?.popUntil((r) => r.isFirst);
    }
    setState(() => _page = page);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_started || !widget.startupTasks) return;
    _started = true;
    WidgetsBinding.instance.addPostFrameCallback((_) => _startup());
  }

  Future<void> _startup() async {
    if (!mounted) return;
    final store = StoreScope.read(context);
    final today = dayOnly(DateTime.now());
    final added = store.addDuePaydays(today);
    if (added.isNotEmpty && mounted) {
      toast(context, added.length == 1 ? 'Payday: added ${added.first.payee}.' : 'Added ${added.length} paydays to Transactions.');
    }
    if (mounted && !_needsSetup(store)) await showWhatsNewAfterUpdate(context);
    final asking = store.askingPaydays(today);
    if (asking.isNotEmpty && mounted && !_needsSetup(store)) await showPaydaySheet(context, asking);
    if (mounted) UpdaterScope.read(context).check();
    if (mounted) AutoBackup.run(StoreScope.read(context));
  }

  bool _needsSetup(Store store) => _inSetup ??= widget.showSetup && store.isEmpty && !store.prefs.setupDone;

  Widget _pageFor(String key) => switch (key) {
        'dashboard' => HomeScreen(onNavigate: (i) => go(i == 1 ? 'transactions' : i == 2 ? 'budgets' : 'dashboard')),
        'transactions' => const TransactionsScreen(),
        'budgets' => const BudgetsScreen(),
        'forecast' => const ForecastScreen(),
        'accounts' => const AccountsScreen(),
        'debts' => const DebtsScreen(),
        'bills' => const BillsScreen(),
        'income' => const IncomeScreen(),
        'goals' => const GoalsScreen(),
        'receipts' => const ReceiptsScreen(),
        'household' => const HouseholdScreen(),
        'owed' => const OwedScreen(),
        'wishlist' => const WishlistScreen(),
        'gifts' => const GiftsScreen(),
        'documents' => const DocumentsScreen(),
        'reports' => const ReportsScreen(),
        'taxes' => const TaxesScreen(),
        'sync' => const SyncScreen(),
        _ => const SettingsScreen(),
      };

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    if (_needsSetup(store)) {
      return GetStartedScreen(onDone: () => setState(() => _inSetup = false));
    }
    final mod = hostOs == HostOs.macos ? LogicalKeyboardKey.meta : LogicalKeyboardKey.control;
    final shortcuts = <ShortcutActivator, VoidCallback>{
      LogicalKeySet(mod, LogicalKeyboardKey.keyN): () => showTransactionSheet(context),
      LogicalKeySet(mod, LogicalKeyboardKey.comma): () => go('settings'),
      LogicalKeySet(mod, LogicalKeyboardKey.keyK): () => go('calculator'),
      for (var i = 0; i < 9; i++) LogicalKeySet(mod, LogicalKeyboardKey(LogicalKeyboardKey.digit1.keyId + i)): () => go(sections[i].key),
    };
    final key = _navigators.putIfAbsent(_page, () => GlobalKey<NavigatorState>());
    return CallbackShortcuts(
      bindings: shortcuts,
      child: Focus(
        autofocus: true,
        child: Scaffold(
          backgroundColor: p.bg,
          body: Row(
            children: [
              _Sidebar(page: _page, onGo: go),
              Expanded(
                child: ClipRect(
                  child: Navigator(
                    key: key,
                    onGenerateRoute: (_) => PageRouteBuilder<void>(
                      pageBuilder: (_, __, ___) => _PageFrame(child: _pageFor(_page)),
                      transitionDuration: Duration.zero,
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Keeps pages a comfortable width on a big screen, and pushed pages in the same frame.
class _PageFrame extends StatelessWidget {
  const _PageFrame({required this.child});
  final Widget child;

  @override
  Widget build(BuildContext context) => ColoredBox(
        color: context.pal.bg,
        child: Align(
          alignment: Alignment.topCenter,
          child: ConstrainedBox(constraints: const BoxConstraints(maxWidth: 1000), child: child),
        ),
      );
}

class _Sidebar extends StatelessWidget {
  const _Sidebar({required this.page, required this.onGo});
  final String page;
  final ValueChanged<String> onGo;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final sync = SyncScope.of(context);
    final p = context.pal;
    final bg = p.dark ? Color.lerp(p.bg, Colors.black, 0.35)! : p.hero;
    final today = dayOnly(DateTime.now());
    final overdue = store.openBills.any((b) => Bills.status(b, today) == BillStatus.overdue);
    final asking = store.askingPaydays(today).isNotEmpty;
    final (syncIcon, _) = syncLook(sync.status, p);
    final mod = hostOs == HostOs.macos ? '⌘' : 'Ctrl+';

    Widget item(String key, String title, IconData icon, IconData selected, {bool dot = false, String? hint}) {
      final on = key == page;
      return Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 1.5),
        child: Tooltip(
          message: hint ?? '',
          waitDuration: const Duration(milliseconds: 800),
          child: Material(
            color: on ? Colors.white.withValues(alpha: 0.11) : Colors.transparent,
            borderRadius: BorderRadius.circular(11),
            child: InkWell(
              borderRadius: BorderRadius.circular(11),
              onTap: () => onGo(key),
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                child: Row(
                  children: [
                    Icon(on ? selected : icon, size: 20, color: on ? p.mint : p.heroMuted),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Text(title, style: TextStyle(fontSize: 14.5, fontWeight: on ? FontWeight.w700 : FontWeight.w500, color: on ? p.heroText : p.heroMuted)),
                    ),
                    if (dot) Container(width: 8, height: 8, decoration: BoxDecoration(color: const Color(0xFFFF8A5B), shape: BoxShape.circle)),
                  ],
                ),
              ),
            ),
          ),
        ),
      );
    }

    return Container(
      width: 252,
      color: bg,
      child: SafeArea(
        right: false,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: EdgeInsets.fromLTRB(22, hostOs == HostOs.macos ? 30 : 20, 18, 16),
              child: Row(
                children: [
                  const CoinLogo(size: 32),
                  const SizedBox(width: 10),
                  Expanded(child: Text('WillowWallet', style: display(19, p.heroText, weight: FontWeight.w800))),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(14, 0, 14, 12),
              child: Tooltip(
                message: '${mod}N',
                child: FilledButton.icon(
                  style: FilledButton.styleFrom(backgroundColor: p.mint, foregroundColor: const Color(0xFF0B120F), minimumSize: const Size(0, 44)),
                  onPressed: () => showTransactionSheet(context),
                  icon: const Icon(Icons.add_rounded),
                  label: const Text('Add transaction'),
                ),
              ),
            ),
            Expanded(
              child: ListView(
                padding: EdgeInsets.zero,
                children: [
                  for (final (i, s) in DesktopShellState.sections.indexed)
                    item(s.key, s.title, s.key == 'sync' ? syncIcon : s.icon, s.key == 'sync' ? syncIcon : s.selectedIcon,
                        dot: (s.key == 'bills' && overdue) || (s.key == 'income' && asking), hint: i < 9 ? '$mod${i + 1}' : null),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(14, 8, 14, 8),
              child: Material(
                color: Colors.white.withValues(alpha: 0.07),
                borderRadius: BorderRadius.circular(14),
                child: InkWell(
                  borderRadius: BorderRadius.circular(14),
                  onTap: () => onGo('sync'),
                  child: Padding(
                    padding: const EdgeInsets.all(12),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Icon(syncIcon, size: 16, color: p.mint),
                            const SizedBox(width: 8),
                            Expanded(
                              child: Text(sync.isSetUp ? sync.statusText : 'Saved on this $deviceWord',
                                  maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w700, color: p.heroText)),
                            ),
                          ],
                        ),
                        const SizedBox(height: 4),
                        Text(
                          sync.isSetUp
                              ? '${sync.username} on ${sync.serverName}'
                              : store.settings.isLocked
                                  ? 'Locked with a ${store.settings.lockKind == 'PIN' ? 'PIN' : 'password'}'
                                  : 'Add a PIN or password in Settings → Privacy.',
                          style: TextStyle(fontSize: 12.5, color: p.heroMuted, height: 1.3),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
            item('calculator', 'Calculator', Icons.calculate_outlined, Icons.calculate_outlined, hint: '${mod}K'),
            item('theme', p.dark ? 'Light mode' : 'Dark mode', p.dark ? Icons.light_mode_outlined : Icons.dark_mode_outlined,
                p.dark ? Icons.light_mode_outlined : Icons.dark_mode_outlined),
            item('settings', 'Settings', Icons.settings_outlined, Icons.settings_rounded, hint: '$mod,'),
            const SizedBox(height: 10),
          ],
        ),
      ),
    );
  }
}
