import 'dart:math';

import 'package:flutter/material.dart';

import '../dates.dart';
import '../forecast.dart';
import '../kit.dart';
import '../money.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

const horizonOptions = ['Month', '30', '60', '90'];
String horizonLabel(String h) => h == 'Month' ? 'This month' : '$h days';

/// "You'll end November about $340 over", "$120 short" or "right on target".
String verdictText(Forecast f, Store store) {
  final when = store.prefs.forecastHorizon == 'Month' ? 'end ${monthNames[f.end.month - 1]}' : 'be on ${shortDate(f.end)}';
  final aim = f.targetCents == 0 ? '' : ' of your ${Money.format(f.targetCents, whole: true)} target';
  return switch (f.verdict) {
    ForecastVerdict.onTarget => 'You\'ll $when right on target',
    ForecastVerdict.over => 'You\'ll $when about ${Money.format(f.difference, whole: true)} over$aim',
    ForecastVerdict.under => 'You\'ll $when about ${Money.format(-f.difference, whole: true)} short$aim',
  };
}

/// Where your money is headed: the expected balance each day, the low point, and what moves it.
class ForecastScreen extends StatefulWidget {
  const ForecastScreen({super.key});

  @override
  State<ForecastScreen> createState() => _ForecastScreenState();
}

class _ForecastScreenState extends State<ForecastScreen> {
  final List<WhatIf> _whatIfs = [];
  final Set<String> _skipBills = {};
  final Map<String, int> _budgetChanges = {};

  bool get _trying => _whatIfs.isNotEmpty || _skipBills.isNotEmpty || _budgetChanges.isNotEmpty;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final prefs = store.prefs;
    final now = DateTime.now();
    final f = store.forecast(now, whatIfs: _whatIfs, skipBills: _skipBills, budgetChanges: _budgetChanges);
    final plain = _trying ? store.forecast(now) : f;
    final low = f.lowPoint;
    final color = switch (f.verdict) { ForecastVerdict.under => p.danger, ForecastVerdict.over => p.positive, _ => p.accent };

