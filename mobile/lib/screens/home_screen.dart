import 'package:flutter/material.dart';

import '../form.dart';

import '../dates.dart';
import '../models.dart';
import '../money.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import '../sync/scope.dart';
import '../finance.dart';
import '../form.dart';
import '../kit.dart';
import 'bills_screen.dart';
import 'budget_sheet.dart';
import 'income_screen.dart';
import 'more_screen.dart';
import 'settings_screen.dart';
import 'sync_screen.dart';
import 'transaction_sheet.dart';

class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key, required this.onNavigate});
  final ValueChanged<int> onNavigate;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final now = DateTime.now();
    final month = store.budgetMonth(now);
    final updater = UpdaterScope.of(context);

    return SafeArea(
      bottom: false,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 14, 20, 32),
        children: [
          if (!isDesktop)
            // With big phone text the name still fits: the text grows a little less here, the sync pill shows just its
            // icon, and the name shrinks to fit rather than being cut off
            MediaQuery.withClampedTextScaling(
              maxScaleFactor: 1.3,
              child: Row(
                children: [
                  const CoinLogo(size: 32),
                  const SizedBox(width: 10),
                  Expanded(
                    child: FittedBox(
                      fit: BoxFit.scaleDown,
                      alignment: Alignment.centerLeft,
                      child: Text('WillowWallet', maxLines: 1, softWrap: false, style: display(22, p.text, weight: FontWeight.w800)),
                    ),
                  ),
                  const SizedBox(width: 8),
                  SyncPill(onTap: () => SyncScreen.open(context), compact: MediaQuery.textScalerOf(context).scale(1) > 1.2),
                ],
              ),
            ),
          const SizedBox(height: 22),
          Text(greeting(now), style: display(26, p.text, weight: FontWeight.w800)),
          const SizedBox(height: 2),
          Text('${longDays[now.weekday - 1]}, ${monthNames[now.month - 1]} ${now.day}',
              style: TextStyle(fontSize: 14.5, color: p.muted)),
          const SizedBox(height: 16),
          if (updater.available != null && !updater.dismissed) ...[
            UpdateBanner(onOpen: () => pushPage(context, const SettingsScreen())),
            const SizedBox(height: 12),
          ],
          if (store.isEmpty) ...[
            _Welcome(),
          ] else ...[
            _Hero(month: month, now: now),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                    child: _Stat(
                        label: 'Income', value: Money.format(store.incomeIn(month)), icon: Icons.south_west_rounded)),
                const SizedBox(width: 12),
                Expanded(
                  child: _Stat(
                    label: 'Net',
                    value: Money.format(store.incomeIn(month) - store.spentIn(month), signed: true),
                    icon: Icons.swap_vert_rounded,
                    valueColor: store.incomeIn(month) - store.spentIn(month) < 0 ? p.danger : p.positive,
                  ),
                ),
              ],
            ),
            _ComingUp(now: now),
            _BudgetsPreview(month: month, onSeeAll: () => onNavigate(2)),
            const SectionTitle('Spending by week'),
            _WeekChart(month: month, now: now),
            SectionTitle('Recent', action: 'View all', onAction: () => onNavigate(1)),
            Panel(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 4),
              child: Column(
                children: [
                  for (final (i, t) in store.recent(5).indexed) ...[
                    if (i > 0) const Divider(),
                    TxnTile(t, showDate: true, onTap: () => showTransactionSheet(context, existing: t)),
                  ],
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }
}

