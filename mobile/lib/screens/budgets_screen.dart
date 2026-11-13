import 'package:flutter/material.dart';

import '../dates.dart';
import '../models.dart';
import '../money.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import 'budget_sheet.dart';

class BudgetsScreen extends StatefulWidget {
  const BudgetsScreen({super.key});

  @override
  State<BudgetsScreen> createState() => _BudgetsScreenState();
}

class _BudgetsScreenState extends State<BudgetsScreen> {
  DateTime _month = monthStart(DateTime.now());

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final budgets = store.budgets;
    final budgeted = store.budgetedTotal;
    final spentBudgeted = budgets.fold<int>(0, (s, b) => s + store.spentIn(_month, category: b.category));
    final left = budgeted - spentBudgeted;
    final unbudgeted = store.unbudgeted(_month);

    return SafeArea(
      bottom: false,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 18, 20, 40),
        children: [
          ScreenHeader(
            title: 'Budgets',
            subtitle: 'A monthly limit for each kind of spending.',
            trailing: IconButton.filled(
              tooltip: 'New budget',
              onPressed: () => showBudgetSheet(context),
              style: IconButton.styleFrom(
                backgroundColor: p.accentSoft,
                foregroundColor: p.accent,
                fixedSize: const Size(46, 46),
              ),
              icon: const Icon(Icons.add_rounded),
            ),
          ),
          MonthSwitcher(month: _month, onChanged: (m) => setState(() => _month = m)),
          const SizedBox(height: 12),
          if (budgets.isEmpty)
            EmptyState(
              icon: Icons.pie_chart_outline_rounded,
              title: 'No budgets yet',
              text: 'Pick a category like Groceries and set how much you want to spend on it each month. '
                  'CoinPilot shows what\'s left as you go.',
              actions: [
                SizedBox(
                  width: double.infinity,
                  child: FilledButton.icon(
                    onPressed: () => showBudgetSheet(context),
                    icon: const Icon(Icons.add_rounded),
                    label: const Text('Create a budget'),
                  ),
                ),
              ],
            )
          else ...[
            Container(
              padding: const EdgeInsets.fromLTRB(20, 18, 20, 18),
              decoration: BoxDecoration(color: p.hero, borderRadius: BorderRadius.circular(22)),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      _HeroFigure(label: 'Budgeted', value: Money.format(budgeted, whole: true)),
                      _HeroFigure(label: 'Spent', value: Money.format(spentBudgeted, whole: true)),
                      _HeroFigure(
                        label: left >= 0 ? 'Left' : 'Over',
                        value: Money.format(left.abs(), whole: true),
                        color: left >= 0 ? p.mint : const Color(0xFFFFB38A),
                      ),
                    ],
                  ),
                  const SizedBox(height: 14),
                  ProgressBar(
                    value: budgeted == 0 ? 0 : spentBudgeted / budgeted,
                    color: left >= 0 ? p.mint : const Color(0xFFFF8A5B),
                    track: p.heroTrack,
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),
            for (final b in budgets) ...[
              _BudgetCard(budget: b, spent: store.spentIn(_month, category: b.category)),
              const SizedBox(height: 10),
            ],
          ],
          if (unbudgeted.isNotEmpty) ...[
            const SectionTitle('Not budgeted'),
            Panel(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 4),
              child: Column(
                children: [
                  for (final (i, e) in unbudgeted.indexed) ...[
                    if (i > 0) const Divider(),
                    Padding(
                      padding: const EdgeInsets.symmetric(vertical: 10),
                      child: Row(
                        children: [
                          CategoryAvatar(e.key, size: 38),
                          const SizedBox(width: 12),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(e.key, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
                                Text('${Money.format(e.value)} spent', style: TextStyle(fontSize: 13, color: p.muted)),
                              ],
                            ),
                          ),
                          TextButton(
                            onPressed: () => showBudgetSheet(context, category: e.key),
                            child: const Text('Budget it'),
                          ),
                        ],
                      ),
                    ),
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

class _HeroFigure extends StatelessWidget {
  const _HeroFigure({required this.label, required this.value, this.color});
  final String label;
  final String value;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Expanded(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: TextStyle(fontSize: 13, color: p.heroMuted)),
          const SizedBox(height: 4),
          FittedBox(
            fit: BoxFit.scaleDown,
            alignment: Alignment.centerLeft,
            child: Text(value, style: display(22, color ?? p.heroText)),
          ),
        ],
      ),
    );
  }
}

class _BudgetCard extends StatelessWidget {
  const _BudgetCard({required this.budget, required this.spent});
  final Budget budget;
  final int spent;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final limit = budget.limitCents;
    final left = limit - spent;
    final color = budgetColor(p, spent, limit);
    final pct = limit == 0 ? 0 : (spent * 100 / limit).round();
    return Panel(
      onTap: () => showBudgetSheet(context, existing: budget),
      child: Column(
        children: [
          Row(
            children: [
              CategoryAvatar(budget.category),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(budget.category, style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w600)),
                    const SizedBox(height: 2),
                    Text('${Money.format(spent, whole: true)} of ${Money.format(limit, whole: true)} · $pct%',
                        style: TextStyle(fontSize: 13, color: p.muted)),
                  ],
                ),
              ),
              Column(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Text(Money.format(left.abs(), whole: true),
                      style: display(18, left >= 0 ? p.text : p.danger)),
                  Text(left >= 0 ? 'left' : 'over',
                      style: TextStyle(fontSize: 12.5, color: left >= 0 ? p.muted : p.danger)),
                ],
              ),
            ],
          ),
          const SizedBox(height: 12),
          ProgressBar(value: limit == 0 ? 0 : spent / limit, color: color),
        ],
      ),
    );
  }
}
