import 'package:flutter/material.dart';

import 'kit.dart';
import 'models.dart';
import 'screens/accounts_screen.dart';
import 'screens/bills_screen.dart';
import 'screens/budgets_screen.dart';
import 'screens/debts_screen.dart';
import 'screens/forecast_screen.dart';
import 'screens/goals_screen.dart';
import 'screens/home_screen.dart';
import 'screens/income_screen.dart';
import 'screens/reports_screen.dart';
import 'screens/taxes_screen.dart';
import 'screens/transactions_screen.dart';
import 'store.dart';
import 'theme.dart';

/// A page that can sit in the phone's bottom bar.
class BarPage {
  const BarPage(this.key, this.label, this.icon, this.selectedIcon, this.blurb);
  final String key, label, blurb;
  final IconData icon, selectedIcon;

  /// The page itself. [open] goes to another page (Home's "View all" and "See all").
  Widget build(ValueChanged<String> open) => switch (key) {
        'home' => HomeScreen(onNavigate: (i) => open(i == 1 ? 'transactions' : i == 2 ? 'budgets' : 'home')),
        'transactions' => const TransactionsScreen(),
        'budgets' => const BudgetsScreen(),
        'forecast' => const ForecastScreen(),
        'accounts' => const AccountsScreen(),
        'bills' => const BillsScreen(),
        'income' => const IncomeScreen(),
        'debts' => const DebtsScreen(),
        'goals' => const GoalsScreen(),
        'reports' => const ReportsScreen(),
        _ => const TaxesScreen(),
      };
}

const barPages = [
  BarPage('home', 'Home', Icons.home_outlined, Icons.home_rounded, 'This month at a glance'),
  BarPage('transactions', 'Transactions', Icons.receipt_long_outlined, Icons.receipt_long_rounded, 'Everything you spent and earned'),
  BarPage('budgets', 'Budgets', Icons.pie_chart_outline_rounded, Icons.pie_chart_rounded, 'How much is left in each'),
  BarPage('forecast', 'Forecast', Icons.timeline_outlined, Icons.timeline_rounded, 'Where your money is headed'),
  BarPage('accounts', 'Accounts', Icons.account_balance_outlined, Icons.account_balance_rounded, 'Balances and net worth'),
  BarPage('bills', 'Bills', Icons.event_note_outlined, Icons.event_note_rounded, 'What\'s due and when'),
  BarPage('income', 'Income', Icons.payments_outlined, Icons.payments_rounded, 'Paychecks and paydays'),
  BarPage('debts', 'Debts', Icons.credit_card_outlined, Icons.credit_card_rounded, 'Your plan to pay them off'),
  BarPage('goals', 'Goals', Icons.flag_outlined, Icons.flag_rounded, 'What you\'re saving for'),
  BarPage('reports', 'Reports', Icons.insights_outlined, Icons.insights_rounded, 'Trends and year in review'),
  BarPage('taxes', 'Taxes', Icons.folder_copy_outlined, Icons.folder_copy_rounded, 'Documents by tax year'),
];

BarPage? barPage(String key) {
  for (final p in barPages) {
    if (p.key == key) return p;
  }
  return null;
}

/// The three pages in the bar, from settings (the default if they're missing or not three different pages).
List<BarPage> barTabs(AppSettings s) {
  final picked = <BarPage>[];
  for (final k in s.navTabs) {
    final p = barPage(k);
    if (p != null && !picked.contains(p)) picked.add(p);
  }
  if (picked.length != 3) return [for (final k in AppSettings.defaultNavTabs) barPage(k)!];
  return picked;
}

