import 'dart:math';

import 'package:flutter/material.dart';

import '../dates.dart';
import '../finance.dart';
import '../kit.dart';
import '../money.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import 'bills_screen.dart';

IconData debtIcon(String type) => switch (type) {
      'Credit card' => Icons.credit_card_rounded,
      'Student loan' => Icons.school_outlined,
      'Car loan' => Icons.directions_car_outlined,
      'Mortgage' => Icons.home_outlined,
      'Medical' => Icons.local_hospital_outlined,
      'Personal loan' => Icons.handshake_outlined,
      _ => Icons.request_quote_outlined,
    };

class DebtsScreen extends StatelessWidget {
  const DebtsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final today = dayOnly(DateTime.now());
    final debts = store.debts;
    final active = debts.where((d) => !d.isPaidOff).toList();
    final paid = debts.where((d) => d.isPaidOff).toList();
    final plan = store.currentDebtPlan();
    active.sort((a, b) => (plan.order[a.id] ?? 99).compareTo(plan.order[b.id] ?? 99));
    final total = store.debtTotalCents;
    final original = debts.fold<int>(0, (s, d) => s + max(d.originalCents, d.balanceCents));
    final paidOff = debts.fold<int>(0, (s, d) => s + d.paidOffCents);
    final free = plan.debtFreeDate(today);

    return SubPage(
      title: 'Debts',
      subtitle: 'Credit cards and loans, with a plan to pay them off.',
      fab: () => showDebtSheet(context),
      fabLabel: 'Add debt',
      children: [
        if (debts.isEmpty)
          EmptyState(
            icon: Icons.credit_card_rounded,
            title: 'No debts. Nice.',
            text: 'Add a credit card balance or a loan (student, car, medical) to see when it\'s paid off and how much interest a plan saves.',
            actions: [
              SizedBox(width: double.infinity, child: FilledButton.icon(onPressed: () => showDebtSheet(context), icon: const Icon(Icons.add_rounded), label: const Text('Add a debt'))),
            ],
          )
        else ...[
          HeroCard(
            label: 'Total owed',
            value: Money.format(total),
            footer: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                ProgressBar(value: original <= 0 ? 0 : paidOff / original, color: p.mint, track: p.heroTrack, height: 9),
                const SizedBox(height: 10),
                Text(
                  active.isEmpty
                      ? 'Everything is paid off.'
                      : plan.neverPaysOff
                          ? 'At these payments some debts keep growing. Pay more than the interest.'
                          : 'Debt-free in ${monthNames[free!.month - 1]} ${free.year} · ${Money.format(paidOff, whole: true)} paid so far',
                  style: TextStyle(fontSize: 13.5, color: p.heroMuted),
                ),
              ],
            ),
          ),
          if (active.isNotEmpty) ...[
            const SectionTitle('Payoff plan'),
            _PlanCard(plan: plan),
            const SectionTitle('In plan order'),
            for (final d in active) ...[_DebtCard(d, order: plan.order[d.id], payoffMonth: plan.payoffMonth[d.id]), const SizedBox(height: 10)],
          ],
          if (paid.isNotEmpty) ...[
            const SectionTitle('Paid off'),
            ListPanel(
              children: [
                for (final d in paid)
                  RowTile(
                    leading: IconBox(Icons.check_rounded, color: p.positive),
                    title: d.name,
                    subtitle: '${d.type} · ${Money.format(d.originalCents, whole: true)} paid off',
                    onTap: () => showDebtSheet(context, existing: d),
                  ),
              ],
            ),
          ],
        ],
      ],
    );
  }
}

