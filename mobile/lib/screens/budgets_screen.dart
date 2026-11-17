import 'package:flutter/material.dart';

import '../finance.dart';
import '../kit.dart';
import '../models.dart';
import '../money.dart';
import '../platform.dart';
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
  DateTime? _month;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final month = _month ?? store.budgetMonth(DateTime.now());
    final budgets = store.budgets;
    final available = store.availableIn(month);
    final spentBudgeted = store.spentInBudgets(month);
    final left = available - spentBudgeted;
    final unbudgeted = store.unbudgeted(month);
    final prev = DateTime(month.year, month.month - 1, month.day);
    final differs = budgets.where((b) => store.limitFor(b, prev) != store.limitFor(b, month)).length;

    return SafeArea(
      bottom: false,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 18, 20, 40),
        children: [
          ScreenHeader(
            title: 'Budgets',
            subtitle: 'A monthly limit for each kind of spending.',
            trailing: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                PopupMenuButton<String>(
                  tooltip: 'More',
                  icon: Icon(Icons.more_vert_rounded, color: p.text),
                  onSelected: (v) {
                    if (v == 'template') showSheet<void>(context, const TemplateSheet());
                    if (v == 'copy') {
                      final n = store.copyLastMonth(month);
                      toast(context, n == 0 ? 'Nothing to copy.' : 'Used last month\'s amounts for $n budget${n == 1 ? '' : 's'}.');
                    }
                  },
                  itemBuilder: (_) => [
                    const PopupMenuItem(value: 'template', child: Text('Use a template…')),
                    PopupMenuItem(value: 'copy', enabled: differs > 0, child: Text(differs > 0 ? 'Copy last month ($differs differ)' : 'Copy last month')),
                  ],
                ),
                IconButton.filled(
                  tooltip: 'New budget',
                  onPressed: () => showBudgetSheet(context, month: month),
                  style: IconButton.styleFrom(backgroundColor: p.accentSoft, foregroundColor: p.accent, fixedSize: const Size(46, 46)),
                  icon: const Icon(Icons.add_rounded),
                ),
              ],
            ),
          ),
          MonthSwitcher(month: month, onChanged: (m) => setState(() => _month = store.budgetMonth(m))),
          const SizedBox(height: 12),
          if (budgets.isEmpty)
            EmptyState(
              icon: Icons.pie_chart_outline_rounded,
              title: 'No budgets yet',
              text: 'Pick a category like Groceries and set how much you want to spend on it each month, or let a template (like 50/30/20) set them up.',
              actions: [
                SizedBox(
                  width: double.infinity,
                  child: FilledButton.icon(onPressed: () => showBudgetSheet(context, month: month), icon: const Icon(Icons.add_rounded), label: const Text('Create a budget')),
                ),
                SizedBox(
                  width: double.infinity,
                  child: OutlinedButton.icon(
                    onPressed: () => showSheet<void>(context, const TemplateSheet()),
                    icon: const Icon(Icons.auto_awesome_outlined),
                    label: const Text('Use a template'),
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
                      _HeroFigure(label: 'Budgeted', value: Money.format(available, whole: true)),
                      _HeroFigure(label: 'Spent', value: Money.format(spentBudgeted, whole: true)),
                      _HeroFigure(label: left >= 0 ? 'Left' : 'Over', value: Money.format(left.abs(), whole: true), color: left >= 0 ? p.mint : const Color(0xFFFFB38A)),
                    ],
                  ),
                  const SizedBox(height: 14),
                  ProgressBar(value: available == 0 ? 0 : spentBudgeted / available, color: left >= 0 ? p.mint : const Color(0xFFFF8A5B), track: p.heroTrack),
                ],
              ),
            ),
            const SizedBox(height: 12),
            for (final b in budgets) ...[
              _BudgetCard(budget: b, month: month),
              const SizedBox(height: 10),
            ],
          ],
          CutsCard(month: month),
          if (unbudgeted.isNotEmpty) ...[
            const SectionTitle('Not budgeted'),
            ListPanel(
              children: [
                for (final e in unbudgeted)
                  Padding(
                    padding: const EdgeInsets.symmetric(vertical: 8),
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
                        TextButton(onPressed: () => showBudgetSheet(context, category: e.key, month: month), child: const Text('Budget it')),
                      ],
                    ),
                  ),
              ],
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
          FittedBox(fit: BoxFit.scaleDown, alignment: Alignment.centerLeft, child: Text(value, style: display(22, color ?? p.heroText))),
        ],
      ),
    );
  }
}

