import 'package:flutter/material.dart';

import '../categories.dart';
import '../dates.dart';
import '../finance.dart';
import '../kit.dart';
import '../insights.dart';
import 'paycheck_plan.dart';
import '../money.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import 'transaction_sheet.dart';

class IncomeScreen extends StatelessWidget {
  const IncomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final today = dayOnly(DateTime.now());
    final month = store.budgetMonth(today);
    final sources = store.incomeSources.where((s) => !s.isDone).toList()..sort((a, b) => a.nextDate.compareTo(b.nextDate));
    final stillComing = [for (final s in sources) for (final d in store.paydaysBetween(s, today.add(const Duration(days: 1)), store.monthEnd(month))) (s, d)];
    final comingCents = stillComing.fold<int>(0, (x, e) => x + (e.$1.amountCents > 0 ? e.$1.amountCents : store.typicalIncome(e.$1)));
    final asking = store.askingPaydays(today);
    final received = store.inMonth(month).where((t) => t.isIncome).take(8).toList();

    return SubPage(
      title: 'Income',
      subtitle: 'Same amount every time? It\'s added on payday. Changes each time? WillowWallet asks how much.',
      fab: () => showIncomeSheet(context),
      fabLabel: 'Add income',
      children: [
        if (asking.isNotEmpty) ...[
          Panel(
            color: p.accentSoft,
            onTap: () => showPaydaySheet(context, asking),
            child: Row(
              children: [
                const IconBox(Icons.payments_outlined),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(asking.length == 1 ? 'Payday: ${asking.first.name}' : '${asking.length} paydays need an amount',
                          style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700)),
                      Text('Tap to enter what came in.', style: TextStyle(fontSize: 13, color: p.muted)),
                    ],
                  ),
                ),
                Icon(Icons.chevron_right_rounded, color: p.accent),
              ],
            ),
          ),
          const SizedBox(height: 12),
        ],
        GridView.count(
          crossAxisCount: 2,
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          mainAxisSpacing: 10,
          crossAxisSpacing: 10,
          childAspectRatio: 1.6,
          padding: EdgeInsets.zero,
          children: [
            StatTile(label: 'Came in', value: Money.format(store.incomeIn(month), whole: true), note: store.monthName(month), icon: Icons.south_west_rounded),
            StatTile(label: 'Still coming', value: Money.format(comingCents, whole: true), note: stillComing.length == 1 ? '1 payday' : '${stillComing.length} paydays', icon: Icons.schedule_rounded),
            StatTile(label: 'Per month', value: Money.format(store.expectedMonthlyIncome(), whole: true), note: 'Expected, on average', icon: Icons.calendar_month_outlined),
            StatTile(
              label: 'Next payday',
              value: sources.isEmpty ? '—' : (sameDay(sources.first.nextDate, today) ? 'Today' : shortDate(sources.first.nextDate)),
              note: sources.isEmpty ? 'Add your paycheck' : sources.first.name,
              icon: Icons.event_available_outlined,
            ),
          ],
        ),
        if (sources.isNotEmpty) ...[
          const SizedBox(height: 12),
          Panel(
            key: const Key('plan-next-paycheck'),
            onTap: () => showPaycheckPlan(context, sources.first),
            child: Row(
              children: [
                const IconBox(Icons.account_tree_outlined),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Text('Plan your next paycheck', style: TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700)),
                      Builder(builder: (context) {
                        final plan = store.planPaycheck(sources.first, sources.first.nextDate);
                        return Text(
                            plan.leftoverCents >= 0
                                ? '${Money.format(plan.leftoverCents, whole: true)} left over after bills and budgets'
                                : '${Money.format(-plan.leftoverCents, whole: true)} short of what\'s due before the next one',
                            style: TextStyle(fontSize: 13, color: plan.leftoverCents >= 0 ? p.muted : p.danger));
                      }),
                    ],
                  ),
                ),
                Icon(Icons.chevron_right_rounded, color: p.muted),
              ],
            ),
          ),
        ],
        const SectionTitle('Regular income'),
        if (sources.isEmpty)
          EmptyState(
            icon: Icons.payments_outlined,
            title: 'No regular income yet',
            text: 'Add your paycheck and anything else that comes in regularly: a side job, benefits, child support.',
            actions: [
              SizedBox(width: double.infinity, child: FilledButton.icon(onPressed: () => showIncomeSheet(context), icon: const Icon(Icons.add_rounded), label: const Text('Add income'))),
            ],
          )
        else
          ListPanel(
            children: [
              for (final s in sources)
                Builder(builder: (context) {
                  final days = dayOnly(s.nextDate).difference(today).inDays;
                  final when = days < 0 ? 'Due ${-days} day${days == -1 ? '' : 's'} ago' : days == 0 ? 'Payday today' : days == 1 ? 'Tomorrow' : 'In $days days';
                  final typical = s.amountCents > 0 ? s.amountCents : store.typicalIncome(s);
                  return RowTile(
                    leading: _DateBox(s.nextDate),
                    title: s.name,
                    subtitle: '${Paydays.describe(s)} · ${s.varies ? 'asks how much' : s.autoAdd ? 'automatic' : 'asks to confirm'}',
                    trailing: s.varies ? (typical > 0 ? 'about ${Money.format(typical)}' : 'Varies') : Money.format(s.amountCents),
                    trailingSub: when,
                    trailingSubColor: days <= 0 ? p.accent : null,
                    onTap: () => showIncomeActions(context, s),
                  );
                }),
            ],
          ),
        const SizedBox(height: 10),
        OutlinedButton.icon(
          onPressed: () => showTransactionSheet(context, income: true),
          icon: const Icon(Icons.add_rounded, size: 19),
          label: const Text('Add one-time income'),
        ),
        if (received.isNotEmpty) ...[
          SectionTitle('Came in during ${store.monthName(month)}'),
          ListPanel(
            children: [
              for (final t in received)
                RowTile(
                  leading: CategoryAvatar(t.category, income: true, size: 38),
                  title: t.payee,
                  subtitle: '${shortDate(t.date)} · ${t.category} · ${t.account}',
                  trailing: Money.format(t.cents, signed: true),
                  trailingColor: p.positive,
                  onTap: () => showTransactionSheet(context, existing: t),
                ),
            ],
          ),
        ],
      ],
    );
  }
}