class _PlanCard extends StatelessWidget {
  const _PlanCard({required this.plan});
  final DebtPlan plan;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final settings = store.debtPlan;
    final minimumOnly = DebtPlan.make(store.debts, settings.strategy, 0);
    final saved = minimumOnly.neverPaysOff ? 0 : minimumOnly.totalInterest - plan.totalInterest;
    return Panel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          PillToggle(
            options: const ['Avalanche', 'Snowball'],
            selected: settings.strategy == DebtPlan.snowball ? 1 : 0,
            onChanged: (i) => store.updateDebtPlan((x) => x.strategy = i == 1 ? DebtPlan.snowball : DebtPlan.avalanche),
          ),
          const SizedBox(height: 10),
          Text(
            settings.strategy == DebtPlan.snowball
                ? 'Smallest balance first: quick wins that keep you going.'
                : 'Highest interest first: the least interest overall.',
            style: TextStyle(fontSize: 13.5, color: p.muted),
          ),
          const SizedBox(height: 14),
          Row(
            children: [
              Expanded(child: _Figure('Each month', Money.format(plan.monthlyPayment, whole: true))),
              Expanded(child: _Figure('Interest', plan.neverPaysOff ? '—' : Money.format(plan.totalInterest, whole: true))),
              Expanded(child: _Figure('Months', plan.neverPaysOff ? '∞' : '${plan.months}')),
            ],
          ),
          const SizedBox(height: 12),
          InkWell(
            borderRadius: BorderRadius.circular(12),
            onTap: () => _editExtra(context, store),
            child: Container(
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(color: p.surfaceAlt, borderRadius: BorderRadius.circular(12)),
              child: Row(
                children: [
                  Icon(Icons.add_card_rounded, color: p.accent, size: 20),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      settings.extraCents > 0
                          ? 'Extra ${Money.format(settings.extraCents, whole: true)} a month${saved > 0 ? ' saves ${Money.format(saved, whole: true)} in interest' : ''}'
                          : 'Add an extra monthly payment to see how much sooner you\'re done',
                      style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
                    ),
                  ),
                  Icon(Icons.edit_outlined, size: 18, color: p.muted),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _editExtra(BuildContext context, Store store) async {
    final c = TextEditingController(text: store.debtPlan.extraCents > 0 ? Money.editable(store.debtPlan.extraCents) : '');
    await showSheet<void>(
      context,
      Builder(builder: (context) {
        String? error;
        return StatefulBuilder(
          builder: (context, set) => SheetFrame(
            title: 'Extra each month',
            subtitle: 'On top of the minimums. It goes to the first debt in the plan, then the next.',
            error: error,
            primary: 'Save',
            onPrimary: () {
              final cents = readMoney(c, emptyIsZero: true);
              if (cents == null) return set(() => error = 'Enter an amount, or leave it empty.');
              store.updateDebtPlan((x) => x.extraCents = cents);
              Navigator.pop(context);
            },
            children: [const SizedBox(height: 12), MoneyField(controller: c, autofocus: true)],
          ),
        );
      }),
    );
    c.dispose();
  }
}

class _Figure extends StatelessWidget {
  const _Figure(this.label, this.value);
  final String label, value;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: TextStyle(fontSize: 12.5, color: p.muted)),
        const SizedBox(height: 2),
        FittedBox(fit: BoxFit.scaleDown, alignment: Alignment.centerLeft, child: Text(value, style: display(19, p.text))),
      ],
    );
  }
}

class _DebtCard extends StatelessWidget {
  const _DebtCard(this.d, {this.order, this.payoffMonth});
  final Debt d;
  final int? order;
  final int? payoffMonth;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final today = dayOnly(DateTime.now());
    final bill = store.billForDebt(d);
    final original = max(d.originalCents, d.balanceCents);
    final done = payoffMonth == null ? null : DateTime(today.year, today.month + payoffMonth!);
    return Panel(
      onTap: () => showDebtActions(context, d),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              IconBox(debtIcon(d.type)),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Flexible(child: Text(d.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700))),
                        if (order == 1) ...[const SizedBox(width: 8), const Tag('Pay first')],
                      ],
                    ),
                    Text('${d.type} · ${d.rate.toStringAsFixed(d.rate == d.rate.roundToDouble() ? 0 : 2)}% APR · min ${Money.format(d.minimumCents, whole: true)}',
                        style: TextStyle(fontSize: 13, color: p.muted)),
                  ],
                ),
              ),
              Text(Money.format(d.balanceCents, whole: true), style: display(18, p.text)),
            ],
          ),
          const SizedBox(height: 12),
          ProgressBar(value: original <= 0 ? 0 : d.paidOffCents / original, height: 7),
          const SizedBox(height: 8),
          Row(
            children: [
              Expanded(
                child: Text(
                  bill != null && !bill.isDone ? '${Money.format(bill.amountCents, whole: true)} bill · ${Bills.statusText(bill, today).toLowerCase()}' : 'No monthly bill',
                  style: TextStyle(fontSize: 12.5, color: p.muted),
                ),
              ),
              Text(done == null ? 'Not at this pace' : 'Paid off ${shortMonths[done.month - 1]} ${done.year}',
                  style: TextStyle(fontSize: 12.5, fontWeight: FontWeight.w600, color: done == null ? p.danger : p.accent)),
            ],
          ),
        ],
      ),
    );
  }
}