class _BudgetCard extends StatelessWidget {
  const _BudgetCard({required this.budget, required this.month});
  final Budget budget;
  final DateTime month;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final spent = store.spentIn(month, category: budget.category);
    final limit = store.limitFor(budget, month);
    final carry = store.carryFor(budget, month);
    final available = limit + carry;
    final left = available - spent;
    final pct = available <= 0 ? 0 : (spent * 100 / available).round();
    final notes = [
      '${Money.format(spent, whole: true)} of ${Money.format(available, whole: true)} · $pct%',
      if (carry != 0) '${carry > 0 ? '+' : '−'}${Money.format(carry.abs(), whole: true)} rolled over',
      if (limit != budget.limitCents) 'this month only',
    ];
    return Panel(
      onTap: () => showBudgetSheet(context, existing: budget, month: month),
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
                    Row(
                      children: [
                        Flexible(child: Text(budget.category, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w600))),
                        if (budget.rollover) ...[const SizedBox(width: 6), Icon(Icons.autorenew_rounded, size: 15, color: p.muted)],
                      ],
                    ),
                    const SizedBox(height: 2),
                    Text(notes.join(' · '), style: TextStyle(fontSize: 13, color: p.muted)),
                  ],
                ),
              ),
              Column(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Text(Money.format(left.abs(), whole: true), style: display(18, left >= 0 ? p.text : p.danger)),
                  Text(left >= 0 ? 'left' : 'over', style: TextStyle(fontSize: 12.5, color: left >= 0 ? p.muted : p.danger)),
                ],
              ),
            ],
          ),
          const SizedBox(height: 12),
          ProgressBar(value: available <= 0 ? (spent > 0 ? 1 : 0) : spent / available, color: budgetColor(p, spent, available)),
        ],
      ),
    );
  }
}

// ----- Subscriptions you could cut -----

/// Subscriptions worth cancelling, each skippable for this month or for good.
class CutsCard extends StatelessWidget {
  const CutsCard({super.key, required this.month});
  final DateTime month;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final today = DateTime.now();
    final cuts = store.suggestCuts(month, today).take(4).toList();
    final skipped = store.prefs.skippedCuts.isNotEmpty;
    if (cuts.isEmpty) {
      if (!skipped) return const SizedBox.shrink();
      return Align(
        alignment: Alignment.centerLeft,
        child: TextButton.icon(onPressed: store.resetCutSkips, icon: const Icon(Icons.visibility_outlined, size: 18), label: const Text('Show skipped subscription suggestions')),
      );
    }
    final subs = store.allSubscriptions(today);
    final monthly = subs.fold<double>(0, (s, x) => s + x.monthly).round();
    final saving = cuts.fold<int>(0, (s, c) => s + c.monthlySaving);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionTitle('Subscriptions you could cut', action: skipped ? 'Show skipped' : null, onAction: skipped ? store.resetCutSkips : null),
        Text(
          'You pay about ${Money.format(monthly, whole: true)} a month for ${subs.length} subscription${subs.length == 1 ? '' : 's'}. '
          'Cutting ${cuts.length == 1 ? 'this one' : 'these ${cuts.length}'} would save ${Money.format(saving)} a month, ${Money.format(saving * 12, whole: true)} a year.',
          style: TextStyle(fontSize: 13.5, color: p.muted, height: 1.4),
        ),
        const SizedBox(height: 10),
        for (final c in cuts) ...[_CutRow(c, month: month), const SizedBox(height: 10)],
      ],
    );
  }
}