class _DateBox extends StatelessWidget {
  const _DateBox(this.date);
  final DateTime date;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Container(
      width: 46,
      padding: const EdgeInsets.symmetric(vertical: 5),
      decoration: BoxDecoration(color: p.accentSoft, borderRadius: BorderRadius.circular(12)),
      child: Column(
        children: [
          Text(shortMonths[date.month - 1].toUpperCase(), style: TextStyle(fontSize: 10.5, fontWeight: FontWeight.w700, color: p.accent)),
          Text('${date.day}', style: display(18, p.accent)),
        ],
      ),
    );
  }
}

Future<void> showIncomeActions(BuildContext context, IncomeSource s) => showSheet<void>(context, _IncomeActions(s));

class _IncomeActions extends StatelessWidget {
  const _IncomeActions(this.source);
  final IncomeSource source;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final s = store.incomeSources.firstWhere((x) => x.id == source.id, orElse: () => source);
    return SheetFrame(
      title: s.name,
      leading: const IconBox(Icons.payments_outlined),
      children: [
        const SizedBox(height: 14),
        Row(
          children: [
            Expanded(child: StatTile(label: 'Next payday', value: shortDate(s.nextDate), note: Paydays.describe(s))),
            const SizedBox(width: 10),
            Expanded(child: StatTile(label: 'Per month', value: Money.format(store.monthlyIncome(s), whole: true), note: s.varies ? 'Typical' : 'Same each time')),
          ],
        ),
        if (s.hasBreakdown) ...[
          const SectionTitle('Where your paycheck goes'),
          PaycheckBreakdown(s),
        ],
        const SizedBox(height: 18),
        FilledButton.icon(
          onPressed: () {
            Navigator.pop(context);
            if (s.varies || !s.autoAdd) {
              showPaydaySheet(context, [s]);
            } else {
              store.receiveIncome(s, s.amountCents, dayOnly(DateTime.now()));
              final next = store.incomeSources.firstWhere((x) => x.id == s.id);
              toast(context, 'Added ${Money.format(s.amountCents)} from ${s.name}. Next payday ${shortDate(next.nextDate)}.');
            }
          },
          icon: const Icon(Icons.check_rounded),
          label: Text(s.varies ? 'Enter what came in' : 'It came in'),
        ),
        const SizedBox(height: 8),
        OutlinedButton.icon(
          onPressed: () {
            Navigator.pop(context);
            showPaycheckPlan(context, s);
          },
          icon: const Icon(Icons.account_tree_outlined, size: 19),
          label: const Text('Plan this paycheck'),
        ),
        const SizedBox(height: 8),
        Row(
          children: [
            Expanded(
              child: OutlinedButton.icon(
                onPressed: () {
                  store.advancePayday(s, s.nextDate);
                  Navigator.pop(context);
                  toast(context, 'Skipped this payday.');
                },
                icon: const Icon(Icons.redo_rounded, size: 19),
                label: const Text('Skip payday'),
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: OutlinedButton.icon(
                onPressed: () {
                  Navigator.pop(context);
                  showIncomeSheet(context, existing: s);
                },
                icon: const Icon(Icons.edit_outlined, size: 19),
                label: const Text('Edit'),
              ),
            ),
          ],
        ),
      ],
    );
  }
}

