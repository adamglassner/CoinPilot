import 'package:flutter/material.dart';

import '../dates.dart';
import '../models.dart';
import '../money.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import 'budget_sheet.dart';
import 'transaction_sheet.dart';

class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key, required this.onNavigate});
  final ValueChanged<int> onNavigate;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final now = DateTime.now();
    final month = monthStart(now);

    return SafeArea(
      bottom: false,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 14, 20, 32),
        children: [
          Row(
            children: [
              const CoinLogo(size: 32),
              const SizedBox(width: 10),
              Text('CoinPilot', style: display(22, p.text, weight: FontWeight.w800)),
              const Spacer(),
              Container(
                padding: const EdgeInsets.fromLTRB(10, 6, 12, 6),
                decoration: BoxDecoration(
                  color: p.surface,
                  borderRadius: BorderRadius.circular(99),
                  border: Border.all(color: p.border),
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(Icons.phone_android_rounded, size: 15, color: p.accent),
                    const SizedBox(width: 5),
                    Text('On this phone', style: TextStyle(fontSize: 12.5, fontWeight: FontWeight.w600, color: p.text)),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 22),
          Text(greeting(now), style: display(26, p.text, weight: FontWeight.w800)),
          const SizedBox(height: 2),
          Text('${longDays[now.weekday - 1]}, ${monthNames[now.month - 1]} ${now.day}',
              style: TextStyle(fontSize: 14.5, color: p.muted)),
          const SizedBox(height: 16),
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

class _Welcome extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    final store = StoreScope.read(context);
    return EmptyState(
      icon: Icons.waving_hand_outlined,
      title: 'Welcome to CoinPilot',
      text: 'Add what you spend and earn, set a few budgets, and see what\'s left to spend this month. '
          'Everything stays on this phone.',
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
    final spent = store.spentIn(month);
    final budgeted = store.budgetedTotal;
    final hasBudgets = budgeted > 0;
    final left = store.leftToSpend(month);
    final over = hasBudgets && left < 0;
    final days = daysLeftInMonth(now);
    final monthName = monthNames[month.month - 1];

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
                Text('${Money.format(spent, whole: true)} of ${Money.format(budgeted, whole: true)} spent',
                    style: TextStyle(fontSize: 13.5, color: p.heroMuted)),
                const Spacer(),
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
                  Text('Set a budget to see what\'s left',
                      style: TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: p.mint)),
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
              Text(label, style: TextStyle(fontSize: 13.5, color: p.muted)),
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
        double f(Budget x) => x.limitCents <= 0 ? 0 : store.spentIn(month, category: x.category) / x.limitCents;
        return f(b).compareTo(f(a));
      });
    if (budgets.isEmpty) return const SizedBox.shrink();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionTitle('Budgets', action: 'See all', onAction: onSeeAll),
        GridView.count(
          crossAxisCount: 2,
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          mainAxisSpacing: 10,
          crossAxisSpacing: 10,
          childAspectRatio: 1.75,
          padding: EdgeInsets.zero,
          children: [
            for (final b in budgets.take(4))
              Builder(builder: (context) {
                final spent = store.spentIn(month, category: b.category);
                final left = b.limitCents - spent;
                return Panel(
                  padding: const EdgeInsets.fromLTRB(14, 12, 14, 14),
                  onTap: () => showBudgetSheet(context, existing: b),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Text(b.category, maxLines: 1, overflow: TextOverflow.ellipsis,
                          style: const TextStyle(fontSize: 14.5, fontWeight: FontWeight.w600)),
                      Text(left >= 0 ? '${Money.format(left, whole: true)} left' : '${Money.format(-left, whole: true)} over',
                          style: TextStyle(fontSize: 13, color: left >= 0 ? p.muted : p.danger, fontWeight: left >= 0 ? FontWeight.w400 : FontWeight.w600)),
                      ProgressBar(
                        value: b.limitCents == 0 ? 0 : spent / b.limitCents,
                        color: budgetColor(p, spent, b.limitCents),
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
