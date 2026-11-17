import 'dart:math';

import 'package:flutter/material.dart';

import '../dates.dart';
import '../finance.dart';
import '../insights.dart';
import '../kit.dart';
import '../money.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import 'monthly_report.dart';

class ReportsScreen extends StatefulWidget {
  const ReportsScreen({super.key});

  @override
  State<ReportsScreen> createState() => _ReportsScreenState();
}

class _ReportsScreenState extends State<ReportsScreen> {
  int _range = 0; // 6 months, 12 months
  bool _byPayee = false;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final months = _range == 0 ? 6 : 12;
    final now = DateTime.now();
    final totals = store.monthlyTotals(now, months);
    final from = totals.first.month;
    final to = store.monthEnd(totals.last.month);
    final income = totals.fold<int>(0, (s, m) => s + m.income);
    final spent = totals.fold<int>(0, (s, m) => s + m.spent);
    final rate = income <= 0 ? null : (income - spent) / income;
    final cats = store.spendingByCategory(from, to);
    final payees = store.spendingByPayee(from, to);
    final history = store.netWorthHistory(months);
    final hasAccounts = store.accountRecords.isNotEmpty || store.debts.isNotEmpty;

    return SubPage(
      title: 'Reports',
      subtitle: 'Where your money went, month by month.',
      actions: [
        IconButton(tooltip: 'Monthly report', onPressed: () => pushPage(context, const MonthlyReportScreen()), icon: const Icon(Icons.summarize_outlined)),
        IconButton(tooltip: 'Year in review', onPressed: () => pushPage(context, YearReviewScreen(year: now.year)), icon: const Icon(Icons.auto_awesome_outlined)),
      ],
      children: [
        PillToggle(options: const ['6 months', '12 months'], selected: _range, onChanged: (i) => setState(() => _range = i)),
        const SizedBox(height: 12),
        Row(
          children: [
            Expanded(child: StatTile(label: 'Income', value: Money.format(income, whole: true), valueColor: p.positive)),
            const SizedBox(width: 10),
            Expanded(child: StatTile(label: 'Spent', value: Money.format(spent, whole: true))),
            const SizedBox(width: 10),
            Expanded(child: StatTile(label: 'Saved', value: rate == null ? '—' : '${(rate * 100).round()}%', valueColor: (rate ?? 0) < 0 ? p.danger : null)),
          ],
        ),
        const SectionTitle('Income and spending'),
        Panel(child: IncomeSpendingChart(totals: totals, label: (m) => shortMonths[m.month - 1].substring(0, months > 6 ? 1 : 3))),
        if (hasAccounts) ...[
          const SectionTitle('Net worth'),
          Panel(child: NetWorthChart(points: history)),
        ],
        SectionTitle(_byPayee ? 'Top payees' : 'Spending by category', action: _byPayee ? 'By category' : 'By payee', onAction: () => setState(() => _byPayee = !_byPayee)),
        if ((_byPayee ? payees.length : cats.length) == 0)
          Text('No spending in this period.', style: TextStyle(color: p.muted))
        else
          Panel(
            child: Column(
              children: [
                if (_byPayee)
                  for (final x in payees.take(10)) _BarRow(label: x.$1, sub: '${x.$3} time${x.$3 == 1 ? '' : 's'}', cents: x.$2, max: payees.first.$2)
                else
                  for (final x in cats.take(12)) _BarRow(label: x.key, sub: '${spent <= 0 ? 0 : (x.value * 100 / spent).round()}%', cents: x.value, max: cats.first.value, icon: true),
              ],
            ),
          ),
        const SizedBox(height: 14),
        FilledButton.tonalIcon(
          onPressed: () => pushPage(context, const MonthlyReportScreen()),
          icon: const Icon(Icons.summarize_outlined, size: 19),
          label: const Text('Monthly report'),
        ),
        const SectionTitle('Compared with last year'),
        _YearOverYearPanel(now: now),
        const SizedBox(height: 14),
        OutlinedButton.icon(
          onPressed: () => pushPage(context, YearReviewScreen(year: now.year)),
          icon: const Icon(Icons.auto_awesome_outlined, size: 19),
          label: Text('${now.year} in review'),
        ),
      ],
    );
  }
}