    return SubPage(
      title: 'Forecast',
      subtitle: 'Where your money is headed, from your paychecks, bills, budgets and plans.',
      children: [
        if (f.accounts.isEmpty) ...[
          const HintNote('Add your checking account (and cash or payment apps) under Accounts, so the forecast starts from what you really have.'),
          const SizedBox(height: 12),
        ],
        Panel(
          key: const Key('forecast-headline'),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(verdictText(f, store), style: display(22, color, weight: FontWeight.w800)),
              const SizedBox(height: 6),
              Text(
                  'Expected ${Money.format(f.endExpected, whole: true)} on ${shortDate(f.end)}'
                  '${f.endLow != f.endHigh ? ' · likely ${Money.format(f.endLow, whole: true)} to ${Money.format(f.endHigh, whole: true)}' : ''}',
                  style: TextStyle(fontSize: 14.5, color: p.muted)),
              if (_trying) ...[
                const SizedBox(height: 6),
                Text('${Money.format(f.endExpected - plain.endExpected, signed: true, whole: true)} with your what-ifs',
                    style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w700, color: f.endExpected >= plain.endExpected ? p.positive : p.danger)),
              ],
              const SizedBox(height: 16),
              ForecastChart(forecast: f, height: 170),
              const SizedBox(height: 12),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  Tag('${f.confidence} confidence'),
                  Tag('From ${Money.format(f.startCents, whole: true)} today'),
                  if (f.nextPayday != null) Tag('Next payday ${shortDate(f.nextPayday!)}'),
                ],
              ),
            ],
          ),
        ),
        const SizedBox(height: 12),
        Row(
          children: [
            Expanded(
              child: StatTile(
                label: 'Safe to spend',
                value: Money.format(f.safeToSpend, whole: true),
                note: f.nextPayday == null ? 'beyond your budgets' : 'beyond budgets until ${shortDate(f.nextPayday!)}',
                icon: Icons.shield_outlined,
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: StatTile(
                label: 'Lowest point',
                value: Money.format(low.expected, whole: true),
                note: dayLabel(low.date),
                icon: Icons.south_rounded,
                valueColor: low.expected < 0 ? p.danger : null,
              ),
            ),
          ],
        ),
        if (low.expected < max(0, f.targetCents) && low.date.isBefore(f.end)) ...[
          const SizedBox(height: 12),
          _Warning(
            'Your balance dips to ${Money.format(low.expected, whole: true)} on ${shortDate(low.date)}'
            '${f.nextPayday != null && low.date.isBefore(f.nextPayday!) ? ', ${f.nextPayday!.difference(low.date).inDays} days before payday' : ''}.'
            ' Moving a bill or spending a little less before then keeps you above ${Money.format(max(0, f.targetCents), whole: true)}.',
          ),
        ],
        const SectionTitle('What moves the number'),
        ListPanel(
          children: [
            for (final d in f.drivers())
              RowTile(
                leading: IconBox(_icon(d.kind), size: 38, color: d.cents >= 0 ? p.positive : null),
                title: d.label,
                trailing: Money.format(d.cents, signed: true, whole: true),
                trailingColor: d.cents >= 0 ? p.positive : p.text,
              ),
            if (f.drivers().isEmpty) const RowTile(title: 'Nothing planned yet', subtitle: 'Add your income, bills and budgets to see where you\'re headed'),
          ],
        ),
        SectionTitle('What if…', action: _trying ? 'Clear' : null, onAction: () => setState(() {
              _whatIfs.clear();
              _skipBills.clear();
              _budgetChanges.clear();
            })),
        Text('Try changes here. Nothing is saved.', style: TextStyle(fontSize: 13.5, color: p.muted)),
        const SizedBox(height: 10),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final (i, w) in _whatIfs.indexed)
              InputChip(
                label: Text('${w.label} ${Money.format(w.cents, signed: true, whole: true)} · ${shortDate(w.date)}'),
                onDeleted: () => setState(() => _whatIfs.removeAt(i)),
              ),
            for (final id in _skipBills)
              InputChip(
                label: Text('Skip ${store.bills.where((b) => b.id == id).map((b) => b.name).firstOrNull ?? 'bill'}'),
                onDeleted: () => setState(() => _skipBills.remove(id)),
              ),
            for (final e in _budgetChanges.entries)
              InputChip(label: Text('${e.key} budget ${Money.format(e.value, whole: true)}'), onDeleted: () => setState(() => _budgetChanges.remove(e.key))),
          ],
        ),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            OutlinedButton.icon(onPressed: () => _addWhatIf(spend: true), icon: const Icon(Icons.remove_rounded, size: 18), label: const Text('One-time expense')),
            OutlinedButton.icon(onPressed: () => _addWhatIf(spend: false), icon: const Icon(Icons.add_rounded, size: 18), label: const Text('Extra money in')),
            if (store.bills.any((b) => !b.isDone)) OutlinedButton.icon(onPressed: _skipBill, icon: const Icon(Icons.event_busy_outlined, size: 18), label: const Text('Skip a bill')),
            if (store.budgets.isNotEmpty) OutlinedButton.icon(onPressed: _changeBudget, icon: const Icon(Icons.tune_rounded, size: 18), label: const Text('Change a budget')),
          ],
        ),
        const SectionTitle('Coming up'),
        ListPanel(
          children: [
            for (final e in f.events.where((e) => e.kind != 'budget' && e.kind != 'other').take(14))
              RowTile(
                leading: IconBox(_icon(e.kind), size: 38),
                title: e.label,
                subtitle: dayLabel(e.date),
                trailing: Money.format(e.cents, signed: true),
                trailingColor: e.cents >= 0 ? p.positive : p.text,
                trailingSub: Money.format(f.dayOf(e.date)?.expected ?? 0, whole: true),
              ),
          ],
        ),
        const SectionTitle('Settings'),
        Panel(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const FieldLabel('Look ahead'),
              Choices(
                options: [for (final h in horizonOptions) horizonLabel(h)],
                selected: horizonLabel(prefs.forecastHorizon),
                onChanged: (v) => store.updatePrefs((x) => x.forecastHorizon = horizonOptions.firstWhere((h) => horizonLabel(h) == v)),
              ),
              const SizedBox(height: 14),
              const FieldLabel('Aim to have at least'),
              Panel(
                key: const Key('forecast-target'),
                padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
                onTap: _setTarget,
                child: Row(
                  children: [
                    Expanded(
                      child: Text(prefs.forecastTargetCents == 0 ? '\$0 (just don\'t run out)' : '${Money.format(prefs.forecastTargetCents, whole: true)} cushion',
                          style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w600)),
                    ),
                    Icon(Icons.chevron_right_rounded, color: p.muted),
                  ],
                ),
              ),
              const SizedBox(height: 6),
              SwitchRow(
                title: 'Count savings accounts',
                subtitle: 'Start from checking, cash and payment apps, plus savings',
                value: prefs.forecastIncludeSavings,
                onChanged: (v) => store.updatePrefs((x) => x.forecastIncludeSavings = v),
              ),
              SwitchRow(
                title: 'Set money aside for goals',
                subtitle: 'Take each month\'s goal and sinking fund savings out of the forecast',
                value: prefs.forecastCountSavings,
                onChanged: (v) => store.updatePrefs((x) => x.forecastCountSavings = v),
              ),
            ],
          ),
        ),
        const SizedBox(height: 10),
        const HintNote('Budgets you usually go over are forecast at what you usually spend, and paychecks that vary use your recent average. '
            'The range shows the best and worst of those.'),
      ],
    );
  }

  IconData _icon(String kind) => switch (kind) {
        'income' => Icons.payments_outlined,
        'bill' => Icons.event_note_outlined,
        'budget' => Icons.pie_chart_outline_rounded,
        'debt' => Icons.credit_card_rounded,
        'savings' => Icons.savings_outlined,
        'whatif' => Icons.lightbulb_outline_rounded,
        _ => Icons.shopping_bag_outlined,
      };

  Future<void> _setTarget() async {
    final store = StoreScope.read(context);
    final cents = await askAmount(context, title: 'Aim to have at least', subtitle: 'The forecast says you\'re over or short compared with this. Use \$0 to just not run out.',
        initial: store.prefs.forecastTargetCents);
    if (cents != null) store.updatePrefs((x) => x.forecastTargetCents = cents);
  }

  Future<void> _addWhatIf({required bool spend}) async {
    final w = await showSheet<WhatIf>(context, _WhatIfSheet(spend: spend));
    if (w != null) setState(() => _whatIfs.add(w));
  }

  Future<void> _skipBill() async {
    final store = StoreScope.read(context);
    final id = await showSheet<String>(
      context,
      SheetFrame(
        title: 'Skip a bill',
        subtitle: 'See the forecast without it.',
        children: [
          const SizedBox(height: 12),
          for (final b in store.bills.where((b) => !b.isDone && !_skipBills.contains(b.id)))
            Builder(builder: (c) => RowTile(title: b.name, subtitle: '${b.repeats} · next ${shortDate(b.nextDue)}', trailing: Money.format(b.amountCents), onTap: () => Navigator.pop(c, b.id))),
        ],
      ),
    );
    if (id != null) setState(() => _skipBills.add(id));
  }

  Future<void> _changeBudget() async {
    final store = StoreScope.read(context);
    final category = await showSheet<String>(
      context,
      SheetFrame(
        title: 'Change a budget',
        children: [
          const SizedBox(height: 12),
          for (final b in store.budgets)
            Builder(
                builder: (c) => RowTile(
                    title: b.category, trailing: Money.format(_budgetChanges[b.category] ?? b.limitCents, whole: true), onTap: () => Navigator.pop(c, b.category))),
        ],
      ),
    );
    if (category == null || !mounted) return;
    final b = store.budgetFor(category)!;
    final cents = await askAmount(context, title: '$category budget', subtitle: 'Try a different amount for this month.', initial: _budgetChanges[category] ?? b.limitCents);
    if (cents != null) setState(() => _budgetChanges[category] = cents);
  }
}