Future<void> showDebtActions(BuildContext context, Debt d) => showSheet<void>(context, _DebtActions(d));

class _DebtActions extends StatelessWidget {
  const _DebtActions(this.debt);
  final Debt debt;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final d = store.debtById(debt.id) ?? debt;
    final bill = store.billForDebt(d);
    final payments = [...d.payments]..sort((a, b) => b.date.compareTo(a.date));
    return SheetFrame(
      title: d.name,
      leading: IconBox(debtIcon(d.type), size: 44),
      children: [
        const SizedBox(height: 14),
        Row(
          children: [
            Expanded(child: StatTile(label: 'Owed', value: Money.format(d.balanceCents), note: '${d.rate}% APR')),
            const SizedBox(width: 10),
            Expanded(child: StatTile(label: 'Paid off', value: Money.format(d.paidOffCents, whole: true), note: 'of ${Money.format(max(d.originalCents, d.balanceCents), whole: true)}')),
          ],
        ),
        const SizedBox(height: 18),
        if (!d.isPaidOff)
          FilledButton.icon(
            onPressed: () {
              Navigator.pop(context);
              showSheet<void>(context, DebtPaymentSheet(d));
            },
            icon: const Icon(Icons.payments_outlined),
            label: const Text('Record a payment'),
          ),
        const SizedBox(height: 8),
        Row(
          children: [
            Expanded(
              child: OutlinedButton.icon(
                onPressed: () {
                  Navigator.pop(context);
                  if (bill != null) {
                    showBillSheet(context, existing: bill);
                  } else {
                    showSheet<void>(context, _DebtBillSheet(d));
                  }
                },
                icon: const Icon(Icons.event_repeat_rounded, size: 19),
                label: Text(bill != null ? 'Its bill' : 'Monthly bill'),
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: OutlinedButton.icon(
                onPressed: () {
                  Navigator.pop(context);
                  showDebtSheet(context, existing: d);
                },
                icon: const Icon(Icons.edit_outlined, size: 19),
                label: const Text('Edit'),
              ),
            ),
          ],
        ),
        if (payments.isNotEmpty) ...[
          const SectionTitle('Payments'),
          for (final pay in payments.take(8))
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 4),
              child: Row(
                children: [
                  Expanded(child: Text(shortDate(pay.date), style: TextStyle(color: p.muted))),
                  Text(Money.format(pay.cents), style: const TextStyle(fontWeight: FontWeight.w600)),
                ],
              ),
            ),
        ],
      ],
    );
  }
}

/// "Add a recurring monthly bill for the payment".
class _DebtBillSheet extends StatefulWidget {
  const _DebtBillSheet(this.debt);
  final Debt debt;

  @override
  State<_DebtBillSheet> createState() => _DebtBillSheetState();
}

class _DebtBillSheetState extends State<_DebtBillSheet> {
  late final _amount = TextEditingController(text: widget.debt.minimumCents > 0 ? Money.editable(widget.debt.minimumCents) : '');
  DateTime _due = DateTime.now().add(const Duration(days: 7));
  String _account = '';
  bool _auto = false;
  String? _error;