class _BarRow extends StatelessWidget {
  const _BarRow({required this.label, required this.sub, required this.cents, required this.max, this.icon = false});
  final String label, sub;
  final int cents, max;
  final bool icon;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 7),
      child: Row(
        children: [
          if (icon) ...[CategoryAvatar(label, size: 34), const SizedBox(width: 10)],
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(child: Text(label, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 14.5, fontWeight: FontWeight.w600))),
                    Text(Money.format(cents, whole: true), style: const TextStyle(fontSize: 14.5, fontWeight: FontWeight.w700)),
                  ],
                ),
                const SizedBox(height: 5),
                Row(
                  children: [
                    Expanded(child: ProgressBar(value: max <= 0 ? 0 : cents / max, height: 6)),
                    const SizedBox(width: 8),
                    SizedBox(width: 52, child: Text(sub, textAlign: TextAlign.right, style: TextStyle(fontSize: 12, color: p.muted))),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// Paired bars per month: income (green) and spending.
class IncomeSpendingChart extends StatelessWidget {
  const IncomeSpendingChart({super.key, required this.totals, required this.label});
  final List<MonthTotals> totals;
  final String Function(DateTime) label;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final top = totals.fold<int>(1, (m, t) => max(m, max(t.income, t.spent)));
    return Column(
      children: [
        SizedBox(
          height: 160,
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              for (final t in totals)
                Expanded(
                  child: Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 3),
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.end,
                      children: [
                        Expanded(
                          child: Row(
                            crossAxisAlignment: CrossAxisAlignment.end,
                            children: [
                              Expanded(child: _bar(t.income / top, p.positive)),
                              const SizedBox(width: 2),
                              Expanded(child: _bar(t.spent / top, t.spent > t.income && t.income > 0 ? p.danger : p.barMuted)),
                            ],
                          ),
                        ),
                        const SizedBox(height: 6),
                        Text(label(t.month), style: TextStyle(fontSize: 11.5, color: p.muted, fontWeight: FontWeight.w600)),
                      ],
                    ),
                  ),
                ),
            ],
          ),
        ),
        const SizedBox(height: 10),
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            _Legend(color: p.positive, text: 'Income'),
            const SizedBox(width: 16),
            _Legend(color: p.barMuted, text: 'Spending'),
          ],
        ),
      ],
    );
  }

  Widget _bar(double f, Color c) => FractionallySizedBox(
        heightFactor: f <= 0 ? 0.01 : f.clamp(0.02, 1.0),
        alignment: Alignment.bottomCenter,
        child: Container(decoration: BoxDecoration(color: c, borderRadius: BorderRadius.circular(4))),
      );
}

class _Legend extends StatelessWidget {
  const _Legend({required this.color, required this.text});
  final Color color;
  final String text;

  @override
  Widget build(BuildContext context) => Row(
        children: [
          Container(width: 10, height: 10, decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(3))),
          const SizedBox(width: 6),
          Text(text, style: TextStyle(fontSize: 12.5, color: context.pal.muted)),
        ],
      );
}

/// A line of net worth at the end of each month.
class NetWorthChart extends StatelessWidget {
  const NetWorthChart({super.key, required this.points});
  final List<(DateTime, int)> points;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final first = points.first.$2, last = points.last.$2;
    final change = last - first;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(child: Text(Money.format(last), style: display(22, p.text))),
            Text('${Money.format(change, signed: true, whole: true)} since ${shortMonths[points.first.$1.month - 1]}',
                style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: change >= 0 ? p.positive : p.danger)),
          ],
        ),
        const SizedBox(height: 12),
        SizedBox(height: 110, width: double.infinity, child: CustomPaint(painter: _LinePainter([for (final x in points) x.$2], p.accent, p.border))),
        const SizedBox(height: 6),
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(shortMonths[points.first.$1.month - 1], style: TextStyle(fontSize: 11.5, color: p.muted)),
            Text('Today', style: TextStyle(fontSize: 11.5, color: p.muted)),
          ],
        ),
      ],
    );
  }
}

class _LinePainter extends CustomPainter {
  _LinePainter(this.values, this.color, this.grid);
  final List<int> values;
  final Color color, grid;