class _Warning extends StatelessWidget {
  const _Warning(this.text);
  final String text;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(color: p.warn.withValues(alpha: 0.14), borderRadius: BorderRadius.circular(14), border: Border.all(color: p.warn.withValues(alpha: 0.4))),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.warning_amber_rounded, color: p.warn),
          const SizedBox(width: 10),
          Expanded(child: Text(text, style: TextStyle(fontSize: 14, color: p.text, height: 1.35))),
        ],
      ),
    );
  }
}

/// Asks for an amount. Null when cancelled.
Future<int?> askAmount(BuildContext context, {required String title, String? subtitle, int initial = 0}) =>
    showSheet<int>(context, _AmountSheet(title: title, subtitle: subtitle, initial: initial));

class _AmountSheet extends StatefulWidget {
  const _AmountSheet({required this.title, this.subtitle, required this.initial});
  final String title;
  final String? subtitle;
  final int initial;

  @override
  State<_AmountSheet> createState() => _AmountSheetState();
}

class _AmountSheetState extends State<_AmountSheet> {
  late final _c = TextEditingController(text: widget.initial == 0 ? '' : Money.editable(widget.initial));

  @override
  Widget build(BuildContext context) => SheetFrame(
        title: widget.title,
        subtitle: widget.subtitle,
        primary: 'Save',
        onPrimary: () => Navigator.pop(context, Money.parse(_c.text) ?? 0),
        children: [const SizedBox(height: 16), MoneyField(controller: _c, autofocus: true)],
      );
}

class _WhatIfSheet extends StatefulWidget {
  const _WhatIfSheet({required this.spend});
  final bool spend;

  @override
  State<_WhatIfSheet> createState() => _WhatIfSheetState();
}