/// Bills due in the next week and paydays that need an answer.
class _ComingUp extends StatelessWidget {
  const _ComingUp({required this.now});
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final today = dayOnly(now);
    final bills = store.billsDueWithin(7, today);
    final asking = store.askingPaydays(today);
    final paydays = [
      for (final s in store.incomeSources.where((s) => !s.isDone && !s.needsAsking))
        if (!dayOnly(s.nextDate).isAfter(today.add(const Duration(days: 7)))) s,
    ];
    if (bills.isEmpty && asking.isEmpty && paydays.isEmpty) return const SizedBox.shrink();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionTitle('Coming up', action: 'All bills', onAction: () => pushPage(context, const BillsScreen())),
        ListPanel(
          children: [
            for (final s in asking)
              RowTile(
                leading: const IconBox(Icons.payments_outlined, size: 40),
                title: 'Payday: ${s.name}',
                subtitle: s.varies ? 'Tap to enter how much came in' : 'Tap to confirm it came in',
                trailing: dayLabel(s.nextDate),
                trailingColor: p.accent,
                onTap: () => showPaydaySheet(context, asking),
              ),
            for (final b in bills.take(5))
              RowTile(
                leading: CategoryAvatar(b.category, size: 40),
                title: b.name,
                subtitle: '${Bills.statusText(b, today)}${b.autoPay ? ' · autopay' : ''}',
                subtitleColor: Bills.status(b, today) == BillStatus.overdue ? p.danger : null,
                trailing: Money.format(b.amountCents),
                onTap: () => showBillActions(context, b),
              ),
            for (final s in paydays.take(2))
              RowTile(
                leading: IconBox(Icons.south_west_rounded, size: 40, color: p.positive),
                title: s.name,
                subtitle: 'Payday ${dayLabel(s.nextDate).toLowerCase()}',
                trailing: Money.format(s.amountCents, signed: true),
                trailingColor: p.positive,
              ),
          ],
        ),
      ],
    );
  }
}

class _Welcome extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    final store = StoreScope.read(context);
    return EmptyState(
      icon: Icons.waving_hand_outlined,
      title: 'Welcome to WillowWallet',
      text: 'Add what you spend and earn, set a few budgets, and see what\'s left to spend this month. '
          'Everything stays on this $deviceWord.',
      actions: [
        SizedBox(
          width: double.infinity,
          child: FilledButton.icon(
            onPressed: () => showTransactionSheet(context),
            icon: const Icon(Icons.add_rounded),
            label: const Text('Add your first transaction'),
          ),
        ),
        SizedBox(
          width: double.infinity,
          child: OutlinedButton.icon(
            onPressed: () => showBudgetSheet(context),
            icon: const Icon(Icons.pie_chart_outline_rounded),
            label: const Text('Set up a budget'),
          ),
        ),
        TextButton(
          onPressed: () {
            store.loadSample();
            toast(context, 'Sample data added. Erase it any time in Settings.');
          },
          child: const Text('Explore with sample data'),
        ),
      ],
    );
  }
}

/// "Left to spend in November" (or "Spent in November" before any budgets).
class _Hero extends StatelessWidget {
  const _Hero({required this.month, required this.now});
  final DateTime month;
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final hasBudgets = store.budgets.isNotEmpty;
    final spent = hasBudgets ? store.spentInBudgets(month) : store.spentIn(month);
    final budgeted = store.availableIn(month);
    final left = store.leftToSpend(month);
    final over = hasBudgets && left < 0;
    final days = store.monthEnd(month).difference(dayOnly(now)).inDays + 1;
    final monthName = store.monthName(month);

    return Container(
      padding: const EdgeInsets.fromLTRB(22, 20, 22, 20),
      decoration: BoxDecoration(color: p.hero, borderRadius: BorderRadius.circular(24)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            !hasBudgets ? 'Spent in $monthName' : (over ? 'Over budget in $monthName' : 'Left to spend in $monthName'),
            style: TextStyle(fontSize: 14.5, color: p.heroMuted),
          ),
          const SizedBox(height: 6),
          FittedBox(
            fit: BoxFit.scaleDown,
            alignment: Alignment.centerLeft,
            child: Text(
              Money.format(hasBudgets ? left.abs() : spent),
              style: display(44, over ? const Color(0xFFFFB38A) : p.heroText, weight: FontWeight.w800),
            ),
          ),
          const SizedBox(height: 14),
          if (hasBudgets) ...[
            ProgressBar(
              value: budgeted == 0 ? 0 : spent / budgeted,
              color: over ? const Color(0xFFFF8A5B) : p.mint,
              track: p.heroTrack,
              height: 9,
            ),
            const SizedBox(height: 10),
            Row(
              children: [
                Expanded(
                  child: Text('${Money.format(spent, whole: true)} of ${Money.format(budgeted, whole: true)} spent',
                      maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 13.5, color: p.heroMuted)),
                ),
                const SizedBox(width: 8),
                Text('$days ${days == 1 ? 'day' : 'days'} left', style: TextStyle(fontSize: 13.5, color: p.heroMuted)),
              ],
            ),
          ] else
            GestureDetector(
              onTap: () => showBudgetSheet(context),
              child: Row(
                children: [
                  Icon(Icons.add_circle_outline_rounded, size: 18, color: p.mint),
                  const SizedBox(width: 6),
                  Flexible(
                    child: Text('Set a budget to see what\'s left',
                        style: TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: p.mint)),
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }
}

class _Stat extends StatelessWidget {
  const _Stat({required this.label, required this.value, required this.icon, this.valueColor});
  final String label;
  final String value;
  final IconData icon;
  final Color? valueColor;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Panel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(icon, size: 16, color: p.muted),
              const SizedBox(width: 6),
              Flexible(
                child: Text(label,
                    maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 13.5, color: p.muted)),
              ),
            ],
          ),
          const SizedBox(height: 8),
          FittedBox(
            fit: BoxFit.scaleDown,
            alignment: Alignment.centerLeft,
            child: Text(value, style: display(22, valueColor ?? p.text)),
          ),
        ],
      ),
    );
  }
}