  @override
  void paint(Canvas canvas, Size size) {
    if (values.isEmpty) return;
    final lo = values.reduce(min).toDouble(), hi = values.reduce(max).toDouble();
    final span = hi - lo == 0 ? 1.0 : hi - lo;
    Offset at(int i) => Offset(values.length == 1 ? size.width / 2 : i * size.width / (values.length - 1), size.height - 6 - (values[i] - lo) / span * (size.height - 12));
    final gridPaint = Paint()
      ..color = grid
      ..strokeWidth = 1;
    for (var i = 0; i < 3; i++) {
      final y = 6 + i * (size.height - 12) / 2;
      canvas.drawLine(Offset(0, y), Offset(size.width, y), gridPaint);
    }
    final path = Path()..moveTo(at(0).dx, at(0).dy);
    for (var i = 1; i < values.length; i++) {
      path.lineTo(at(i).dx, at(i).dy);
    }
    final fill = Path.from(path)
      ..lineTo(at(values.length - 1).dx, size.height)
      ..lineTo(at(0).dx, size.height)
      ..close();
    canvas.drawPath(fill, Paint()..color = color.withValues(alpha: 0.12));
    canvas.drawPath(
        path,
        Paint()
          ..color = color
          ..style = PaintingStyle.stroke
          ..strokeWidth = 3
          ..strokeJoin = StrokeJoin.round
          ..strokeCap = StrokeCap.round);
    canvas.drawCircle(at(values.length - 1), 5, Paint()..color = color);
  }

  @override
  bool shouldRepaint(covariant _LinePainter old) => old.values != values || old.color != color;
}

// ----- Year in review -----

class YearReviewScreen extends StatefulWidget {
  const YearReviewScreen({super.key, required this.year});
  final int year;

  @override
  State<YearReviewScreen> createState() => _YearReviewScreenState();
}

class _YearReviewScreenState extends State<YearReviewScreen> {
  late int _year = widget.year;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final r = store.yearReview(_year);
    final years = store.reviewYears();
    return SubPage(
      title: '$_year in review',
      subtitle: 'The year in numbers.',
      children: [
        if (years.length > 1) ...[
          Choices(options: [for (final y in years) '$y'], selected: '$_year', onChanged: (v) => setState(() => _year = int.parse(v))),
          const SizedBox(height: 12),
        ],
        HeroCard(
          label: r.net >= 0 ? 'You kept' : 'You spent more than came in by',
          value: Money.format(r.net.abs()),
          valueColor: r.net < 0 ? const Color(0xFFFFB38A) : null,
          footer: Text(
            r.savingsRate == null ? '${r.count} transactions' : '${(r.savingsRate! * 100).round()}% of what came in · ${r.count} transactions',
            style: TextStyle(fontSize: 13.5, color: p.heroMuted),
          ),
        ),
        const SizedBox(height: 12),
        GridView.count(
          crossAxisCount: 2,
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          mainAxisSpacing: 10,
          crossAxisSpacing: 10,
          childAspectRatio: 1.7,
          padding: EdgeInsets.zero,
          children: [
            StatTile(label: 'Came in', value: Money.format(r.income, whole: true), valueColor: p.positive, icon: Icons.south_west_rounded),
            StatTile(label: 'Spent', value: Money.format(r.spent, whole: true), icon: Icons.north_east_rounded),
            StatTile(label: 'Paid on debts', value: Money.format(r.debtPaid, whole: true), icon: Icons.credit_card_rounded),
            StatTile(label: 'Saved to goals', value: Money.format(r.savedToGoals, whole: true), icon: Icons.flag_outlined),
          ],
        ),
        const SectionTitle('Month by month'),
        Panel(child: IncomeSpendingChart(totals: r.months, label: (m) => shortMonths[m.month - 1].substring(0, 1))),
        if (r.topCategories.isNotEmpty) ...[
          const SectionTitle('Where it went'),
          Panel(
            child: Column(
              children: [
                for (final c in r.topCategories)
                  _BarRow(label: c.key, sub: '${r.spent <= 0 ? 0 : (c.value * 100 / r.spent).round()}%', cents: c.value, max: r.topCategories.first.value, icon: true),
              ],
            ),
          ),
        ],
        if (r.topPayees.isNotEmpty) ...[
          const SectionTitle('Top places'),
          ListPanel(
            children: [
              for (final (i, x) in r.topPayees.indexed)
                RowTile(
                  leading: IconBox(Icons.storefront_outlined, size: 38, background: i == 0 ? null : p.surfaceAlt),
                  title: x.$1,
                  subtitle: '${x.$3} time${x.$3 == 1 ? '' : 's'}',
                  trailing: Money.format(x.$2, whole: true),
                ),
            ],
          ),
        ],
        if (r.biggest != null) ...[
          const SectionTitle('Biggest purchase'),
          ListPanel(children: [
            RowTile(
              leading: CategoryAvatar(r.biggest!.category, size: 38),
              title: r.biggest!.payee,
              subtitle: '${shortDate(r.biggest!.date)} · ${r.biggest!.category}',
              trailing: Money.format(-r.biggest!.cents),
            ),
          ]),
        ],
        const SizedBox(height: 12),
        Text('${r.billsPaid} bill${r.billsPaid == 1 ? '' : 's'} marked paid in WillowWallet this year.',
            textAlign: TextAlign.center, style: TextStyle(fontSize: 13, color: p.muted)),
      ],
    );
  }
}