class _WhatIfSheetState extends State<_WhatIfSheet> {
  final _name = TextEditingController();
  final _amount = TextEditingController();
  DateTime _date = DateTime.now();
  String? _error;

  @override
  Widget build(BuildContext context) => SheetFrame(
        title: widget.spend ? 'One-time expense' : 'Extra money in',
        subtitle: widget.spend ? 'New tires, a trip, a gift…' : 'A bonus, a tax refund, selling something…',
        error: _error,
        primary: 'Try it',
        onPrimary: () {
          final cents = Money.parse(_amount.text);
          if (cents == null || cents <= 0) return setState(() => _error = 'Enter an amount.');
          final label = _name.text.trim().isEmpty ? (widget.spend ? 'Expense' : 'Extra money') : _name.text.trim();
          Navigator.pop(context, WhatIf(label, widget.spend ? -cents : cents, _date));
        },
        children: [
          const SizedBox(height: 16),
          LabeledField(label: 'What is it?', controller: _name, hint: widget.spend ? 'New tires' : 'Bonus'),
          const SizedBox(height: 14),
          const FieldLabel('Amount'),
          MoneyField(controller: _amount),
          const SizedBox(height: 14),
          const FieldLabel('When'),
          DateTile(date: _date, first: DateTime.now().subtract(const Duration(days: 1)), onChanged: (d) => setState(() => _date = d ?? _date)),
        ],
      );
}

/// The forecast card on the dashboard: the verdict, a small chart and safe to spend.
class ForecastCard extends StatelessWidget {
  const ForecastCard({super.key, required this.onOpen});
  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    if (store.incomeSources.isEmpty && store.bills.isEmpty && store.budgets.isEmpty) return const SizedBox.shrink();
    final f = store.forecast(DateTime.now());
    final color = switch (f.verdict) { ForecastVerdict.under => p.danger, ForecastVerdict.over => p.positive, _ => p.accent };
    final low = f.lowPoint;
    return Panel(
      key: const Key('forecast-card'),
      onTap: onOpen,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.timeline_rounded, size: 18, color: p.muted),
              const SizedBox(width: 6),
              Expanded(child: Text('FORECAST', style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700, letterSpacing: 0.6, color: p.muted))),
              Icon(Icons.chevron_right_rounded, color: p.muted),
            ],
          ),
          const SizedBox(height: 6),
          Text(verdictText(f, store), style: display(18, color, weight: FontWeight.w800)),
          const SizedBox(height: 10),
          ForecastChart(forecast: f, height: 64, compact: true),
          const SizedBox(height: 10),
          Wrap(
            spacing: 14,
            runSpacing: 4,
            children: [
              Text.rich(TextSpan(children: [
                TextSpan(text: 'Safe to spend ', style: TextStyle(color: p.muted)),
                TextSpan(text: Money.format(f.safeToSpend, whole: true), style: const TextStyle(fontWeight: FontWeight.w700)),
              ]), style: const TextStyle(fontSize: 13.5)),
              if (low.expected < max(0, f.targetCents))
                Text('Dips to ${Money.format(low.expected, whole: true)} ${shortDate(low.date)}', style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w700, color: p.warn)),
            ],
          ),
        ],
      ),
    );
  }
}

/// The expected balance each day, the likely range around it, the target line and the low point.
/// Drag across it to see a day.
class ForecastChart extends StatefulWidget {
  const ForecastChart({super.key, required this.forecast, this.height = 160, this.compact = false});
  final Forecast forecast;
  final double height;
  final bool compact;

  @override
  State<ForecastChart> createState() => _ForecastChartState();
}

class _ForecastChartState extends State<ForecastChart> {
  int? _pick;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final f = widget.forecast;
    final pick = _pick == null ? null : f.days[_pick!.clamp(0, f.days.length - 1)];
    void choose(double x, double width) => setState(() => _pick = (x / width * (f.days.length - 1)).round());
    final chart = LayoutBuilder(
      builder: (context, box) => GestureDetector(
        onHorizontalDragUpdate: widget.compact ? null : (d) => choose(d.localPosition.dx, box.maxWidth),
        onTapDown: widget.compact ? null : (d) => choose(d.localPosition.dx, box.maxWidth),
        onHorizontalDragEnd: widget.compact ? null : (_) => setState(() => _pick = null),
        child: SizedBox(
          height: widget.height,
          width: double.infinity,
          child: CustomPaint(painter: _ForecastPainter(f, p, _pick)),
        ),
      ),
    );
    if (widget.compact) return Semantics(label: 'Forecast chart', child: chart);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SizedBox(
          height: 20,
          child: Text(
            pick == null
                ? 'Drag across the chart to see a day'
                : '${dayLabel(pick.date)}: ${Money.format(pick.expected, whole: true)}${pick.low != pick.high ? ' (${Money.format(pick.low, whole: true)}–${Money.format(pick.high, whole: true)})' : ''}',
            style: TextStyle(fontSize: 13, fontWeight: pick == null ? FontWeight.w500 : FontWeight.w700, color: pick == null ? p.muted : p.text),
          ),
        ),
        const SizedBox(height: 6),
        chart,
        const SizedBox(height: 6),
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text('Today', style: TextStyle(fontSize: 11.5, color: p.muted)),
            Text(shortDate(f.end), style: TextStyle(fontSize: 11.5, color: p.muted)),
          ],
        ),
      ],
    );
  }
}