/// One button in the bottom bar. The label stays on one line however big the phone's text is.
class NavItem extends StatelessWidget {
  const NavItem({super.key, required this.icon, required this.selectedIcon, required this.label, required this.selected, required this.onTap, this.onLongPress, this.dot = false});
  final IconData icon, selectedIcon;
  final String label;
  final bool selected, dot;
  final VoidCallback onTap;
  final VoidCallback? onLongPress;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Expanded(
      child: Semantics(
        selected: selected,
        button: true,
        label: label,
        hint: onLongPress == null ? null : 'Long-press to change the bottom bar',
        excludeSemantics: true,
        child: InkResponse(
          onTap: onTap,
          onLongPress: onLongPress,
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
                    decoration: BoxDecoration(color: selected ? p.accentSoft : Colors.transparent, borderRadius: BorderRadius.circular(99)),
                    child: Icon(selected ? selectedIcon : icon, size: 23, color: selected ? p.accent : p.muted),
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
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 3),
                child: FittedBox(
                  fit: BoxFit.scaleDown,
                  child: Text(label,
                      maxLines: 1,
                      softWrap: false,
                      style: TextStyle(fontSize: 11.5, fontWeight: selected ? FontWeight.w700 : FontWeight.w500, color: selected ? p.text : p.muted)),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

Future<void> showBottomBarSheet(BuildContext context) => showSheet<void>(context, const BottomBarSheet());

/// Choose the three pages in the bottom bar and their order. Everything else stays under More.
class BottomBarSheet extends StatefulWidget {
  const BottomBarSheet({super.key});

  @override
  State<BottomBarSheet> createState() => _BottomBarSheetState();
}

class _BottomBarSheetState extends State<BottomBarSheet> {
  List<String>? _picked;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final picked = _picked ??= [for (final t in barTabs(store.settings)) t.key];
    final full = picked.length == 3;
    final others = [for (final b in barPages) if (!picked.contains(b.key)) b];

    void save() {
      store.updateSettings((s) => s.navTabs = [...picked]);
      Navigator.pop(context);
    }

    Widget preview() {
      Widget slot(IconData icon, String label, {bool empty = false}) => Expanded(
            child: Column(
              children: [
                Icon(icon, size: 21, color: empty ? p.border : p.muted),
                const SizedBox(height: 3),
                FittedBox(
                  fit: BoxFit.scaleDown,
                  child: Text(label, maxLines: 1, style: TextStyle(fontSize: 11, color: empty ? p.muted.withValues(alpha: 0.5) : p.muted, fontWeight: FontWeight.w600)),
                ),
              ],
            ),
          );
      Widget at(int i) => i < picked.length ? slot(barPage(picked[i])!.icon, barPage(picked[i])!.label) : slot(Icons.add_circle_outline_rounded, 'Pick one', empty: true);
      return MediaQuery.withClampedTextScaling(
        maxScaleFactor: 1.2,
        child: Container(
          padding: const EdgeInsets.fromLTRB(6, 12, 6, 10),
          decoration: BoxDecoration(color: p.surfaceAlt, borderRadius: BorderRadius.circular(16)),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              at(0),
              at(1),
              Container(width: 40, height: 40, decoration: BoxDecoration(color: p.accent, shape: BoxShape.circle), child: Icon(Icons.add_rounded, color: p.onAccent)),
              at(2),
              slot(Icons.grid_view_rounded, 'More'),
            ],
          ),
        ),
      );
    }

    return SheetFrame(
      title: 'Bottom bar',
      subtitle: 'Pick the three pages you use most. Everything else is a tap away under More.',
      primary: full ? 'Save' : 'Pick ${3 - picked.length} more',
      onPrimary: full ? save : null,
      secondary: TextButton(
        onPressed: () => setState(() => _picked = [...AppSettings.defaultNavTabs]),
        child: const Text('Use the default (Home, Transactions, Budgets)'),
      ),
      children: [
        const SizedBox(height: 18),
        preview(),
        const SizedBox(height: 18),
        Text('IN THE BAR · DRAG TO REORDER', style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700, letterSpacing: 0.6, color: p.muted)),
        const SizedBox(height: 6),
        ReorderableListView(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          buildDefaultDragHandles: false,
          onReorder: (from, to) => setState(() {
            if (to > from) to--;
            picked.insert(to, picked.removeAt(from));
          }),
          children: [
            for (final (i, k) in picked.indexed)
              _PageRow(
                key: ValueKey(k),
                page: barPage(k)!,
                trailing: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    IconButton(
                      tooltip: 'Take ${barPage(k)!.label} out of the bar',
                      onPressed: () => setState(() => picked.remove(k)),
                      icon: Icon(Icons.remove_circle_outline_rounded, color: p.danger),
                    ),
                    ReorderableDragStartListener(
                      index: i,
                      child: Padding(padding: const EdgeInsets.all(8), child: Icon(Icons.drag_handle_rounded, color: p.muted)),
                    ),
                  ],
                ),
              ),
          ],
        ),
        const SizedBox(height: 14),
        Text('MORE PAGES', style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700, letterSpacing: 0.6, color: p.muted)),
        const SizedBox(height: 6),
        for (final b in others)
          _PageRow(
            page: b,
            onTap: full ? null : () => setState(() => picked.add(b.key)),
            trailing: IconButton(
              tooltip: 'Put ${b.label} in the bar',
              onPressed: full ? null : () => setState(() => picked.add(b.key)),
              icon: Icon(Icons.add_circle_outline_rounded, color: full ? p.border : p.accent),
            ),
          ),
        if (full) ...[
          const SizedBox(height: 8),
          const HintNote('The bar is full. Take a page out to put a different one in.'),
        ],
      ],
    );
  }
}

class _PageRow extends StatelessWidget {
  const _PageRow({super.key, required this.page, required this.trailing, this.onTap});
  final BarPage page;
  final Widget trailing;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: Row(
            children: [
              IconBox(page.icon, size: 38),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(page.label, style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700)),
                    Text(page.blurb, maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 13, color: p.muted)),
                  ],
                ),
              ),
              trailing,
            ],
          ),
        ),
      ),
    );
  }
}