/// This budget month so far against the same days a year ago: spending, income and the categories that changed most.
class _YearOverYearPanel extends StatelessWidget {
  const _YearOverYearPanel({required this.now});
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final from = store.budgetMonth(now);
    final to = dayOnly(now);
    final y = store.yearOverYear(from, to);
    final period = '${shortMonths[from.month - 1]} ${from.day} – ${shortMonths[to.month - 1]} ${to.day}';
    if (!y.hasLastYear) {
      return Panel(
        child: Text('Once you have a year of history, this shows $period against the same days last year.', style: TextStyle(color: p.muted, height: 1.35)),
      );
    }
    Widget change(int now, int before, {bool spending = true}) {
      final c = YearOverYear.change(now, before);
      if (c == null) return Text('new', style: TextStyle(fontSize: 12.5, color: p.muted));
      final good = spending ? c <= 0 : c >= 0;
      return Text('${c > 0 ? '+' : ''}$c%', style: TextStyle(fontSize: 12.5, fontWeight: FontWeight.w700, color: c == 0 ? p.muted : (good ? p.positive : p.danger)));
    }

    return Panel(
      key: const Key('year-over-year'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('$period, ${from.year} vs ${from.year - 1}', style: TextStyle(fontSize: 13, color: p.muted)),
          const SizedBox(height: 10),
          Row(
            children: [
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text('Spent', style: TextStyle(fontSize: 13, color: p.muted)),
                  Text(Money.format(y.spent, whole: true), style: display(20, p.text)),
                  Row(children: [Text('${Money.format(y.spentBefore, whole: true)} last year  ', style: TextStyle(fontSize: 12.5, color: p.muted)), change(y.spent, y.spentBefore)]),
                ]),
              ),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text('Income', style: TextStyle(fontSize: 13, color: p.muted)),
                  Text(Money.format(y.income, whole: true), style: display(20, p.text)),
                  Row(children: [Text('${Money.format(y.incomeBefore, whole: true)} last year  ', style: TextStyle(fontSize: 12.5, color: p.muted)), change(y.income, y.incomeBefore, spending: false)]),
                ]),
              ),
            ],
          ),
          const SizedBox(height: 12),
          for (final (cat, a, b) in y.categories.take(6))
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 5),
              child: Row(
                children: [
                  CategoryAvatar(cat, size: 30),
                  const SizedBox(width: 10),
                  Expanded(child: Text(cat, style: const TextStyle(fontWeight: FontWeight.w600))),
                  Text('${Money.format(b, whole: true)} → ${Money.format(a, whole: true)}  ', style: TextStyle(fontSize: 13, color: p.muted)),
                  SizedBox(width: 48, child: Align(alignment: Alignment.centerRight, child: change(a, b))),
                ],
              ),
            ),
        ],
      ),
    );
  }
}
