import 'package:flutter/material.dart';

import '../categories.dart';
import '../kit.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

/// Categories (with subcategories) and rules that pick a category from the payee. Shared with the PC.
class CategoriesScreen extends StatefulWidget {
  const CategoriesScreen({super.key});

  @override
  State<CategoriesScreen> createState() => _CategoriesScreenState();
}

class _CategoriesScreenState extends State<CategoriesScreen> {
  int _tab = 0; // spending, income, rules

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final income = _tab == 1;
    final cats = store.allCategories(income: income);
    return SubPage(
      title: 'Categories',
      subtitle: 'Shared with WillowWallet on your PC when you sync.',
      fab: () => _tab == 2 ? showSheet<void>(context, const RuleSheet()) : _add(context, store, income),
      fabLabel: _tab == 2 ? 'Add rule' : 'Add category',
      children: [
        PillToggle(options: const ['Spending', 'Income', 'Rules'], selected: _tab, onChanged: (i) => setState(() => _tab = i)),
        const SizedBox(height: 12),
        if (_tab == 2) ...[
          HintNote('When a payee contains the words of a rule, new and imported transactions get its category. The longest match wins.', icon: Icons.rule_rounded),
          const SizedBox(height: 12),
          if (store.rules.isEmpty)
            Text('No rules yet.', style: TextStyle(color: p.muted))
          else
            ListPanel(
              children: [
                for (final r in store.rules..sort((a, b) => a.contains.toLowerCase().compareTo(b.contains.toLowerCase())))
                  RowTile(
                    leading: CategoryAvatar(store.rootOf(r.category), size: 36),
                    title: '"${r.contains}"',
                    subtitle: '→ ${store.displayCategory(r.category)}',
                    onTap: () => showSheet<void>(context, RuleSheet(existing: r)),
                  ),
              ],
            ),
        ] else
          ListPanel(
            children: [
              for (final c in cats)
                Padding(
                  padding: EdgeInsets.only(left: c.parent.isEmpty ? 0 : 28),
                  child: RowTile(
                    leading: CategoryAvatar(c.parent.isEmpty ? c.name : c.parent, size: c.parent.isEmpty ? 38 : 30, income: income),
                    title: c.name,
                    subtitle: [if (c.parent.isNotEmpty) 'in ${c.parent}', if (c.isHidden) 'Hidden', if (store.budgetFor(c.name) != null) 'Has a budget'].join(' · ').let((s) => s.isEmpty ? null : s),
                    subtitleColor: c.isHidden ? p.warn : null,
                    onTap: () => _options(context, store, c),
                  ),
                ),
            ],
          ),
      ],
    );
  }

  Future<void> _add(BuildContext context, Store store, bool income) async {
    final parents = [for (final c in store.allCategories(income: income)) if (c.parent.isEmpty) c.name];
    final result = await _askName(context, title: 'Add category', parents: parents);
    if (result == null) return;
    final (name, parent) = result;
    if (store.allCategories(income: true).followedBy(store.allCategories(income: false)).any((c) => c.name.toLowerCase() == name.toLowerCase())) {
      if (context.mounted) toast(context, 'There\'s already a category called $name.');
      return;
    }
    store.addCategory(name, income: income, parent: parent);
    if (context.mounted) toast(context, parent.isEmpty ? 'Added $name.' : 'Added $name under $parent.');
  }

  Future<void> _options(BuildContext context, Store store, CategoryDef c) async {
    final used = store.isCategoryUsed(c.name) || store.childrenOf(c.name).isNotEmpty;
    final choice = await showAdaptiveSheet<String>(
      context: context,
      useSafeArea: true,
      builder: (context) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Padding(padding: const EdgeInsets.fromLTRB(22, 0, 22, 8), child: Text(c.name, style: display(22, context.pal.text))),
            ListTile(leading: const Icon(Icons.edit_outlined), title: const Text('Rename'), onTap: () => Navigator.pop(context, 'rename')),
            ListTile(
              leading: Icon(c.isHidden ? Icons.visibility_outlined : Icons.visibility_off_outlined),
              title: Text(c.isHidden ? 'Show' : 'Hide'),
              subtitle: const Text('Hidden categories aren\'t offered for new transactions'),
              onTap: () => Navigator.pop(context, 'hide'),
            ),
            ListTile(
              leading: Icon(Icons.delete_outline_rounded, color: used ? null : context.pal.danger),
              title: const Text('Delete'),
              subtitle: used ? const Text('In use. Hide it instead, or rename it.') : null,
              enabled: !used,
              onTap: () => Navigator.pop(context, 'delete'),
            ),
            const SizedBox(height: 8),
          ],
        ),
      ),
    );
    if (!context.mounted || choice == null) return;
    switch (choice) {
      case 'rename':
        final r = await _askName(context, title: 'Rename ${c.name}', initial: c.name);
        if (r != null && r.$1 != c.name) {
          store.renameCategory(c.name, r.$1);
          if (context.mounted) toast(context, 'Renamed to ${r.$1} everywhere.');
        }
      case 'hide':
        store.setCategoryHidden(c.name, !c.isHidden);
      case 'delete':
        store.deleteCategory(c.name);
    }
  }

  Future<(String, String)?> _askName(BuildContext context, {required String title, String initial = '', List<String>? parents}) {
    final c = TextEditingController(text: initial);
    var parent = '';
    return showSheet<(String, String)>(
      context,
      StatefulBuilder(
        builder: (context, set) => SheetFrame(
          title: title,
          primary: 'Save',
          onPrimary: () {
            if (c.text.trim().isEmpty) return;
            Navigator.pop(context, (c.text.trim(), parent));
          },
          children: [
            LabeledField(label: 'Name', controller: c, autofocus: true, caps: TextCapitalization.sentences),
            if (parents != null) ...[
              const FieldLabel('Inside'),
              Choices(options: ['Nothing (top level)', ...parents], selected: parent.isEmpty ? 'Nothing (top level)' : parent, onChanged: (v) => set(() => parent = v == 'Nothing (top level)' ? '' : v)),
            ],
          ],
        ),
      ),
    );
  }
}