class _CutRow extends StatelessWidget {
  const _CutRow(this.c, {required this.month});
  final CutSuggestion c;
  final DateTime month;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.read(context);
    final p = context.pal;
    final name = c.sub.name;
    return Panel(
      padding: const EdgeInsets.fromLTRB(16, 14, 8, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: 40,
                height: 40,
                alignment: Alignment.center,
                decoration: BoxDecoration(color: p.accentSoft, borderRadius: BorderRadius.circular(12)),
                child: Text(name.isEmpty ? '?' : name[0].toUpperCase(), style: display(18, p.accent)),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(name, style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700)),
                    Text('${Money.format(c.sub.cents)} ${c.sub.repeats.toLowerCase()}', style: TextStyle(fontSize: 13, color: p.muted)),
                  ],
                ),
              ),
              Padding(
                padding: const EdgeInsets.only(right: 8),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Text('Save ${Money.format(c.monthlySaving)}/mo', style: TextStyle(fontSize: 14, fontWeight: FontWeight.w700, color: p.positive)),
                    Text('${Money.format(c.yearlySaving, whole: true)} a year', style: TextStyle(fontSize: 12.5, color: p.muted)),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Padding(
            padding: const EdgeInsets.only(right: 8),
            child: Text(c.reason, style: TextStyle(fontSize: 13.5, color: p.muted, height: 1.4)),
          ),
          const SizedBox(height: 2),
          Row(
            children: [
              TextButton(
                onPressed: () => Platform2.openUrl('https://www.bing.com/search?q=${Uri.encodeComponent('how to cancel $name subscription')}'),
                child: const Text('How to cancel'),
              ),
              const Spacer(),
              PopupMenuButton<String>(
                tooltip: 'Skip $name',
                onSelected: (v) {
                  if (v == 'month') {
                    store.skipCut(c.key, month);
                    toast(context, 'Skipped $name for ${store.monthName(month)}.');
                  } else {
                    store.skipCut(c.key, null);
                    toast(context, 'WillowWallet won\'t suggest cutting $name again.');
                  }
                },
                itemBuilder: (_) => [
                  PopupMenuItem(value: 'month', child: Text('Skip for ${store.monthName(month)}')),
                  const PopupMenuItem(value: 'forever', child: Text('Don\'t suggest it again')),
                ],
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                  child: Row(
                    children: [
                      Text('Skip', style: TextStyle(fontSize: 14.5, fontWeight: FontWeight.w600, color: p.muted)),
                      Icon(Icons.arrow_drop_down_rounded, color: p.muted),
                    ],
                  ),
                ),
              ),
              TextButton(
                onPressed: () async {
                  final ok = await confirm(context,
                      title: 'Canceled $name?',
                      message: c.sub.bill != null
                          ? 'It\'s removed from your bills and won\'t be suggested again. Past charges stay in Transactions. That\'s ${Money.format(c.yearlySaving, whole: true)} a year back in your pocket.'
                          : 'WillowWallet stops watching for its charges and won\'t suggest it again. That\'s ${Money.format(c.yearlySaving, whole: true)} a year back in your pocket.',
                      confirmText: 'Yes, I canceled it');
                  if (!ok || !context.mounted) return;
                  store.markCanceled(c.sub);
                  toast(context, 'Nice: ${Money.format(c.monthlySaving)} a month saved. You could lower your Subscriptions budget to match.');
                },
                child: const Text('I canceled it'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

// ----- Templates -----

class TemplateSheet extends StatefulWidget {
  const TemplateSheet({super.key});

  @override
  State<TemplateSheet> createState() => _TemplateSheetState();
}

class _TemplateSheetState extends State<TemplateSheet> {
  String _template = BudgetTemplates.fiftyThirtyTwenty;
  late final _income = TextEditingController();
  bool _replace = false;
  bool _filled = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_filled) return;
    _filled = true;
    final income = StoreScope.read(context).incomeForTemplates();
    if (income > 0) _income.text = Money.editable(income);
  }

  @override
  void dispose() {
    _income.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final income = readMoney(_income) ?? 0;
    final plan = BudgetTemplates.plan(_template, income, store.averageSpending(), store.categoriesFor(income: false), parentOf: store.parentOf);
    final entries = plan.entries.toList()..sort((a, b) => b.value.compareTo(a.value));
    final hasBudgets = store.budgets.isNotEmpty;
    return SheetFrame(
      title: 'Budget template',
      subtitle: 'A quick start you can adjust afterwards.',
      primary: plan.isEmpty ? null : 'Set ${plan.length} budgets',
      onPrimary: () {
        final n = store.applyBudgetPlan(plan, replace: _replace);
        Navigator.pop(context);
        toast(context, n == 0 ? 'Your budgets already cover these.' : 'Set $n budget${n == 1 ? '' : 's'}.');
      },
      children: [
        const FieldLabel('Template'),
        Choices(options: BudgetTemplates.all, selected: _template, onChanged: (v) => setState(() => _template = v)),
        const SizedBox(height: 10),
        Text(BudgetTemplates.explain(_template), style: TextStyle(fontSize: 13.5, color: p.muted, height: 1.4)),
        if (_template != BudgetTemplates.starter) ...[
          const FieldLabel('Monthly income (after taxes)'),
          MoneyField(controller: _income, size: 24, onChanged: (_) => setState(() {})),
        ],
        if (hasBudgets) ...[
          const SizedBox(height: 10),
          SwitchRow(
            title: 'Replace my budgets',
            subtitle: 'Off: only categories without a budget are added.',
            value: _replace,
            onChanged: (v) => setState(() => _replace = v),
          ),
        ],
        const FieldLabel('Preview'),
        if (plan.isEmpty)
          Text(_template == BudgetTemplates.zeroBased ? 'There\'s no spending from the last few months to base budgets on yet. Try 50/30/20 or Starter amounts.' : 'Enter your monthly income.',
              style: TextStyle(color: p.muted))
        else
          ListPanel(
            children: [
              for (final e in entries)
                RowTile(
                  leading: CategoryAvatar(e.key, size: 34),
                  title: e.key,
                  subtitle: store.budgetFor(e.key) != null ? (_replace ? 'Replaces ${Money.format(store.budgetFor(e.key)!.limitCents, whole: true)}' : 'Already budgeted: kept') : null,
                  trailing: Money.format(e.value, whole: true),
                ),
            ],
          ),
        const SizedBox(height: 4),
        Row(
          children: [
            Expanded(child: Text('Total', style: TextStyle(fontWeight: FontWeight.w700, color: p.muted))),
            Text(Money.format(plan.values.fold<int>(0, (s, v) => s + v), whole: true), style: const TextStyle(fontWeight: FontWeight.w700)),
          ],
        ),
      ],
    );
  }
}
