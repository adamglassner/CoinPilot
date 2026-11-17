import 'package:flutter/material.dart';

import '../dates.dart';
import '../insights.dart';
import '../kit.dart';
import '../money.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

Future<void> showPaycheckPlan(BuildContext context, IncomeSource s, {DateTime? payday, int? amountCents}) =>
    showSheet<void>(context, PaycheckPlanSheet(source: s, payday: payday ?? s.nextDate, amountCents: amountCents));

/// How a paycheck could be split until the next one: bills, budgets, debts, savings, and what's left over.
class PaycheckPlanSheet extends StatelessWidget {
  const PaycheckPlanSheet({super.key, required this.source, required this.payday, this.amountCents});
  final IncomeSource source;
  final DateTime payday;
  final int? amountCents;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final plan = store.planPaycheck(source, payday, amountCents: amountCents);
    final bills = plan.lines.where((l) => l.kind == 'bill').toList()..sort((a, b) => a.date.compareTo(b.date));
    final others = plan.lines.where((l) => l.kind != 'bill').toList();
    final short = plan.leftoverCents < 0;
    Widget line(String label, String sub, int cents, IconData icon) => Padding(
          padding: const EdgeInsets.symmetric(vertical: 6),
          child: Row(
            children: [
              IconBox(icon, size: 36),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(label, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
                    if (sub.isNotEmpty) Text(sub, style: TextStyle(fontSize: 12.5, color: p.muted)),
                  ],
                ),
              ),
              Text(Money.format(cents), style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
            ],
          ),
        );
    return SheetFrame(
      title: 'Plan this paycheck',
      subtitle: '${source.name} on ${shortDate(plan.payday)}: ${Money.format(plan.amountCents)}. Here\'s what it needs to cover until the next one on ${shortDate(plan.next)}.',
      leading: const IconBox(Icons.payments_outlined, size: 44),
      children: [
        const SizedBox(height: 14),
        Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(color: short ? p.danger.withValues(alpha: 0.12) : p.accentSoft, borderRadius: BorderRadius.circular(16)),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(short ? 'Short by' : 'Left over', style: TextStyle(fontSize: 13.5, color: p.muted)),
              Text(Money.format(plan.leftoverCents.abs()), key: const Key('plan-leftover'), style: display(30, short ? p.danger : p.positive, weight: FontWeight.w800)),
              const SizedBox(height: 4),
              Text(
                short
                    ? 'This paycheck doesn\'t cover everything before the next one. Spending a little less from your budgets, or moving a bill, closes the gap.'
                    : plan.leftoverCents > 0
                        ? 'Ideas: put it toward a goal, pay extra on a debt, or keep it as a cushion.'
                        : 'Every dollar has a job.',
                style: TextStyle(fontSize: 13.5, color: p.text, height: 1.35),
              ),
            ],
          ),
        ),
        if (bills.isNotEmpty) ...[
          SectionTitle('Bills due · ${Money.format(plan.total('bill'), whole: true)}'),
          for (final b in bills) line(b.label, dayLabel(b.date), b.cents, Icons.event_note_outlined),
        ],
        if (others.isNotEmpty) ...[
          const SectionTitle('Spending and saving'),
          for (final o in others)
            line(
              o.kind == 'budget' ? 'Budgets' : o.label,
              switch (o.kind) { 'budget' => o.label.replaceFirst('Budgets for', 'For the next'), 'debt' => 'Debt payment', _ => 'Savings' },
              o.cents,
              switch (o.kind) { 'budget' => Icons.pie_chart_outline_rounded, 'debt' => Icons.credit_card_rounded, _ => Icons.savings_outlined },
            ),
        ],
        if (plan.lines.isEmpty) ...[
          const SizedBox(height: 14),
          const HintNote('Nothing is planned yet. Add your bills and budgets to see what each paycheck needs to cover.'),
        ],
      ],
    );
  }
}