// ----- Add or edit income -----

Future<void> showIncomeSheet(BuildContext context, {IncomeSource? existing}) => showSheet<void>(context, IncomeSheet(existing: existing));

class IncomeSheet extends StatefulWidget {
  const IncomeSheet({super.key, this.existing});
  final IncomeSource? existing;

  @override
  State<IncomeSheet> createState() => _IncomeSheetState();
}

class _IncomeSheetState extends State<IncomeSheet> {
  late final IncomeSource _s = widget.existing?.copy() ?? IncomeSource.create(name: '', nextDate: DateTime.now().add(const Duration(days: 1)));
  late final _name = TextEditingController(text: _s.name);
  late final _amount = TextEditingController(text: _s.amountCents > 0 ? Money.editable(_s.amountCents) : '');
  late final _day2 = TextEditingController(text: '${_s.day2}');
  late final _note = TextEditingController(text: _s.note);
  late final _gross = TextEditingController(text: _s.grossCents > 0 ? Money.editable(_s.grossCents) : '');
  late final List<(TextEditingController, TextEditingController)> _deductions = [
    for (final (n, c) in _s.deductions) (TextEditingController(text: n), TextEditingController(text: Money.editable(c))),
  ];
  late bool _breakdown = _s.hasBreakdown;
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _amount.dispose();
    _day2.dispose();
    _note.dispose();
    super.dispose();
  }

  void _save() {
    final store = StoreScope.read(context);
    final name = _name.text.trim();
    if (name.isEmpty) return setState(() => _error = 'Enter a name, like "Paycheck".');
    final cents = readMoney(_amount, emptyIsZero: _s.varies);
    if (!_s.varies && (cents == null || cents <= 0)) return setState(() => _error = 'Enter the amount you get each time (after taxes).');
    if (cents == null) return setState(() => _error = 'Enter the typical amount as a number, or leave it empty.');
    var day2 = _s.day2;
    if (_s.repeats == IncomeSchedule.twiceAMonth) {
      day2 = int.tryParse(_day2.text.trim()) ?? 0;
      if (day2 < 1 || day2 > 31 || day2 == _s.nextDate.day) return setState(() => _error = 'Enter the other day of the month it\'s paid (1 to 31).');
    }
    if (_s.isDone && (_s.repeats != IncomeSchedule.once || _s.nextDate.isAfter(_s.lastReceived!))) _s.lastReceived = null;
    _s
      ..grossCents = _breakdown ? (Money.parse(_gross.text) ?? 0) : 0
      ..deductions = _breakdown
          ? [
              for (final (n, a) in _deductions)
                if (n.text.trim().isNotEmpty && (Money.parse(a.text) ?? 0) > 0) (n.text.trim(), Money.parse(a.text)!),
            ]
          : [];
    _s
      ..name = name
      ..amountCents = cents
      ..day1 = _s.nextDate.day
      ..day2 = day2
      ..note = _note.text.trim();
    if (_s.varies) _s.autoAdd = false;
    if (_s.account.isEmpty) _s.account = store.accountNames().first;
    store.put(_s);
    final added = store.addDuePaydays(dayOnly(DateTime.now()));
    Navigator.pop(context);
    toast(context, (widget.existing == null ? 'Added $name. Next payday ${shortDate(store.incomeSources.firstWhere((x) => x.id == _s.id).nextDate)}.' : 'Income saved.') +
        (added.isNotEmpty ? ' Recorded ${added.length} payday${added.length == 1 ? '' : 's'} that already came.' : ''));
  }

  Future<void> _delete() async {
    final s = widget.existing!;
    final ok = await confirm(context, title: 'Delete ${s.name}?', message: 'What it added to Transactions stays.', confirmText: 'Delete', danger: true);
    if (!ok || !mounted) return;
    final store = StoreScope.read(context);
    store.remove(s);
    Navigator.pop(context);
    toast(context, 'Deleted ${s.name}', action: 'Undo', onAction: () => store.put(s.copy()));
  }

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final isNew = widget.existing == null;
    final cats = store.categoriesFor(income: true);
    return SheetFrame(
      title: isNew ? 'Add income' : 'Edit income',
      subtitle: isNew ? 'A paycheck or other money that comes in regularly.' : null,
      onDelete: isNew ? null : _delete,
      error: _error,
      primary: isNew ? 'Add income' : 'Save',
      onPrimary: _save,
      children: [
        LabeledField(label: 'Name', controller: _name, hint: 'Paycheck · Acme Corp, DoorDash, Child support…', caps: TextCapitalization.words, autofocus: isNew),
        const FieldLabel('Amount'),
        PillToggle(
          options: const ['Same every time', 'Changes'],
          selected: _s.varies ? 1 : 0,
          onChanged: (i) => setState(() => _s.varies = i == 1),
        ),
        FieldLabel(_s.varies ? 'Typical amount (optional)' : 'Amount each time (after taxes)'),
        MoneyField(controller: _amount, size: 26),
        const FieldLabel('How often'),
        Choices(options: IncomeSchedule.all, selected: _s.repeats, onChanged: (v) => setState(() => _s.repeats = v)),
        FieldLabel(_s.repeats == IncomeSchedule.once ? 'Comes on' : 'Next payday'),
        DateTile(date: _s.nextDate, onChanged: (d) => setState(() => _s.nextDate = d ?? _s.nextDate)),
        if (_s.repeats == IncomeSchedule.twiceAMonth)
          LabeledField(label: 'Also paid on day', controller: _day2, keyboard: TextInputType.number, hint: '15'),
        const FieldLabel('Category'),
        Choices(options: cats, selected: _s.category, icons: categoryIcon, onChanged: (v) => setState(() => _s.category = v)),
        const FieldLabel('Deposited to'),
        Choices(options: store.accountNames(current: _s.account), selected: _s.account.isEmpty ? store.accountNames().first : _s.account, onChanged: (v) => setState(() => _s.account = v)),
        if (!_s.varies) ...[
          const SizedBox(height: 10),
          SwitchRow(
            title: 'Add it automatically on payday',
            subtitle: 'Off: WillowWallet asks you to confirm each payday instead.',
            value: _s.autoAdd,
            onChanged: (v) => setState(() => _s.autoAdd = v),
          ),
        ],
        if (!_breakdown)
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton.icon(
              onPressed: () => setState(() {
                _breakdown = true;
                if (_deductions.isEmpty) {
                  for (final n in ['Federal tax', 'State tax', 'Social Security', 'Medicare', '401(k)', 'Health insurance']) {
                    _deductions.add((TextEditingController(text: n), TextEditingController()));
                  }
                }
              }),
              icon: const Icon(Icons.add_rounded, size: 18),
              label: const Text('Paycheck breakdown (before taxes)'),
            ),
          )
        else ...[
          const FieldLabel('Pay before deductions (gross)'),
          MoneyField(controller: _gross, size: 22, onChanged: (_) => setState(() {})),
          const FieldLabel('Deductions'),
          for (final (i, (n, a)) in _deductions.indexed)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Row(children: [
                Expanded(flex: 3, child: TextField(controller: n, decoration: const InputDecoration(hintText: 'Deduction'))),
                const SizedBox(width: 8),
                Expanded(flex: 2, child: TextField(controller: a, onChanged: (_) => setState(() {}), keyboardType: const TextInputType.numberWithOptions(decimal: true), decoration: const InputDecoration(hintText: '0.00'))),
                IconButton(tooltip: 'Remove', onPressed: () => setState(() => _deductions.removeAt(i)), icon: const Icon(Icons.close_rounded)),
              ]),
            ),
          Row(children: [
            TextButton.icon(onPressed: () => setState(() => _deductions.add((TextEditingController(), TextEditingController()))), icon: const Icon(Icons.add_rounded, size: 18), label: const Text('Add a deduction')),
            const Spacer(),
            Builder(builder: (context) {
              final net = (Money.parse(_gross.text) ?? 0) - _deductions.fold<int>(0, (s, d) => s + (Money.parse(d.$2.text) ?? 0));
              return Text('Take-home ${Money.format(net)}', style: const TextStyle(fontWeight: FontWeight.w700));
            }),
          ]),
        ],
        LabeledField(label: 'Note (optional)', controller: _note),
      ],
    );
  }
}

