import 'package:flutter/material.dart';

import '../dates.dart';
import '../insights.dart';
import '../kit.dart';
import '../money.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

/// Shared household: who paid what this month, each person's share, and who owes whom.
class HouseholdScreen extends StatefulWidget {
  const HouseholdScreen({super.key});

  @override
  State<HouseholdScreen> createState() => _HouseholdScreenState();
}

class _HouseholdScreenState extends State<HouseholdScreen> {
  DateTime _month = DateTime.now();

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final prefs = store.prefs;
    if (!prefs.household) {
      return SubPage(
        title: 'Household',
        subtitle: 'Share spending with a partner or roommate: each transaction says who paid, and WillowWallet works out who owes whom.',
        children: [
          EmptyState(
            icon: Icons.people_outline_rounded,
            title: 'Set up your household',
            text: 'Add the people who share expenses. You both sign in to the same WillowWallet account on your own devices, so everything stays in sync.',
            actions: [
              SizedBox(width: double.infinity, child: FilledButton.icon(onPressed: () => showHouseholdSheet(context), icon: const Icon(Icons.group_add_outlined), label: const Text('Set up'))),
            ],
          ),
        ],
      );
    }
    final h = store.householdMonth(_month);
    return SubPage(
      title: 'Household',
      subtitle: '${prefs.householdPeople.join(' and ')} · ${prefs.householdPeople.length == 2 ? '${prefs.householdShare}/${100 - prefs.householdShare} split' : 'even split'}',
      actions: [IconButton(tooltip: 'Edit household', onPressed: () => showHouseholdSheet(context), icon: const Icon(Icons.edit_outlined))],
      children: [
        MonthSwitcher(month: _month, onChanged: (m) => setState(() => _month = m)),
        const SizedBox(height: 12),
        Panel(
          key: const Key('household-settle'),
          color: p.accentSoft,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Shared spending ${Money.format(h.total, whole: true)}', style: TextStyle(fontSize: 13.5, color: p.muted)),
              const SizedBox(height: 4),
              if (h.settle.isEmpty)
                Text(h.total == 0 ? 'Nothing shared yet this month' : 'All even', style: display(22, p.text, weight: FontWeight.w800))
              else
                for (final (from, to, cents) in h.settle) Text('$from owes $to ${Money.format(cents)}', style: display(22, p.text, weight: FontWeight.w800)),
            ],
          ),
        ),
        const SizedBox(height: 12),
        Row(
          children: [
            for (final (i, person) in h.people.indexed) ...[
              if (i > 0) const SizedBox(width: 10),
              Expanded(child: StatTile(label: '$person paid', value: Money.format(h.paid[person]!, whole: true), note: 'share ${Money.format(h.fair[person]!, whole: true)}')),
            ],
          ],
        ),
        const SectionTitle('Shared this month'),
        if (h.transactions.isEmpty)
          Text('Add spending and pick who paid. Transactions marked "Not shared" stay out of this.', style: TextStyle(color: p.muted))
        else
          ListPanel(children: [
            for (final t in h.transactions.take(40))
              RowTile(
                leading: CategoryAvatar(t.category, size: 36),
                title: t.payee,
                subtitle: '${shortDate(t.date)} · paid by ${t.paidBy}',
                trailing: Money.format(-t.cents),
              ),
          ]),
      ],
    );
  }
}

Future<void> showHouseholdSheet(BuildContext context) => showSheet<void>(context, const _HouseholdSheet());

class _HouseholdSheet extends StatefulWidget {
  const _HouseholdSheet();

  @override
  State<_HouseholdSheet> createState() => _HouseholdSheetState();
}

class _HouseholdSheetState extends State<_HouseholdSheet> {
  late final _store = StoreScope.read(context);
  late final List<TextEditingController> _names = [
    for (final n in [..._store.prefs.householdPeople, if (_store.prefs.householdPeople.length < 2) ...['', ''].take(2 - _store.prefs.householdPeople.length)]) TextEditingController(text: n),
  ];
  late int _share = _store.prefs.householdShare;
  String? _error;

  @override
  Widget build(BuildContext context) {
    final names = [for (final c in _names) c.text.trim()];
    return SheetFrame(
      title: 'Household',
      subtitle: 'The people who share expenses, and how shared spending is split.',
      error: _error,
      primary: 'Save',
      onPrimary: () {
        final people = [for (final n in names) if (n.isNotEmpty) n];
        if (people.toSet().length != people.length) return setState(() => _error = 'Use a different name for each person.');
        if (people.length == 1) return setState(() => _error = 'Add at least two people, or clear both to turn it off.');
        _store.updatePrefs((p) => p
          ..householdPeople = people
          ..householdShare = _share);
        Navigator.pop(context);
      },
      children: [
        for (final (i, c) in _names.indexed) ...[
          const SizedBox(height: 10),
          LabeledField(label: 'Person ${i + 1}', controller: c, hint: i == 0 ? 'You' : 'Partner or roommate', caps: TextCapitalization.words, onChanged: (_) => setState(() {})),
        ],
        Align(
          alignment: Alignment.centerLeft,
          child: TextButton.icon(onPressed: () => setState(() => _names.add(TextEditingController())), icon: const Icon(Icons.person_add_alt_rounded, size: 18), label: const Text('Add a person')),
        ),
        if (names.where((n) => n.isNotEmpty).length == 2) ...[
          const FieldLabel('Split shared spending'),
          Choices(
            options: const ['50/50', '60/40', '70/30', '40/60', '30/70'],
            selected: '$_share/${100 - _share}',
            onChanged: (v) => setState(() => _share = int.parse(v.split('/').first)),
          ),
          const SizedBox(height: 6),
          Text('${names[0].isEmpty ? 'Person 1' : names[0]} pays $_share%, ${names[1].isEmpty ? 'person 2' : names[1]} pays ${100 - _share}%.', style: TextStyle(fontSize: 13, color: context.pal.muted)),
        ] else if (names.where((n) => n.isNotEmpty).length > 2)
          Text('With three or more people, shared spending is split evenly.', style: TextStyle(fontSize: 13, color: context.pal.muted)),
      ],
    );
  }
}