extension _Let on String {
  T let<T>(T Function(String) f) => f(this);
}

class RuleSheet extends StatefulWidget {
  const RuleSheet({super.key, this.existing});
  final Rule? existing;

  @override
  State<RuleSheet> createState() => _RuleSheetState();
}

class _RuleSheetState extends State<RuleSheet> {
  late final Rule _r = widget.existing?.copy() ?? Rule.create(contains: '', category: '');
  late final _contains = TextEditingController(text: _r.contains);
  bool _income = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _income = incomeCategories.contains(_r.category);
  }

  @override
  void dispose() {
    _contains.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    return SheetFrame(
      title: widget.existing == null ? 'Add rule' : 'Edit rule',
      subtitle: 'When the payee contains…',
      onDelete: widget.existing == null
          ? null
          : () {
              store.remove(widget.existing!);
              Navigator.pop(context);
            },
      error: _error,
      primary: 'Save rule',
      onPrimary: () {
        if (_contains.text.trim().isEmpty) return setState(() => _error = 'Enter the words to look for, like "STARBUCKS".');
        if (_r.category.isEmpty) return setState(() => _error = 'Pick a category.');
        store.put(_r..contains = _contains.text.trim());
        Navigator.pop(context);
      },
      children: [
        LabeledField(label: 'Payee contains', controller: _contains, hint: 'STARBUCKS', caps: TextCapitalization.none, autofocus: widget.existing == null),
        const SizedBox(height: 14),
        PillToggle(options: const ['Spending', 'Income'], selected: _income ? 1 : 0, onChanged: (i) => setState(() => _income = i == 1)),
        const FieldLabel('Use category'),
        Choices(options: store.categoriesFor(income: _income), selected: _r.category, icons: categoryIcon, onChanged: (v) => setState(() => _r.category = v)),
      ],
    );
  }
}