class _BudgetsPreview extends StatelessWidget {
  const _BudgetsPreview({required this.month, required this.onSeeAll});
  final DateTime month;
  final VoidCallback onSeeAll;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final budgets = store.budgets.toList()
      ..sort((a, b) {
        double f(Budget x) {
          final a = store.availableFor(x, month);
          return a <= 0 ? 0 : store.spentIn(month, category: x.category) / a;
        }
        return f(b).compareTo(f(a));
      });
    if (budgets.isEmpty) return const SizedBox.shrink();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionTitle('Budgets', action: 'See all', onAction: onSeeAll),
        GridView.count(
          crossAxisCount: isDesktop ? 4 : 2,
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          mainAxisSpacing: 10,
          crossAxisSpacing: 10,
          childAspectRatio: isDesktop ? 1.9 : 1.75,
          padding: EdgeInsets.zero,
          children: [
            for (final b in budgets.take(isDesktop ? 8 : 4))
              Builder(builder: (context) {
                final spent = store.spentIn(month, category: b.category);
                final available = store.availableFor(b, month);
                final left = available - spent;
                return Panel(
                  padding: const EdgeInsets.fromLTRB(14, 12, 14, 14),
                  onTap: () => showBudgetSheet(context, existing: b, month: month),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Text(b.category, maxLines: 1, overflow: TextOverflow.ellipsis,
                          style: const TextStyle(fontSize: 14.5, fontWeight: FontWeight.w600)),
                      Text(left >= 0 ? '${Money.format(left, whole: true)} left' : '${Money.format(-left, whole: true)} over',
                          style: TextStyle(fontSize: 13, color: left >= 0 ? p.muted : p.danger, fontWeight: left >= 0 ? FontWeight.w400 : FontWeight.w600)),
                      ProgressBar(
                        value: available <= 0 ? 0 : spent / available,
                        color: budgetColor(p, spent, available),
                        height: 6,
                      ),
                    ],
                  ),
                );
              }),
          ],
        ),
      ],
    );
  }
}

class _WeekChart extends StatelessWidget {
  const _WeekChart({required this.month, required this.now});
  final DateTime month;
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final weeks = store.weeklySpending(month);
    final top = weeks.fold<int>(1, (m, w) => w > m ? w : m);
    final currentWeek = ((now.day - 1) ~/ 7).clamp(0, 4);
    final lastWeek = ((daysInMonth(month) - 1) ~/ 7).clamp(0, 4);
    return Panel(
      padding: const EdgeInsets.fromLTRB(16, 18, 16, 14),
      child: SizedBox(
        height: 150,
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            for (var i = 0; i <= lastWeek; i++)
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 6),
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.end,
                    children: [
                      Text(weeks[i] == 0 ? '' : Money.format(weeks[i], whole: true),
                          style: TextStyle(fontSize: 11.5, color: p.muted, fontWeight: FontWeight.w600)),
                      const SizedBox(height: 4),
                      Flexible(
                        child: FractionallySizedBox(
                          heightFactor: weeks[i] == 0 ? 0.02 : (weeks[i] / top).clamp(0.04, 1.0),
                          child: Container(
                            decoration: BoxDecoration(
                              color: i == currentWeek ? p.accent : p.barMuted,
                              borderRadius: BorderRadius.circular(8),
                            ),
                          ),
                        ),
                      ),
                      const SizedBox(height: 8),
                      Text('Wk ${i + 1}',
                          style: TextStyle(
                              fontSize: 12,
                              color: i == currentWeek ? p.text : p.muted,
                              fontWeight: i == currentWeek ? FontWeight.w700 : FontWeight.w500)),
                    ],
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
