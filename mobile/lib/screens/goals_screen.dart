import 'package:flutter/material.dart';

import '../dates.dart';
import '../finance.dart';
import '../kit.dart';
import '../money.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

class GoalsScreen extends StatelessWidget {
  const GoalsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final goals = store.goals..sort((a, b) => (store.isReached(a) ? 1 : 0).compareTo(store.isReached(b) ? 1 : 0));
    final saved = goals.fold<int>(0, (s, g) => s + store.savedFor(g));
    final target = goals.fold<int>(0, (s, g) => s + g.targetCents);
    return SubPage(
      title: 'Goals',
      subtitle: 'Something to save toward: an emergency fund, a trip, a down payment.',
      fab: () => showGoalSheet(context),
      fabLabel: 'New goal',
      children: [
        if (goals.isEmpty)
          EmptyState(
            icon: Icons.flag_outlined,
            title: 'No goals yet',
            text: 'Set a target (and a date if you like). WillowWallet shows how much to put away each month.',
            actions: [
              SizedBox(width: double.infinity, child: FilledButton.icon(onPressed: () => showGoalSheet(context), icon: const Icon(Icons.add_rounded), label: const Text('New goal'))),
            ],
          )
        else ...[
          HeroCard(
            label: 'Saved toward your goals',
            value: Money.format(saved),
            footer: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                ProgressBar(value: target <= 0 ? 0 : saved / target, color: p.mint, track: p.heroTrack, height: 9),
                const SizedBox(height: 10),
                Text('of ${Money.format(target, whole: true)} across ${goals.length} goal${goals.length == 1 ? '' : 's'}', style: TextStyle(fontSize: 13.5, color: p.heroMuted)),
              ],
            ),
          ),
          const SizedBox(height: 12),
          for (final g in goals) ...[_GoalCard(g), const SizedBox(height: 10)],
        ],
      ],
    );
  }
}

class _GoalCard extends StatelessWidget {
  const _GoalCard(this.g);
  final Goal g;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final today = dayOnly(DateTime.now());
    final saved = store.savedFor(g);
    final reached = store.isReached(g);
    final needed = store.monthlyNeeded(g, today);
    final pct = (store.fraction(g) * 100).round();
    final lines = [
      if (reached) 'Reached! 🎉' else '${Money.format(store.remaining(g), whole: true)} to go',
      if (g.targetDate != null) 'by ${shortDate(g.targetDate!)}',
      if (needed != null) '${Money.format(needed, whole: true)}/month',
      if (g.isLinked) 'from ${g.linkedAccount}',
    ];
    return Panel(
      onTap: () => showGoalActions(context, g),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              IconBox(reached ? Icons.emoji_events_outlined : Icons.flag_outlined, color: reached ? p.positive : null),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(g.name, style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700)),
                    Text(lines.join(' · '), style: TextStyle(fontSize: 13, color: reached ? p.positive : p.muted)),
                  ],
                ),
              ),
              Column(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Text(Money.format(saved, whole: true), style: display(18, p.text)),
                  Text('of ${Money.format(g.targetCents, whole: true)}', style: TextStyle(fontSize: 12.5, color: p.muted)),
                ],
              ),
            ],
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(child: ProgressBar(value: store.fraction(g), color: reached ? p.positive : p.accent)),
              const SizedBox(width: 10),
              Text('$pct%', style: TextStyle(fontSize: 13, fontWeight: FontWeight.w700, color: p.muted)),
            ],
          ),
        ],
      ),
    );
  }
}

Future<void> showGoalActions(BuildContext context, Goal g) => showSheet<void>(context, _GoalActions(g));

