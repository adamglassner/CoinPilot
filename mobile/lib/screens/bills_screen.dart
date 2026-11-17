import 'dart:math';

import 'package:flutter/material.dart';

import '../categories.dart';
import '../dates.dart';
import '../finance.dart';
import '../kit.dart';
import '../money.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

class BillsScreen extends StatefulWidget {
  const BillsScreen({super.key});

  @override
  State<BillsScreen> createState() => _BillsScreenState();
}

class _BillsScreenState extends State<BillsScreen> {
  int _view = 0; // list, calendar
  late DateTime _calMonth = DateTime(DateTime.now().year, DateTime.now().month);
  DateTime? _calDay;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final today = dayOnly(DateTime.now());
    final open = store.openBills;
    final monthly = store.monthlyBillsCents();
    final guesses = store.findSubscriptions(today);

    final overdue = open.where((b) => Bills.status(b, today) == BillStatus.overdue).toList();
    final week = open.where((b) => const [BillStatus.dueToday, BillStatus.dueSoon].contains(Bills.status(b, today))).toList();
    final later = open.where((b) => Bills.status(b, today) == BillStatus.upcoming).toList();
    final weekTotal = [...overdue, ...week].fold<int>(0, (s, b) => s + b.amountCents);

    return SubPage(
      title: 'Bills',
      subtitle: open.isEmpty ? 'Rent, utilities, insurance and subscriptions: what\'s due, and when.' : '${open.length} bills · about ${Money.format(monthly, whole: true)} a month',
      fab: () => showBillSheet(context),
      fabLabel: 'Add bill',
      children: [
        if (open.isEmpty)
          EmptyState(
            icon: Icons.event_note_outlined,
            title: 'No bills yet',
            text: 'Add the bills you pay regularly. WillowWallet shows what\'s due, and paying one adds it to Transactions.',
            actions: [
              SizedBox(width: double.infinity, child: FilledButton.icon(onPressed: () => showBillSheet(context), icon: const Icon(Icons.add_rounded), label: const Text('Add a bill'))),
            ],
          )
        else ...[
          Row(
            children: [
              Expanded(child: StatTile(label: 'Due in 7 days', value: Money.format(weekTotal, whole: true), note: '${overdue.length + week.length} bills', icon: Icons.schedule_rounded)),
              const SizedBox(width: 10),
              Expanded(
                child: StatTile(
                  label: 'Overdue',
                  value: '${overdue.length}',
                  note: overdue.isEmpty ? 'All caught up' : Money.format(overdue.fold<int>(0, (s, b) => s + b.amountCents)),
                  icon: Icons.warning_amber_rounded,
                  valueColor: overdue.isEmpty ? p.text : p.danger,
                ),
              ),
            ],
          ),
          const SizedBox(height: 14),
          PillToggle(options: const ['List', 'Calendar'], selected: _view, onChanged: (i) => setState(() => _view = i)),
          const SizedBox(height: 4),
          if (_view == 0) ...[
            if (overdue.isNotEmpty) ...[const SectionTitle('Overdue'), _BillList(overdue, today: today)],
            if (week.isNotEmpty) ...[const SectionTitle('This week'), _BillList(week, today: today)],
            if (later.isNotEmpty) ...[const SectionTitle('Later'), _BillList(later, today: today)],
          ] else
            _calendar(context, store, open, today),
        ],
        if (guesses.isNotEmpty) ...[
          const SectionTitle('Found in your transactions'),
          Text('These look like repeating charges. Add them as bills to see them coming.', style: TextStyle(fontSize: 13.5, color: p.muted, height: 1.35)),
          const SizedBox(height: 10),
          ListPanel(children: [for (final g in guesses.take(6)) _GuessRow(g)]),
        ],
      ],
    );
  }

  Widget _calendar(BuildContext context, Store store, List<Bill> open, DateTime today) {
    final p = context.pal;
    final first = _calMonth;
    final days = daysInMonth(first);
    final lead = first.weekday - 1; // Monday first
    // Every due date in this month, repeating bills included
    final due = <int, List<Bill>>{};
    for (final b in open) {
      var d = dayOnly(b.nextDue);
      for (var i = 0; i < 60 && !d.isAfter(DateTime(first.year, first.month, days)); i++) {
        if (sameMonth(d, first)) (due[d.day] ??= []).add(b);
        if (b.repeats == BillSchedule.once) break;
        d = Bills.next(d, b.repeats, b.dueDay);
      }
    }
    final selected = _calDay != null && sameMonth(_calDay!, first) ? _calDay!.day : null;
    final dayBills = selected == null ? <Bill>[] : (due[selected] ?? []);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SizedBox(height: 10),
        Panel(
          padding: const EdgeInsets.fromLTRB(10, 8, 10, 12),
          child: Column(
            children: [
              Row(
                children: [
                  IconButton(
                      tooltip: 'Previous month',
                      onPressed: () => setState(() => _calMonth = DateTime(first.year, first.month - 1)),
                      icon: const Icon(Icons.chevron_left_rounded)),
                  Expanded(child: Text(monthLabel(first), textAlign: TextAlign.center, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700))),
                  IconButton(
                      tooltip: 'Next month',
                      onPressed: () => setState(() => _calMonth = DateTime(first.year, first.month + 1)),
                      icon: const Icon(Icons.chevron_right_rounded)),
                ],
              ),
              Row(
                children: [
                  for (final d in const ['M', 'T', 'W', 'T', 'F', 'S', 'S'])
                    Expanded(child: Text(d, textAlign: TextAlign.center, style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700, color: p.muted))),
                ],
              ),
              const SizedBox(height: 6),
              for (var week = 0; week * 7 < lead + days; week++)
                Row(
                  children: [
                    for (var c = 0; c < 7; c++)
                      Expanded(
                        child: Builder(builder: (context) {
                          final day = week * 7 + c - lead + 1;
                          if (day < 1 || day > days) return const SizedBox(height: 46);
                          final date = DateTime(first.year, first.month, day);
                          final bills = due[day] ?? const <Bill>[];
                          final isToday = sameDay(date, today);
                          final isSel = selected == day;
                          final late = bills.isNotEmpty && date.isBefore(today);
                          return GestureDetector(
                            onTap: () => setState(() => _calDay = date),
                            behavior: HitTestBehavior.opaque,
                            child: Container(
                              height: 46,
                              margin: const EdgeInsets.all(2),
                              decoration: BoxDecoration(
                                color: isSel ? p.accent : (isToday ? p.accentSoft : null),
                                borderRadius: BorderRadius.circular(12),
                              ),
                              child: Column(
                                mainAxisAlignment: MainAxisAlignment.center,
                                children: [
                                  Text('$day',
                                      style: TextStyle(
                                          fontSize: 14.5,
                                          fontWeight: isToday || isSel ? FontWeight.w800 : FontWeight.w500,
                                          color: isSel ? p.onAccent : p.text)),
                                  const SizedBox(height: 3),
                                  Row(
                                    mainAxisAlignment: MainAxisAlignment.center,
                                    children: [
                                      for (var i = 0; i < min(3, bills.length); i++)
                                        Container(
                                          width: 5,
                                          height: 5,
                                          margin: const EdgeInsets.symmetric(horizontal: 1),
                                          decoration: BoxDecoration(
                                              color: isSel ? p.onAccent : (late ? p.danger : p.accent), shape: BoxShape.circle),
                                        ),
                                    ],
                                  ),
                                ],
                              ),
                            ),
                          );
                        }),
                      ),
                  ],
                ),
            ],
          ),
        ),
        if (selected != null) ...[
          SectionTitle(dayLabel(DateTime(first.year, first.month, selected))),
          dayBills.isEmpty
              ? Text('Nothing due this day.', style: TextStyle(color: p.muted))
              : _BillList(dayBills, today: today),
        ] else ...[
          const SizedBox(height: 12),
          Text('Tap a day to see what\'s due.', textAlign: TextAlign.center, style: TextStyle(color: p.muted)),
        ],
      ],
    );
  }
}

