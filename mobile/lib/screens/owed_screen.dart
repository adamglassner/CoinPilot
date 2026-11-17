import 'package:flutter/material.dart';

import '../dates.dart';
import '../extras.dart';
import '../kit.dart';
import '../models.dart';
import '../money.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import 'transaction_sheet.dart';

/// Money coming back to you (refunds, reimbursements), IOUs with people, and return windows and warranties.
class OwedScreen extends StatelessWidget {
  const OwedScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final now = DateTime.now();
    final back = store.expectingBack();
    final balances = store.iouBalances();
    final returns = store.openReturns(now);
    final owedToYou = back.fold<int>(0, (s, t) => s + t.expectBackCents) + balances.values.where((v) => v > 0).fold<int>(0, (s, v) => s + v);
    final youOwe = balances.values.where((v) => v < 0).fold<int>(0, (s, v) => s - v);

    return SubPage(
      title: 'Owed to you',
      subtitle: 'Refunds and reimbursements on their way, money lent and borrowed, and return windows and warranties.',
      fab: () => showIouSheet(context),
      fabLabel: 'Add an IOU',
      children: [
        Row(children: [
          Expanded(child: StatTile(label: 'Owed to you', value: Money.format(owedToYou, whole: true), icon: Icons.south_west_rounded, valueColor: p.positive)),
          const SizedBox(width: 10),
          Expanded(child: StatTile(label: 'You owe', value: Money.format(youOwe, whole: true), icon: Icons.north_east_rounded)),
        ]),
        SectionTitle('Money coming back${back.isEmpty ? '' : ' · ${back.length}'}'),
        if (back.isEmpty)
          Text('When you add a purchase, choose "Expecting money back" for a refund or something work will pay you back for.', style: TextStyle(color: p.muted, height: 1.35))
        else
          ListPanel(children: [
            for (final t in back)
              RowTile(
                leading: const IconBox(Icons.hourglass_bottom_rounded, size: 38),
                title: '${Money.format(t.expectBackCents)}${t.expectFrom.isEmpty ? '' : ' from ${t.expectFrom}'}',
                subtitle: '${t.payee} · ${shortDate(t.date)} · ${now.difference(t.date).inDays} days',
                trailing: 'Got it',
                trailingColor: p.accent,
                onTap: () => _received(context, t),
              ),
          ]),
        SectionTitle('IOUs${balances.isEmpty ? '' : ' · ${balances.length} ${balances.length == 1 ? 'person' : 'people'}'}'),
        if (balances.isEmpty)
          Text('Lend a friend \$20, or borrow some? Add an IOU so neither of you forgets.', style: TextStyle(color: p.muted, height: 1.35))
        else
          ListPanel(children: [
            for (final e in balances.entries)
              RowTile(
                leading: CircleAvatar(radius: 19, backgroundColor: p.accentSoft, child: Text(e.key.isEmpty ? '?' : e.key[0].toUpperCase(), style: TextStyle(color: p.accent, fontWeight: FontWeight.w700))),
                title: e.key,
                subtitle: e.value > 0 ? 'Owes you' : 'You owe',
                trailing: Money.format(e.value.abs()),
                trailingColor: e.value > 0 ? p.positive : p.text,
                onTap: () => _person(context, e.key),
              ),
          ]),
        SectionTitle('Returns and warranties${returns.isEmpty ? '' : ' · ${returns.length}'}'),
        if (returns.isEmpty)
          Text('Add a "Return by" or "Warranty until" date to a purchase and it shows here, with a reminder before it runs out.', style: TextStyle(color: p.muted, height: 1.35))
        else
          ListPanel(children: [
            for (final (t, d, kind) in returns)
              RowTile(
                leading: IconBox(kind == 'Return' ? Icons.assignment_return_outlined : Icons.verified_user_outlined, size: 38),
                title: t.payee,
                subtitle: '$kind ${kind == 'Return' ? 'by' : 'until'} ${shortDate(d)} · ${Money.format(-t.cents)}',
                trailing: d.difference(dayOnly(now)).inDays == 0 ? 'Today' : '${d.difference(dayOnly(now)).inDays} days',
                trailingColor: d.difference(dayOnly(now)).inDays <= 3 ? p.warn : null,
                onTap: () => showTransactionSheet(context, existing: t),
              ),
          ]),
      ],
    );
  }

  Future<void> _received(BuildContext context, Txn t) async {
    final store = StoreScope.read(context);
    final add = await showSheet<bool>(
      context,
      Builder(
        builder: (c) => SheetFrame(
          title: 'Got ${Money.format(t.expectBackCents)} back?',
          subtitle: 'For ${t.payee} on ${shortDate(t.date)}.',
          children: [
            const SizedBox(height: 12),
            RowTile(
                leading: const IconBox(Icons.add_rounded, size: 40),
                title: 'Yes, and add it to Transactions',
                subtitle: 'As a refund to ${t.account}',
                onTap: () => Navigator.pop(c, true)),
            RowTile(
                leading: const IconBox(Icons.check_rounded, size: 40),
                title: 'Yes, it\'s already in Transactions',
                subtitle: 'From an import, or added by hand',
                onTap: () => Navigator.pop(c, false)),
          ],
        ),
      ),
    );
    if (add == null || !context.mounted) return;
    store.markBack(t, addIncome: add);
    toast(context, 'Marked as received.');
  }

  Future<void> _person(BuildContext context, String person) => showSheet<void>(context, _PersonSheet(person));
}