class _GoalActions extends StatelessWidget {
  const _GoalActions(this.goal);
  final Goal goal;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final g = store.goals.firstWhere((x) => x.id == goal.id, orElse: () => goal);
    final history = [...g.history]..sort((a, b) => b.date.compareTo(a.date));
    return SheetFrame(
      title: g.name,
      leading: const IconBox(Icons.flag_outlined, size: 44),
      children: [
        const SizedBox(height: 14),
        Row(
          children: [
            Expanded(child: StatTile(label: 'Saved', value: Money.format(store.savedFor(g)), note: '${(store.fraction(g) * 100).round()}% of ${Money.format(g.targetCents, whole: true)}')),
            const SizedBox(width: 10),
            Expanded(
              child: StatTile(
                label: 'Each month',
                value: store.monthlyNeeded(g, DateTime.now()) == null ? '—' : Money.format(store.monthlyNeeded(g, DateTime.now())!, whole: true),
                note: g.targetDate == null ? 'No target date' : 'to reach it by ${shortDate(g.targetDate!)}',
              ),
            ),
          ],
        ),
        const SizedBox(height: 18),
        if (g.isLinked)
          HintNote('Linked to ${g.linkedAccount}: what\'s saved is that account\'s balance. Move money with a transfer.', icon: Icons.link_rounded)
        else
          Row(
            children: [
              Expanded(
                child: FilledButton.icon(
                  onPressed: () {
                    Navigator.pop(context);
                    showSheet<void>(context, GoalMoneySheet(g, add: true));
                  },
                  icon: const Icon(Icons.add_rounded),
                  label: const Text('Add money'),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: OutlinedButton.icon(
                  style: OutlinedButton.styleFrom(minimumSize: const Size(0, 54)),
                  onPressed: () {
                    Navigator.pop(context);
                    showSheet<void>(context, GoalMoneySheet(g, add: false));
                  },
                  icon: const Icon(Icons.remove_rounded),
                  label: const Text('Take out'),
                ),
              ),
            ],
          ),
        const SizedBox(height: 8),
        OutlinedButton.icon(
          onPressed: () {
            Navigator.pop(context);
            showGoalSheet(context, existing: g);
          },
          icon: const Icon(Icons.edit_outlined, size: 19),
          label: const Text('Edit goal'),
        ),
        if (history.isNotEmpty) ...[
          const SectionTitle('History'),
          for (final h in history.take(8))
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 4),
              child: Row(
                children: [
                  Expanded(child: Text(shortDate(h.date), style: TextStyle(color: p.muted))),
                  Text(Money.format(h.cents, signed: true), style: TextStyle(fontWeight: FontWeight.w600, color: h.cents >= 0 ? p.positive : p.text)),
                ],
              ),
            ),
        ],
      ],
    );
  }
}

class GoalMoneySheet extends StatefulWidget {
  const GoalMoneySheet(this.goal, {super.key, required this.add});
  final Goal goal;
  final bool add;

  @override
  State<GoalMoneySheet> createState() => _GoalMoneySheetState();
}

class _GoalMoneySheetState extends State<GoalMoneySheet> {
  final _amount = TextEditingController();
  DateTime _date = dayOnly(DateTime.now());
  String? _error;

  @override
  void dispose() {
    _amount.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    return SheetFrame(
      title: widget.add ? 'Add to ${widget.goal.name}' : 'Take out of ${widget.goal.name}',
      subtitle: '${Money.format(store.savedFor(widget.goal))} saved so far.',
      error: _error,
      primary: widget.add ? 'Add money' : 'Take out',
      onPrimary: () {
        final cents = readMoney(_amount);
        if (cents == null || cents <= 0) return setState(() => _error = 'Enter an amount.');
        final milestone = store.addToGoal(widget.goal, widget.add ? cents : -cents, _date);
        Navigator.pop(context);
        toast(
            context,
            milestone == 100
                ? 'You reached ${widget.goal.name}! 🎉'
                : milestone != null
                    ? '$milestone% of the way to ${widget.goal.name}!'
                    : widget.add
                        ? 'Added ${Money.format(cents)}.'
                        : 'Took out ${Money.format(cents)}.');
      },
      children: [
        const FieldLabel('Amount'),
        MoneyField(controller: _amount, autofocus: true),
        const FieldLabel('Date'),
        DateTile(date: _date, onChanged: (d) => setState(() => _date = d ?? _date)),
      ],
    );
  }
}