class _BillList extends StatelessWidget {
  const _BillList(this.bills, {required this.today});
  final List<Bill> bills;
  final DateTime today;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return ListPanel(
      children: [
        for (final b in bills)
          Builder(builder: (context) {
            final status = Bills.status(b, today);
            final Color? color = switch (status) {
              BillStatus.overdue => p.danger,
              BillStatus.dueToday || BillStatus.dueSoon => p.warn,
              _ => null,
            };
            final parts = [shortDate(b.nextDue), b.repeats, if (b.autoPay) 'Autopay', if (b.paysDebt) 'Pays a debt'];
            return RowTile(
              leading: CategoryAvatar(b.category),
              title: b.name,
              subtitle: parts.join(' · '),
              trailing: Money.format(b.amountCents),
              trailingSub: Bills.statusText(b, today),
              trailingSubColor: color,
              onTap: () => showBillActions(context, b),
            );
          }),
      ],
    );
  }
}

class _GuessRow extends StatelessWidget {
  const _GuessRow(this.g);
  final SubscriptionGuess g;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final store = StoreScope.read(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              CategoryAvatar(g.category, size: 38),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(Subscriptions.pretty(g.payee), style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
                    Text('${Money.format(g.cents)} ${g.repeats.toLowerCase()} · ${g.charges} charges · next ${shortDate(g.nextDue)}',
                        style: TextStyle(fontSize: 13, color: p.muted)),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              TextButton(
                onPressed: () {
                  store.dismissSubscription(g.payee);
                  toast(context, 'Got it. ${Subscriptions.pretty(g.payee)} won\'t be suggested again.');
                },
                child: Text('Not a bill', style: TextStyle(color: p.muted)),
              ),
              const SizedBox(width: 4),
              FilledButton.tonal(
                style: FilledButton.styleFrom(minimumSize: const Size(0, 40), backgroundColor: p.accentSoft, foregroundColor: p.accent),
                onPressed: () {
                  store.put(Bill.create(
                    name: Subscriptions.pretty(g.payee),
                    amountCents: g.cents,
                    nextDue: g.nextDue,
                    repeats: g.repeats,
                    category: g.category.isEmpty ? 'Subscriptions' : g.category,
                    account: g.account,
                  ));
                  toast(context, 'Added ${Subscriptions.pretty(g.payee)} to your bills.');
                },
                child: const Text('Add as bill'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

// ----- What to do with a bill -----

Future<void> showBillActions(BuildContext context, Bill bill) => showSheet<void>(context, _BillActions(bill));

class _BillActions extends StatelessWidget {
  const _BillActions(this.bill);
  final Bill bill;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final b = store.bills.firstWhere((x) => x.id == bill.id, orElse: () => bill);
    final today = dayOnly(DateTime.now());
    final debt = b.paysDebt ? store.debtById(b.debtId) : null;
    return SheetFrame(
      title: b.name,
      leading: CategoryAvatar(b.category, size: 44),
      children: [
        const SizedBox(height: 14),
        Row(
          children: [
            Expanded(child: StatTile(label: b.isDone ? 'Paid' : 'Next due', value: shortDate(b.isDone ? (b.lastPaid ?? b.nextDue) : b.nextDue), note: Bills.statusText(b, today))),
            const SizedBox(width: 10),
            Expanded(child: StatTile(label: 'Amount', value: Money.format(b.amountCents), note: b.repeats)),
          ],
        ),
        const SizedBox(height: 12),
        HintNote(
          [
            b.account.isEmpty ? 'Paid from your first account' : 'Paid from ${b.account}',
            b.category,
            if (b.autoPay) 'pays itself automatically',
            if (debt != null) 'pays down ${debt.name} (${Money.format(debt.balanceCents)} left)',
            if (b.lastPaid != null && !b.isDone) 'last paid ${shortDate(b.lastPaid!)}',
          ].join(' · '),
          icon: Icons.info_outline_rounded,
        ),
        if (!b.isDone) ...[
          const SizedBox(height: 18),
          FilledButton.icon(
            onPressed: () {
              Navigator.pop(context);
              showSheet<void>(context, PayBillSheet(b));
            },
            icon: const Icon(Icons.check_rounded),
            label: const Text('Mark paid'),
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              Expanded(
                child: OutlinedButton.icon(
                  onPressed: () {
                    store.skipBill(b);
                    Navigator.pop(context);
                    toast(context, b.repeats == BillSchedule.once ? '${b.name} skipped.' : 'Skipped. Next due ${shortDate(Bills.next(b.nextDue, b.repeats, b.dueDay))}.');
                  },
                  icon: const Icon(Icons.redo_rounded, size: 19),
                  label: const Text('Skip this one'),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: OutlinedButton.icon(
                  onPressed: () {
                    Navigator.pop(context);
                    showBillSheet(context, existing: b);
                  },
                  icon: const Icon(Icons.edit_outlined, size: 19),
                  label: const Text('Edit'),
                ),
              ),
            ],
          ),
        ] else ...[
          const SizedBox(height: 18),
          OutlinedButton.icon(
            onPressed: () {
              Navigator.pop(context);
              showBillSheet(context, existing: b);
            },
            icon: const Icon(Icons.edit_outlined, size: 19),
            label: const Text('Edit'),
          ),
        ],
      ],
    );
  }
}

/// Mark a bill paid: the amount, the day, and whether to add it to Transactions.
class PayBillSheet extends StatefulWidget {
  const PayBillSheet(this.bill, {super.key});
  final Bill bill;

  @override
  State<PayBillSheet> createState() => _PayBillSheetState();
}

class _PayBillSheetState extends State<PayBillSheet> {
  late final _amount = TextEditingController(text: Money.editable(widget.bill.amountCents));
  DateTime _date = dayOnly(DateTime.now());
  bool _add = true;
  String? _error;

  @override
  void dispose() {
    _amount.dispose();
    super.dispose();
  }

  void _save() {
    final cents = readMoney(_amount);
    if (cents == null || cents <= 0) {
      setState(() => _error = 'Enter how much you paid.');
      return;
    }
    final store = StoreScope.read(context);
    final b = widget.bill.copy()..amountCents = cents;
    // A different amount this time doesn't change the bill's usual amount.
    final usual = widget.bill.amountCents;
    store.batch(() {
      store.markPaid(b, _date, addTransaction: _add);
      final after = store.bills.where((x) => x.id == b.id).toList();
      if (after.isNotEmpty && after.first.amountCents != usual) store.put(after.first.copy()..amountCents = usual);
    });
    Navigator.pop(context);
    final after = store.bills.where((x) => x.id == b.id).toList();
    toast(context, after.isEmpty || after.first.isDone ? '${b.name} paid.' : '${b.name} paid. Next due ${shortDate(after.first.nextDue)}.');
  }

  @override
  Widget build(BuildContext context) {
    final b = widget.bill;
    return SheetFrame(
      title: 'Pay ${b.name}',
      subtitle: 'Due ${shortDate(b.nextDue)}${b.paysDebt ? '. Lowers the debt it pays.' : ''}',
      error: _error,
      primary: 'Mark paid',
      onPrimary: _save,
      children: [
        const FieldLabel('Amount paid'),
        MoneyField(controller: _amount),
        const FieldLabel('Paid on'),
        DateTile(date: _date, onChanged: (d) => setState(() => _date = d ?? _date)),
        const SizedBox(height: 10),
        SwitchRow(
          title: 'Add to Transactions',
          subtitle: 'Off if the payment is already there (imported from your bank).',
          value: _add,
          onChanged: (v) => setState(() => _add = v),
        ),
      ],
    );
  }
}

// ----- Add or edit a bill -----

Future<void> showBillSheet(BuildContext context, {Bill? existing, String? debtId}) =>
    showSheet<void>(context, BillSheet(existing: existing, debtId: debtId));

class BillSheet extends StatefulWidget {
  const BillSheet({super.key, this.existing, this.debtId});
  final Bill? existing;
  final String? debtId;

  @override
  State<BillSheet> createState() => _BillSheetState();
}

class _BillSheetState extends State<BillSheet> {
  late final Bill _b = widget.existing?.copy() ?? Bill.create(name: '', nextDue: DateTime.now().add(const Duration(days: 1)), debtId: widget.debtId ?? '');
  late final _name = TextEditingController(text: _b.name);
  late final _amount = TextEditingController(text: _b.amountCents == 0 ? '' : Money.editable(_b.amountCents));
  late final _note = TextEditingController(text: _b.note);
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _amount.dispose();
    _note.dispose();
    super.dispose();
  }

  void _save() {
    final store = StoreScope.read(context);
    final name = _name.text.trim();
    final cents = readMoney(_amount);
    if (name.isEmpty) return setState(() => _error = 'Enter a name, like "Rent" or "Car insurance".');
    if (cents == null || cents <= 0) return setState(() => _error = 'Enter the amount.');
    _b
      ..name = name
      ..amountCents = cents
      ..note = _note.text.trim()
      ..dueDay = _b.nextDue.day;
    if (_b.account.isEmpty) _b.account = store.accountNames().first;
    if (_b.paysDebt) _b.category = 'Debt payments';
    if (_b.isDone && (_b.repeats != BillSchedule.once || _b.nextDue.isAfter(_b.lastPaid!))) _b.lastPaid = null;
    store.put(_b);
    Navigator.pop(context);
    toast(context, widget.existing == null ? 'Added $name. Due ${shortDate(_b.nextDue)}.' : 'Saved $name.');
  }

  Future<void> _delete() async {
    final b = widget.existing!;
    final ok = await confirm(context,
        title: 'Delete ${b.name}?', message: 'Payments already in Transactions stay.', confirmText: 'Delete', danger: true);
    if (!ok || !mounted) return;
    final store = StoreScope.read(context);
    store.remove(b);
    Navigator.pop(context);
    toast(context, 'Deleted ${b.name}', action: 'Undo', onAction: () => store.put(b.copy()));
  }

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final debts = store.debts.where((d) => !d.isPaidOff || d.id == _b.debtId).toList();
    return SheetFrame(
      title: widget.existing == null ? 'Add bill' : 'Edit bill',
      subtitle: widget.existing == null ? 'Something you pay on a schedule: rent, a phone plan, insurance, a subscription.' : null,
      onDelete: widget.existing == null ? null : _delete,
      error: _error,
      primary: widget.existing == null ? 'Add bill' : 'Save',
      onPrimary: _save,
      children: [
        LabeledField(label: 'Name', controller: _name, hint: 'Rent, Netflix, Car insurance…', caps: TextCapitalization.words, autofocus: widget.existing == null),
        const FieldLabel('Amount'),
        MoneyField(controller: _amount, size: 26),
        const FieldLabel('How often'),
        Choices(options: BillSchedule.all, selected: _b.repeats, onChanged: (v) => setState(() => _b.repeats = v)),
        FieldLabel(_b.repeats == BillSchedule.once ? 'Due on' : 'Next due'),
        DateTile(date: _b.nextDue, onChanged: (d) => setState(() => _b.nextDue = d ?? _b.nextDue)),
        if (debts.isNotEmpty) ...[
          const FieldLabel('Pays down a debt?'),
          Choices(
            options: ['No, an ordinary bill', for (final d in debts) d.name],
            selected: _b.paysDebt ? (store.debtById(_b.debtId)?.name ?? 'No, an ordinary bill') : 'No, an ordinary bill',
            onChanged: (v) => setState(() {
              final d = debts.where((d) => d.name == v).toList();
              _b.debtId = d.isEmpty ? '' : d.first.id;
              if (d.isNotEmpty) _b.category = 'Debt payments';
            }),
          ),
          const SizedBox(height: 8),
          HintNote('Loans and cards (student loan, car loan, credit card) are debts. Paying a linked bill lowers the debt and the bill ends when it\'s paid off. Insurance and subscriptions are ordinary bills.'),
        ],
        if (!_b.paysDebt) ...[
          const FieldLabel('Category'),
          Choices(options: store.categoriesFor(income: false), selected: _b.category, icons: categoryIcon, onChanged: (v) => setState(() => _b.category = v)),
        ],
        const FieldLabel('Paid from'),
        Choices(options: store.accountNames(current: _b.account), selected: _b.account.isEmpty ? store.accountNames().first : _b.account, onChanged: (v) => setState(() => _b.account = v)),
        const SizedBox(height: 10),
        SwitchRow(
          title: 'Autopay',
          subtitle: 'It pays itself. You still mark it paid (or import it) so it\'s in Transactions.',
          value: _b.autoPay,
          onChanged: (v) => setState(() => _b.autoPay = v),
        ),
        LabeledField(label: 'Note (optional)', controller: _note),
      ],
    );
  }
}