  @override
  void dispose() {
    _amount.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final names = store.accountNames();
    if (_account.isEmpty) _account = names.first;
    return SheetFrame(
      title: 'Monthly bill',
      subtitle: 'A repeating bill for ${widget.debt.name}. Paying it lowers the balance, and it ends when the debt is paid off.',
      error: _error,
      primary: 'Add bill',
      onPrimary: () {
        final cents = readMoney(_amount);
        if (cents == null || cents <= 0) return setState(() => _error = 'Enter the monthly payment.');
        store.createDebtBill(widget.debt, _due, _account, amountCents: cents, autoPay: _auto);
        Navigator.pop(context);
        toast(context, 'Added a monthly ${widget.debt.name} bill, due ${shortDate(_due)}.');
      },
      children: [
        const FieldLabel('Monthly payment'),
        MoneyField(controller: _amount, autofocus: true),
        const FieldLabel('Next due'),
        DateTile(date: _due, onChanged: (d) => setState(() => _due = d ?? _due)),
        const FieldLabel('Paid from'),
        Choices(options: names, selected: _account, onChanged: (v) => setState(() => _account = v)),
        const SizedBox(height: 8),
        SwitchRow(title: 'Autopay', value: _auto, onChanged: (v) => setState(() => _auto = v)),
      ],
    );
  }
}

class DebtPaymentSheet extends StatefulWidget {
  const DebtPaymentSheet(this.debt, {super.key});
  final Debt debt;

  @override
  State<DebtPaymentSheet> createState() => _DebtPaymentSheetState();
}

class _DebtPaymentSheetState extends State<DebtPaymentSheet> {
  late final _amount = TextEditingController(text: widget.debt.minimumCents > 0 ? Money.editable(min(widget.debt.minimumCents, widget.debt.balanceCents)) : '');
  DateTime _date = dayOnly(DateTime.now());
  String _account = '';
  bool _add = true;
  String? _error;

  @override
  void dispose() {
    _amount.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final names = store.accountNames();
    if (_account.isEmpty) _account = names.first;
    return SheetFrame(
      title: 'Payment on ${widget.debt.name}',
      subtitle: '${Money.format(widget.debt.balanceCents)} owed.',
      error: _error,
      primary: 'Record payment',
      onPrimary: () {
        final cents = readMoney(_amount);
        if (cents == null || cents <= 0) return setState(() => _error = 'Enter how much you paid.');
        store.recordDebtPayment(widget.debt, cents, _date, _account, addTransaction: _add);
        final after = store.debtById(widget.debt.id);
        Navigator.pop(context);
        toast(context, after != null && after.isPaidOff ? '${widget.debt.name} is paid off! 🎉' : 'Payment recorded. ${Money.format(after?.balanceCents ?? 0)} left.');
      },
      children: [
        const FieldLabel('Amount'),
        MoneyField(controller: _amount, autofocus: true),
        const FieldLabel('Paid on'),
        DateTile(date: _date, onChanged: (d) => setState(() => _date = d ?? _date)),
        const FieldLabel('Paid from'),
        Choices(options: names, selected: _account, onChanged: (v) => setState(() => _account = v)),
        const SizedBox(height: 8),
        SwitchRow(title: 'Add to Transactions', subtitle: 'Off if it\'s already there.', value: _add, onChanged: (v) => setState(() => _add = v)),
      ],
    );
  }
}

// ----- Add or edit a debt -----

Future<void> showDebtSheet(BuildContext context, {Debt? existing}) => showSheet<void>(context, DebtSheet(existing: existing));

class DebtSheet extends StatefulWidget {
  const DebtSheet({super.key, this.existing});
  final Debt? existing;

  @override
  State<DebtSheet> createState() => _DebtSheetState();
}

