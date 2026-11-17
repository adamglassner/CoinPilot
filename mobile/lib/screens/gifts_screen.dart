import 'package:flutter/material.dart';

import '../extras.dart';
import '../kit.dart';
import '../money.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

/// Gifts for the holidays, birthdays and other occasions: a budget per person, and the ideas or what you bought.
class GiftsScreen extends StatefulWidget {
  const GiftsScreen({super.key});

  @override
  State<GiftsScreen> createState() => _GiftsScreenState();
}

class _GiftsScreenState extends State<GiftsScreen> {
  String? _occasion;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final occasions = store.giftOccasions();
    final names = occasions.keys.toList()..sort();
    final occasion = _occasion != null && occasions.containsKey(_occasion) ? _occasion! : (names.isEmpty ? null : names.last);
    final plans = occasion == null ? <GiftPlan>[] : occasions[occasion]!;
    final budget = plans.fold<int>(0, (s, g) => s + g.budgetCents), bought = plans.fold<int>(0, (s, g) => s + g.boughtCents);
    return SubPage(
      title: 'Gifts',
      subtitle: 'Plan gifts for the holidays and birthdays: a budget for each person, ideas, and what you\'ve bought.',
      fab: () => showSheet<void>(context, _PersonSheet(occasion: occasion ?? 'Holidays ${DateTime.now().year}')),
      fabLabel: 'Add a person',
      children: [
        if (names.isNotEmpty)
          SizedBox(
            height: 40,
            child: ListView(scrollDirection: Axis.horizontal, children: [
              for (final n in names) Padding(padding: const EdgeInsets.only(right: 8), child: ChoicePill(label: n, selected: n == occasion, onTap: () => setState(() => _occasion = n))),
            ]),
          ),
        if (plans.isEmpty)
          EmptyState(
            icon: Icons.card_giftcard_rounded,
            title: 'Plan your gifts',
            text: 'Add the people you\'re buying for, a budget for each, and gift ideas. Tick them off as you buy.',
            actions: [
              SizedBox(
                  width: double.infinity,
                  child: FilledButton.icon(
                      onPressed: () => showSheet<void>(context, _PersonSheet(occasion: 'Holidays ${DateTime.now().year}')),
                      icon: const Icon(Icons.person_add_alt_rounded),
                      label: Text('Start Holidays ${DateTime.now().year}'))),
            ],
          )
        else ...[
          const SizedBox(height: 12),
          HeroCard(
            label: '$occasion: spent',
            value: Money.format(bought, whole: true),
            footer: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              ProgressBar(value: budget <= 0 ? 0 : bought / budget, color: bought > budget ? p.danger : p.mint, track: p.heroTrack, height: 9),
              const SizedBox(height: 10),
              Text('of ${Money.format(budget, whole: true)} for ${plans.length} ${plans.length == 1 ? 'person' : 'people'}', style: TextStyle(fontSize: 13.5, color: p.heroMuted)),
            ]),
          ),
          const SizedBox(height: 12),
          for (final g in plans) ...[
            Panel(
              onTap: () => showSheet<void>(context, _PersonSheet(occasion: g.occasion, existing: g)),
              child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                Row(children: [
                  Expanded(child: Text(g.person, style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700))),
                  Text('${Money.format(g.boughtCents, whole: true)} of ${Money.format(g.budgetCents, whole: true)}',
                      style: TextStyle(color: g.boughtCents > g.budgetCents ? p.danger : p.muted, fontWeight: FontWeight.w600)),
                ]),
                const SizedBox(height: 6),
                if (g.items.isEmpty) Text('No ideas yet', style: TextStyle(color: p.muted, fontSize: 13)),
                for (final (i, item) in g.items.indexed)
                  InkWell(
                    onTap: () {
                      final items = g.items;
                      items[i] = GiftItem(item.name, item.cents, !item.bought);
                      store.put(g.copy()..items = items);
                    },
                    child: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 4),
                      child: Row(children: [
                        Icon(item.bought ? Icons.check_circle_rounded : Icons.radio_button_unchecked_rounded, size: 20, color: item.bought ? p.positive : p.muted),
                        const SizedBox(width: 8),
                        Expanded(child: Text(item.name, style: TextStyle(decoration: item.bought ? TextDecoration.lineThrough : null))),
                        if (item.cents > 0) Text(Money.format(item.cents, whole: true), style: TextStyle(color: p.muted)),
                      ]),
                    ),
                  ),
              ]),
            ),
            const SizedBox(height: 10),
          ],
        ],
      ],
    );
  }
}