Future<void> showGoalSheet(BuildContext context, {Goal? existing}) => showSheet<void>(context, GoalSheet(existing: existing));

class GoalSheet extends StatefulWidget {
  const GoalSheet({super.key, this.existing});
  final Goal? existing;

  @override
  State<GoalSheet> createState() => _GoalSheetState();
}

class _GoalSheetState extends State<GoalSheet> {
  late final Goal _g = widget.existing?.copy() ?? Goal.create(name: '');
  late final _name = TextEditingController(text: _g.name);
  late final _target = TextEditingController(text: _g.targetCents > 0 ? Money.editable(_g.targetCents) : '');
  late final _saved = TextEditingController(text: _g.savedCents > 0 ? Money.editable(_g.savedCents) : '');
  late final _note = TextEditingController(text: _g.note);
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _target.dispose();
    _saved.dispose();
    _note.dispose();
    super.dispose();
  }

  void _save() {
    final store = StoreScope.read(context);
    final name = _name.text.trim();
    if (name.isEmpty) return setState(() => _error = 'Enter a name, like "Emergency fund".');
    final target = readMoney(_target);
    if (target == null || target <= 0) return setState(() => _error = 'Enter how much you want to save.');
    final saved = readMoney(_saved, emptyIsZero: true);
    if (saved == null) return setState(() => _error = 'Enter what\'s saved so far as a number.');
    _g
      ..name = name
      ..targetCents = target
      ..note = _note.text.trim();
    if (!_g.isLinked && saved != _g.savedCents) {
      final diff = saved - _g.savedCents;
      _g.savedCents = saved;
      if (widget.existing != null) _g.history = [..._g.history, MoneyEntry(dayOnly(DateTime.now()), diff)];
    }
    store.put(_g);
    Navigator.pop(context);
    toast(context, widget.existing == null ? 'Goal added: $name.' : 'Saved $name.');
  }

  Future<void> _delete() async {
    final g = widget.existing!;
    final ok = await confirm(context, title: 'Delete ${g.name}?', message: 'The goal and its history are removed. Your accounts don\'t change.', confirmText: 'Delete', danger: true);
    if (!ok || !mounted) return;
    final store = StoreScope.read(context);
    store.remove(g);
    Navigator.pop(context);
    toast(context, 'Deleted ${g.name}', action: 'Undo', onAction: () => store.put(g.copy()));
  }

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final isNew = widget.existing == null;
    final accounts = [for (final a in store.accountRecords) if (!a.isClosed && !a.isLiability) a.name];
    return SheetFrame(
      title: isNew ? 'New goal' : 'Edit goal',
      onDelete: isNew ? null : _delete,
      error: _error,
      primary: isNew ? 'Add goal' : 'Save',
      onPrimary: _save,
      children: [
        LabeledField(label: 'Name', controller: _name, hint: 'Emergency fund, Hawaii trip…', caps: TextCapitalization.words, autofocus: isNew),
        const FieldLabel('Target'),
        MoneyField(controller: _target, size: 26),
        const FieldLabel('Target date (optional)'),
        DateTile(date: _g.targetDate, allowClear: true, emptyText: 'No date', onChanged: (d) => setState(() => _g.targetDate = d)),
        if (accounts.isNotEmpty) ...[
          const FieldLabel('Saved in'),
          Choices(
            options: ['Added by hand', ...accounts],
            selected: _g.isLinked ? _g.linkedAccount : 'Added by hand',
            onChanged: (v) => setState(() => _g.linkedAccount = v == 'Added by hand' ? '' : v),
          ),
          const SizedBox(height: 8),
          Text(_g.isLinked ? 'What\'s saved is that account\'s balance.' : 'Add money to it yourself as you save.',
              style: TextStyle(fontSize: 13, color: context.pal.muted)),
        ],
        if (!_g.isLinked) ...[
          const FieldLabel('Saved so far'),
          MoneyField(controller: _saved, size: 22, hint: '0.00'),
        ],
        LabeledField(label: 'Note (optional)', controller: _note),
      ],
    );
  }
}