class _DebtSheetState extends State<DebtSheet> {
  late final Debt _d = widget.existing?.copy() ?? Debt.create(name: '');
  late final _name = TextEditingController(text: _d.name);
  late final _balance = TextEditingController(text: widget.existing == null ? '' : Money.editable(_d.balanceCents));
  late final _rate = TextEditingController(text: widget.existing == null ? '' : '${_d.rate}');
  late final _min = TextEditingController(text: widget.existing == null || _d.minimumCents == 0 ? '' : Money.editable(_d.minimumCents));
  late final _note = TextEditingController(text: _d.note);
  bool _addBill = true;
  DateTime _due = DateTime.now().add(const Duration(days: 7));
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _balance.dispose();
    _rate.dispose();
    _min.dispose();
    _note.dispose();
    super.dispose();
  }

  void _save() {
    final store = StoreScope.read(context);
    final name = _name.text.trim();
    if (name.isEmpty) return setState(() => _error = 'Enter a name, like "Visa" or "Student loan".');
    final bal = readMoney(_balance);
    if (bal == null || bal < 0) return setState(() => _error = 'Enter what you owe now.');
    final rate = double.tryParse(_rate.text.replaceAll('%', '').trim().isEmpty ? '0' : _rate.text.replaceAll('%', '').trim());
    if (rate == null || rate < 0 || rate > 100) return setState(() => _error = 'Enter the interest rate (APR) as a percent, like 22.99.');
    final minimum = readMoney(_min, emptyIsZero: true);
    if (minimum == null) return setState(() => _error = 'Enter the minimum payment as a number.');
    final isNew = widget.existing == null;
    _d
      ..name = name
      ..rate = rate
      ..minimumCents = minimum
      ..note = _note.text.trim();
    if (isNew || bal > _d.originalCents) _d.originalCents = bal;
    _d.balanceCents = bal;
    store.batch(() {
      store.put(_d);
      if (isNew && _addBill && minimum > 0) store.createDebtBill(_d, _due, store.accountNames().first);
      if (!isNew) {
        final bill = store.billForDebt(_d);
        if (bill != null && bill.name != name) store.put(bill.copy()..name = name);
      }
    });
    Navigator.pop(context);
    toast(context, isNew ? 'Added $name.' : 'Saved $name.');
  }

  Future<void> _delete() async {
    final d = widget.existing!;
    final ok = await confirm(context,
        title: 'Delete ${d.name}?', message: 'Its payments in Transactions stay. A bill that paid it becomes an ordinary bill.', confirmText: 'Delete', danger: true);
    if (!ok || !mounted) return;
    StoreScope.read(context).deleteDebt(d);
    Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) {
    final isNew = widget.existing == null;
    return SheetFrame(
      title: isNew ? 'Add debt' : 'Edit debt',
      subtitle: isNew ? 'A credit card balance or a loan: student, car, medical, personal.' : null,
      onDelete: isNew ? null : _delete,
      error: _error,
      primary: isNew ? 'Add debt' : 'Save',
      onPrimary: _save,
      children: [
        LabeledField(label: 'Name', controller: _name, hint: 'Visa, Student loan, Car loan…', caps: TextCapitalization.words, autofocus: isNew),
        const FieldLabel('Type'),
        Choices(options: DebtTypes.all, selected: _d.type, icons: debtIcon, onChanged: (v) => setState(() => _d.type = v)),
        const FieldLabel('Owed now'),
        MoneyField(controller: _balance, size: 26),
        Row(
          children: [
            Expanded(child: LabeledField(label: 'Interest (APR %)', controller: _rate, hint: '22.99', keyboard: const TextInputType.numberWithOptions(decimal: true))),
            const SizedBox(width: 12),
            Expanded(child: LabeledField(label: 'Minimum payment', controller: _min, hint: '0.00', keyboard: const TextInputType.numberWithOptions(decimal: true))),
          ],
        ),
        if (isNew) ...[
          const SizedBox(height: 12),
          SwitchRow(
            title: 'Add a monthly bill for the payment',
            subtitle: 'It repeats every month, lowers the balance when paid, and ends when the debt is paid off.',
            value: _addBill,
            onChanged: (v) => setState(() => _addBill = v),
          ),
          if (_addBill) ...[
            const FieldLabel('First payment due'),
            DateTile(date: _due, onChanged: (d) => setState(() => _due = d ?? _due)),
          ],
        ],
        LabeledField(label: 'Note (optional)', controller: _note),
      ],
    );
  }
}