class _PersonSheet extends StatelessWidget {
  const _PersonSheet(this.person);
  final String person;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final list = store.ious.where((i) => i.person == person).toList()..sort((a, b) => b.date.compareTo(a.date));
    final bal = store.iouBalances()[person] ?? 0;
    return SheetFrame(
      title: person,
      subtitle: bal == 0 ? 'All square.' : bal > 0 ? 'Owes you ${Money.format(bal)}.' : 'You owe ${Money.format(-bal)}.',
      children: [
        const SizedBox(height: 10),
        for (final i in list)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 6),
            child: Row(children: [
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text('${i.theyOwe ? 'Lent' : 'Borrowed'} ${Money.format(i.cents.abs())}${i.note.isEmpty ? '' : ' · ${i.note}'}', style: const TextStyle(fontWeight: FontWeight.w600)),
                  Text('${shortDate(i.date)}${i.paidBack > 0 ? ' · ${Money.format(i.paidBack)} paid back' : ''}${i.settled ? ' · settled' : ''}', style: TextStyle(fontSize: 12.5, color: p.muted)),
                ]),
              ),
              if (!i.settled)
                TextButton(
                  onPressed: () => store.payIou(i, i.left.abs(), DateTime.now()),
                  child: const Text('Paid back'),
                ),
            ]),
          ),
        const SizedBox(height: 10),
        OutlinedButton.icon(
            onPressed: () {
              Navigator.pop(context);
              showIouSheet(context, person: person);
            },
            icon: const Icon(Icons.add_rounded),
            label: Text('Another IOU with $person')),
      ],
    );
  }
}

Future<void> showIouSheet(BuildContext context, {String? person}) => showSheet<void>(context, _IouSheet(person: person));

class _IouSheet extends StatefulWidget {
  const _IouSheet({this.person});
  final String? person;

  @override
  State<_IouSheet> createState() => _IouSheetState();
}

class _IouSheetState extends State<_IouSheet> {
  late final _person = TextEditingController(text: widget.person ?? '');
  final _amount = TextEditingController();
  final _note = TextEditingController();
  bool _lent = true;
  DateTime _date = dayOnly(DateTime.now());
  String? _error;

  @override
  Widget build(BuildContext context) => SheetFrame(
        title: 'Add an IOU',
        error: _error,
        primary: 'Add',
        onPrimary: () {
          final cents = Money.parse(_amount.text);
          if (_person.text.trim().isEmpty) return setState(() => _error = 'Who is it with?');
          if (cents == null || cents <= 0) return setState(() => _error = 'Enter an amount.');
          StoreScope.read(context).put(Iou.create(person: _person.text.trim(), cents: _lent ? cents : -cents, date: _date, note: _note.text.trim()));
          Navigator.pop(context);
          toast(context, _lent ? '${_person.text.trim()} owes you ${Money.format(cents)}.' : 'You owe ${_person.text.trim()} ${Money.format(cents)}.');
        },
        children: [
          const SizedBox(height: 12),
          PillToggle(options: const ['I lent money', 'I borrowed'], selected: _lent ? 0 : 1, onChanged: (i) => setState(() => _lent = i == 0)),
          const SizedBox(height: 6),
          LabeledField(label: 'Who', controller: _person, hint: 'A name', caps: TextCapitalization.words, autofocus: widget.person == null),
          const FieldLabel('Amount'),
          MoneyField(controller: _amount),
          const FieldLabel('Date'),
          DateTile(date: _date, onChanged: (d) => setState(() => _date = d ?? _date)),
          LabeledField(label: 'For (optional)', controller: _note, hint: 'Concert tickets'),
        ],
      );
}