class _ForecastPainter extends CustomPainter {
  _ForecastPainter(this.f, this.p, this.pick);
  final Forecast f;
  final Palette p;
  final int? pick;

  @override
  void paint(Canvas canvas, Size size) {
    final days = f.days;
    if (days.isEmpty) return;
    var lo = days.map((d) => d.low).reduce(min).toDouble();
    var hi = days.map((d) => d.high).reduce(max).toDouble();
    if (f.targetCents != 0) {
      lo = min(lo, f.targetCents.toDouble());
      hi = max(hi, f.targetCents.toDouble());
    }
    // Zoom in on the balances (zero only shows when the balance gets near it), with a little room above and below
    final pad = max((hi - lo) * 0.15, 1000.0);
    lo = lo > pad * 2 ? lo - pad : min(lo, 0) - pad * 0.3;
    hi += pad;
    final span = hi - lo == 0 ? 1.0 : hi - lo;
    double x(int i) => days.length == 1 ? size.width / 2 : i * size.width / (days.length - 1);
    double y(num v) => size.height - 4 - (v - lo) / span * (size.height - 8);

    // Zero and the target
    final zeroY = y(0).clamp(0.0, size.height);
    if (lo < 0) {
      canvas.drawLine(Offset(0, zeroY), Offset(size.width, zeroY), Paint()
        ..color = p.border
        ..strokeWidth = 1);
    }
    if (f.targetCents != 0) {
      final ty = y(f.targetCents);
      for (var dx = 0.0; dx < size.width; dx += 8) {
        canvas.drawLine(Offset(dx, ty), Offset(min(size.width, dx + 4), ty), Paint()
          ..color = p.muted
          ..strokeWidth = 1);
      }
    }

    // Likely range
    if (days.any((d) => d.low != d.high)) {
      final band = Path()..moveTo(x(0), y(days[0].high));
      for (var i = 1; i < days.length; i++) {
        band.lineTo(x(i), y(days[i].high));
      }
      for (var i = days.length - 1; i >= 0; i--) {
        band.lineTo(x(i), y(days[i].low));
      }
      band.close();
      canvas.drawPath(band, Paint()..color = p.accent.withValues(alpha: 0.13));
    }

    // Expected balance, red where it's below zero
    final path = Path()..moveTo(x(0), y(days[0].expected));
    for (var i = 1; i < days.length; i++) {
      path.lineTo(x(i), y(days[i].expected));
    }
    final stroke = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2.6
      ..strokeJoin = StrokeJoin.round
      ..strokeCap = StrokeCap.round;
    canvas.save();
    canvas.clipRect(Rect.fromLTRB(0, 0, size.width, zeroY));
    canvas.drawPath(path, stroke..color = p.accent);
    canvas.restore();
    canvas.save();
    canvas.clipRect(Rect.fromLTRB(0, zeroY, size.width, size.height));
    canvas.drawPath(path, stroke..color = p.danger);
    canvas.restore();

    // Low point
    var li = 0;
    for (var i = 1; i < days.length; i++) {
      if (days[i].expected < days[li].expected) li = i;
    }
    canvas.drawCircle(Offset(x(li), y(days[li].expected)), 4.5, Paint()..color = days[li].expected < 0 ? p.danger : p.warn);
    canvas.drawCircle(Offset(x(days.length - 1), y(days.last.expected)), 4.5, Paint()..color = p.accent);

    if (pick != null) {
      final i = pick!.clamp(0, days.length - 1);
      canvas.drawLine(Offset(x(i), 0), Offset(x(i), size.height), Paint()
        ..color = p.muted
        ..strokeWidth = 1);
      canvas.drawCircle(Offset(x(i), y(days[i].expected)), 5, Paint()..color = p.text);
    }
  }

  @override
  bool shouldRepaint(covariant _ForecastPainter old) => true;
}