// ----- Payday: how much came in? -----

Future<void> showPaydaySheet(BuildContext context, List<IncomeSource> due) => showSheet<void>(context, PaydaySheet(due));

class PaydaySheet extends StatefulWidget {
  const PaydaySheet(this.due, {super.key});
  final List<IncomeSource> due;

  @override
  State<PaydaySheet> createState() => _PaydaySheetState();
}

class _PaydaySheetState extends State<PaydaySheet> {
  late final _amounts = [for (final s in widget.due) TextEditingController(text: s.varies ? '' : Money.editable(s.amountCents))];
  late final _came = [for (final _ in widget.due) true];
  String? _error;

  @override
  void dispose() {
    for (final c in _amounts) {
      c.dispose();
    }
    super.dispose();
  }

  void _save() {
    final store = StoreScope.read(context);
    final today = dayOnly(DateTime.now());
    for (var i = 0; i < widget.due.length; i++) {
      if (!_came[i]) continue;
      final c = readMoney(_amounts[i]);
      if (c == null || c <= 0) return setState(() => _error = 'Enter how much ${widget.due[i].name} was, or untick it.');
    }
    var n = 0;
    store.batch(() {
      for (var i = 0; i < widget.due.length; i++) {
        if (!_came[i]) continue;
        final s = widget.due[i];
        store.receiveIncome(s, readMoney(_amounts[i])!, s.nextDate.isAfter(today) ? today : s.nextDate);
        n++;
      }
    });
    Navigator.pop(context);
    toast(context, n == 0 ? 'Nothing recorded.' : n == 1 ? 'Income added to Transactions.' : '$n paydays added to Transactions.');
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final store = StoreScope.of(context);
    final one = widget.due.length == 1;
    return SheetFrame(
      title: one ? 'Payday: ${widget.due.first.name}' : 'Payday',
      subtitle: one
          ? (widget.due.first.varies ? 'How much came in? It\'s added to Transactions.' : 'Did it come in? It\'s added to Transactions.')
          : 'Enter what came in. Untick any that haven\'t arrived yet.',
      error: _error,
      primary: 'Add to Transactions',
      onPrimary: _save,
      children: [
        for (var i = 0; i < widget.due.length; i++) ...[
          const SizedBox(height: 16),
          Row(
            children: [
              Checkbox(value: _came[i], onChanged: (v) => setState(() => _came[i] = v ?? false)),
              const SizedBox(width: 4),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(widget.due[i].name, style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700)),
                    Text(
                      '${dayLabel(widget.due[i].nextDate)} · into ${widget.due[i].account.isEmpty ? store.accountNames().first : widget.due[i].account}'
                      '${widget.due[i].varies && store.typicalIncome(widget.due[i]) > 0 ? ' · usually about ${Money.format(store.typicalIncome(widget.due[i]))}' : ''}',
                      style: TextStyle(fontSize: 13, color: p.muted),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          if (_came[i]) MoneyField(controller: _amounts[i], size: 24, autofocus: i == 0 && widget.due[i].varies, color: p.positive),
        ],
        const SizedBox(height: 12),
        Text('Not here yet? Untick it and WillowWallet asks again next time.', style: TextStyle(fontSize: 13, color: p.muted)),
      ],
    );
  }
}

/// Gross pay to take-home: a bar split into each deduction and what lands in your account.
class PaycheckBreakdown extends StatelessWidget {
  const PaycheckBreakdown(this.s, {super.key});
  final IncomeSource s;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final gross = s.grossCents;
    final deductions = s.deductions;
    final taken = deductions.fold<int>(0, (x, d) => x + d.$2);
    final net = gross - taken;
    final colors = [p.danger, p.warn, const Color(0xFF6C8EF5), const Color(0xFFB678E5), const Color(0xFF4FB3BF), const Color(0xFFE58FB0), p.muted];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        ClipRRect(
          borderRadius: BorderRadius.circular(8),
          child: SizedBox(
            height: 16,
            child: Row(children: [
              for (final (i, d) in deductions.indexed) Expanded(flex: (d.$2 * 1000 ~/ (gross <= 0 ? 1 : gross)).clamp(1, 1000), child: Container(color: colors[i % colors.length])),
              if (net > 0) Expanded(flex: (net * 1000 ~/ (gross <= 0 ? 1 : gross)).clamp(1, 1000), child: Container(color: p.positive)),
            ]),
          ),
        ),
        const SizedBox(height: 10),
        for (final (i, d) in deductions.indexed)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 3),
            child: Row(children: [
              Container(width: 10, height: 10, decoration: BoxDecoration(color: colors[i % colors.length], shape: BoxShape.circle)),
              const SizedBox(width: 8),
              Expanded(child: Text(d.$1)),
              Text('${Money.format(d.$2)}  ${gross <= 0 ? '' : '${(d.$2 * 100 / gross).round()}%'}', style: TextStyle(color: p.muted)),
            ]),
          ),
        const Divider(),
        Row(children: [
          Container(width: 10, height: 10, decoration: BoxDecoration(color: p.positive, shape: BoxShape.circle)),
          const SizedBox(width: 8),
          const Expanded(child: Text('Take-home', style: TextStyle(fontWeight: FontWeight.w700))),
          Text('${Money.format(net)} of ${Money.format(gross)}', style: const TextStyle(fontWeight: FontWeight.w700)),
        ]),
      ],
    );
  }
}
