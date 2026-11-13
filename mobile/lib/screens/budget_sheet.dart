import 'package:flutter/material.dart';

import '../categories.dart';
import '../models.dart';
import '../money.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

Future<void> showBudgetSheet(BuildContext context, {Budget? existing, String? category}) =>
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (_) => BudgetSheet(existing: existing, category: category),
    );

/// Create or change a monthly budget.
class BudgetSheet extends StatefulWidget {
  const BudgetSheet({super.key, this.existing, this.category});
  final Budget? existing;
  final String? category;

  @override
  State<BudgetSheet> createState() => _BudgetSheetState();
}

class _BudgetSheetState extends State<BudgetSheet> {
  late String _category = widget.existing?.category ?? widget.category ?? '';
  late final _amount = TextEditingController(
      text: widget.existing == null ? '' : Money.editable(widget.existing!.limitCents));
  String? _error;
  bool _filled = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_filled && widget.existing == null && _category.isNotEmpty) {
      _filled = true;
      final s = StoreScope.read(context).suggestedBudget(_category);
      if (s > 0) _amount.text = Money.editable(s);
    }
  }

  @override
  void dispose() {
    _amount.dispose();
    super.dispose();
  }

  void _pick(String c) {
    final s = StoreScope.read(context).suggestedBudget(c);
    setState(() {
      _category = c;
      if (s > 0) _amount.text = Money.editable(s);
    });
  }

  void _save() {
    final cents = Money.parse(_amount.text);
    if (_category.isEmpty) {
      setState(() => _error = 'Pick a category.');
      return;
    }
    if (cents == null || cents <= 0) {
      setState(() => _error = 'Enter how much you want to spend each month.');
      return;
    }
    final store = StoreScope.read(context);
    final old = widget.existing ?? store.budgetFor(_category);
    store.saveBudget(Budget(id: old?.id, category: _category, limitCents: cents, createdAt: old?.createdAt));
    Navigator.pop(context);
    toast(context, '$_category: ${Money.format(cents, whole: true)} a month');
  }

  Future<void> _delete() async {
    final b = widget.existing!;
    final ok = await confirm(context,
        title: 'Delete this budget?',
        message: 'Your ${b.category} transactions stay. Only the monthly limit is removed.',
        confirmText: 'Delete',
        danger: true);
    if (!ok || !mounted) return;
    final store = StoreScope.read(context);
    store.deleteBudget(b.id);
    Navigator.pop(context);
    toast(context, 'Deleted the ${b.category} budget', action: 'Undo', onAction: () => store.saveBudget(b));
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final store = StoreScope.of(context);
    final editing = widget.existing != null;
    final available = spendingCategories.where((c) => c == _category || store.budgetFor(c) == null).toList();
    final suggestion = _category.isEmpty ? 0 : store.suggestedBudget(_category);

    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(22, 0, 22, 24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                if (editing) ...[CategoryAvatar(_category, size: 44), const SizedBox(width: 12)],
                Expanded(child: Text(editing ? _category : 'New budget', style: display(24, p.text))),
                if (editing)
                  IconButton(
                      tooltip: 'Delete', onPressed: _delete, icon: Icon(Icons.delete_outline_rounded, color: p.danger)),
                IconButton(
                    tooltip: 'Close',
                    onPressed: () => Navigator.pop(context),
                    icon: Icon(Icons.close_rounded, color: p.muted)),
              ],
            ),
            if (!editing) ...[
              const SizedBox(height: 4),
              Text('A monthly limit for one kind of spending.', style: TextStyle(fontSize: 14.5, color: p.muted)),
              const FieldLabel('Category'),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  for (final c in available)
                    ChoicePill(label: c, icon: categoryIcon(c), selected: c == _category, onTap: () => _pick(c)),
                ],
              ),
            ],
            const FieldLabel('Each month'),
            TextField(
              controller: _amount,
              autofocus: editing,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              style: display(30, p.text),
              decoration: InputDecoration(
                hintText: '0',
                hintStyle: display(30, p.border),
                prefixIcon: Padding(
                  padding: const EdgeInsets.only(left: 18, right: 6),
                  child: Text(Money.currency.symbol, style: display(30, p.muted)),
                ),
                prefixIconConstraints: const BoxConstraints(minWidth: 0, minHeight: 0),
                contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 14),
              ),
            ),
            if (suggestion > 0) ...[
              const SizedBox(height: 10),
              Row(
                children: [
                  Icon(Icons.auto_awesome_rounded, size: 16, color: p.accent),
                  const SizedBox(width: 6),
                  Expanded(
                    child: Text('You\'ve spent about ${Money.format(suggestion, whole: true)} a month here lately.',
                        style: TextStyle(fontSize: 13.5, color: p.muted)),
                  ),
                ],
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: 14),
              Text(_error!, style: TextStyle(color: p.danger, fontWeight: FontWeight.w600)),
            ],
            const SizedBox(height: 24),
            FilledButton(onPressed: _save, child: Text(editing ? 'Save budget' : 'Create budget')),
          ],
        ),
      ),
    );
  }
}