class _PersonSheet extends StatefulWidget {
  const _PersonSheet({required this.occasion, this.existing});
  final String occasion;
  final GiftPlan? existing;

  @override
  State<_PersonSheet> createState() => _PersonSheetState();
}

class _PersonSheetState extends State<_PersonSheet> {
  late final _occasion = TextEditingController(text: widget.existing?.occasion ?? widget.occasion);
  late final _person = TextEditingController(text: widget.existing?.person ?? '');
  late final _budget = TextEditingController(text: (widget.existing?.budgetCents ?? 0) > 0 ? Money.editable(widget.existing!.budgetCents) : '');
  late final List<(TextEditingController, TextEditingController, bool)> _items = [
    for (final i in widget.existing?.items ?? const <GiftItem>[]) (TextEditingController(text: i.name), TextEditingController(text: i.cents > 0 ? Money.editable(i.cents) : ''), i.bought),
  ];
  String? _error;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.read(context);
    return SheetFrame(
      title: widget.existing == null ? 'Add a person' : widget.existing!.person,
      error: _error,
      onDelete: widget.existing == null
          ? null
          : () {
              store.remove(widget.existing!);
              Navigator.pop(context);
            },
      primary: 'Save',
      onPrimary: () {
        if (_person.text.trim().isEmpty) return setState(() => _error = 'Who is it for?');
        final g = widget.existing?.copy() ?? GiftPlan.create(occasion: '', person: '');
        g
          ..occasion = _occasion.text.trim().isEmpty ? widget.occasion : _occasion.text.trim()
          ..person = _person.text.trim()
          ..budgetCents = Money.parse(_budget.text) ?? 0
          ..items = [
            for (final (n, a, b) in _items)
              if (n.text.trim().isNotEmpty) GiftItem(n.text.trim(), Money.parse(a.text) ?? 0, b),
          ];
        store.put(g);
        Navigator.pop(context);
      },
      children: [
        LabeledField(label: 'Occasion', controller: _occasion, hint: 'Holidays 2026, Mom\'s birthday', caps: TextCapitalization.words),
        LabeledField(label: 'Person', controller: _person, hint: 'A name', caps: TextCapitalization.words, autofocus: widget.existing == null),
        const FieldLabel('Budget'),
        MoneyField(controller: _budget, size: 22),
        const FieldLabel('Gift ideas'),
        for (final (i, (n, a, _)) in _items.indexed)
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: Row(children: [
              Expanded(flex: 3, child: TextField(controller: n, decoration: const InputDecoration(hintText: 'Idea'))),
              const SizedBox(width: 8),
              Expanded(flex: 2, child: TextField(controller: a, keyboardType: const TextInputType.numberWithOptions(decimal: true), decoration: const InputDecoration(hintText: 'Price'))),
              IconButton(tooltip: 'Remove', onPressed: () => setState(() => _items.removeAt(i)), icon: const Icon(Icons.close_rounded)),
            ]),
          ),
        Align(
          alignment: Alignment.centerLeft,
          child: TextButton.icon(
            onPressed: () => setState(() => _items.add((TextEditingController(), TextEditingController(), false))),
            icon: const Icon(Icons.add_rounded, size: 18),
            label: const Text('Add an idea'),
          ),
        ),
      ],
    );
  }
}
