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
    final goals = [for (final g in store.goals) if (!g.isFund) g]..sort((a, b) => (store.isReached(a) ? 1 : 0).compareTo(store.isReached(b) ? 1 : 0));
    final funds = [for (final g in store.goals) if (g.isFund) g]..sort((a, b) => (a.targetDate ?? DateTime(9999)).compareTo(b.targetDate ?? DateTime(9999)));
    final perMonth = funds.fold<int>(0, (s, g) => s + (store.monthlyNeeded(g, DateTime.now()) ?? 0));
    final saved = goals.fold<int>(0, (s, g) => s + store.savedFor(g));
    final target = goals.fold<int>(0, (s, g) => s + g.targetCents);
    return SubPage(
      title: 'Goals',
      subtitle: 'Something to save toward, and sinking funds for costs that come around every year.',
      fab: () => showGoalSheet(context),
      fabLabel: 'New goal',
      children: [
        if (goals.isEmpty && funds.isEmpty)
          EmptyState(
            icon: Icons.flag_outlined,
            title: 'No goals yet',
            text: 'Set a target (and a date if you like). WillowWallet shows how much to put away each month.',
            actions: [
              SizedBox(width: double.infinity, child: FilledButton.icon(onPressed: () => showGoalSheet(context), icon: const Icon(Icons.add_rounded), label: const Text('New goal'))),
              const SizedBox(height: 8),
              SizedBox(
                  width: double.infinity,
                  child: OutlinedButton.icon(onPressed: () => showGoalSheet(context, fund: true), icon: const Icon(Icons.savings_outlined), label: const Text('New sinking fund'))),
            ],
          )
        else if (goals.isNotEmpty) ...[
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
        if (goals.isNotEmpty || funds.isNotEmpty) ...[
          SectionTitle('Sinking funds', action: 'Add', onAction: () => showGoalSheet(context, fund: true)),
          Text(
              funds.isEmpty
                  ? 'Set aside a little each month for costs that come around every year or so: car registration, insurance, holidays. '
                      'No more surprise bills.'
                  : 'About ${Money.format(perMonth, whole: true)} a month covers all of them. The forecast sets it aside for you.',
              style: TextStyle(fontSize: 13.5, color: p.muted, height: 1.35)),
          const SizedBox(height: 10),
          for (final g in funds) ...[_GoalCard(g), const SizedBox(height: 10)],
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
    final dueSoon = g.isFund && g.targetDate != null && !dayOnly(g.targetDate!).isAfter(today.add(const Duration(days: 14)));
    final lines = g.isFund
        ? [
            if (g.targetDate != null) '${Money.format(g.targetCents, whole: true)} due ${shortDate(g.targetDate!)}',
            g.repeats.toLowerCase(),
            if (reached) 'ready' else if (needed != null) '${Money.format(needed, whole: true)}/month',
          ]
        : [
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
              IconBox(g.isFund ? Icons.savings_outlined : (reached ? Icons.emoji_events_outlined : Icons.flag_outlined),
                  color: dueSoon && !reached ? p.warn : (reached ? p.positive : null)),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(g.name, style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700)),
                    Text(lines.join(' · '), style: TextStyle(fontSize: 13, color: dueSoon && !reached ? p.warn : (reached ? p.positive : p.muted))),
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
      subtitle: g.isFund && g.targetDate != null ? '${Money.format(g.targetCents)} due ${shortDate(g.targetDate!)}, ${g.repeats.toLowerCase()}.' : null,
      leading: IconBox(g.isFund ? Icons.savings_outlined : Icons.flag_outlined, size: 44),
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
        if (g.isFund) ...[
          const SizedBox(height: 8),
          FilledButton.tonalIcon(
            onPressed: () {
              Navigator.pop(context);
              store.useFund(g, DateTime.now());
              final next = store.goals.firstWhere((x) => x.id == g.id).targetDate;
              toast(context, 'Paid. Saving for next time${next == null ? '' : ', due ${shortDate(next)}'}.');
            },
            icon: const Icon(Icons.check_rounded),
            label: const Text('I paid it: start saving for next time'),
          ),
        ],
        const SizedBox(height: 8),
        OutlinedButton.icon(
          onPressed: () {
            Navigator.pop(context);
            showGoalSheet(context, existing: g);
          },
          icon: const Icon(Icons.edit_outlined, size: 19),
          label: Text(g.isFund ? 'Edit sinking fund' : 'Edit goal'),
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

Future<void> showGoalSheet(BuildContext context, {Goal? existing, bool fund = false}) => showSheet<void>(context, GoalSheet(existing: existing, fund: fund));

class GoalSheet extends StatefulWidget {
  const GoalSheet({super.key, this.existing, this.fund = false});
  final Goal? existing;

  /// A new sinking fund rather than a goal.
  final bool fund;

  @override
  State<GoalSheet> createState() => _GoalSheetState();
}

class _GoalSheetState extends State<GoalSheet> {
  late final Goal _g = widget.existing?.copy() ?? (Goal.create(name: '')..isFund = widget.fund);
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
    if (name.isEmpty) return setState(() => _error = _g.isFund ? 'Enter a name, like "Car registration".' : 'Enter a name, like "Emergency fund".');
    final target = readMoney(_target);
    if (target == null || target <= 0) return setState(() => _error = _g.isFund ? 'Enter what it costs.' : 'Enter how much you want to save.');
    if (_g.isFund && _g.targetDate == null) return setState(() => _error = 'Pick when it\'s next due.');
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
    toast(context, widget.existing == null ? (_g.isFund ? 'Sinking fund added: $name.' : 'Goal added: $name.') : 'Saved $name.');
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
    final fund = _g.isFund;
    return SheetFrame(
      title: fund ? (isNew ? 'New sinking fund' : 'Edit sinking fund') : (isNew ? 'New goal' : 'Edit goal'),
      subtitle: fund && isNew ? 'For a cost that comes around again. WillowWallet works out how much to set aside each month.' : null,
      onDelete: isNew ? null : _delete,
      error: _error,
      primary: isNew ? (fund ? 'Add sinking fund' : 'Add goal') : 'Save',
      onPrimary: _save,
      children: [
        LabeledField(
            label: 'Name', controller: _name, hint: fund ? 'Car registration, holidays…' : 'Emergency fund, Hawaii trip…', caps: TextCapitalization.words, autofocus: isNew),
        FieldLabel(fund ? 'What it costs' : 'Target'),
        MoneyField(controller: _target, size: 26),
        FieldLabel(fund ? 'Next due' : 'Target date (optional)'),
        DateTile(date: _g.targetDate, allowClear: !fund, emptyText: fund ? 'Pick a date' : 'No date', onChanged: (d) => setState(() => _g.targetDate = d)),
        if (fund) ...[
          const FieldLabel('Comes around'),
          Choices(options: FundSchedule.all, selected: _g.repeats, onChanged: (v) => setState(() => _g.repeats = v)),
        ],
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
