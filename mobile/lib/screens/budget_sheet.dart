import 'package:flutter/material.dart';

import '../categories.dart';
import '../finance.dart';
import '../kit.dart';
import '../models.dart';
import '../money.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

Future<void> showBudgetSheet(BuildContext context, {Budget? existing, String? category, DateTime? month}) =>
    showSheet<void>(context, BudgetSheet(existing: existing, category: category, month: month));

/// Create or change a monthly budget (with rollover, or a different amount for one month).
class BudgetSheet extends StatefulWidget {
  const BudgetSheet({super.key, this.existing, this.category, this.month});
  final Budget? existing;
  final String? category;
  final DateTime? month;

  @override
  State<BudgetSheet> createState() => _BudgetSheetState();
}

class _BudgetSheetState extends State<BudgetSheet> {
  late String _category = widget.existing?.category ?? widget.category ?? '';
  late final _amount = TextEditingController(text: widget.existing == null ? '' : Money.editable(widget.existing!.limitCents));
  late final _monthAmount = TextEditingController();
  late bool _rollover = widget.existing?.rollover ?? false;
  bool _oneMonth = false;
  String? _error;
  bool _filled = false;

  DateTime get _month => widget.month ?? DateTime.now();

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_filled) return;
    _filled = true;
    final store = StoreScope.read(context);
    final b = widget.existing;
    if (b != null) {
      final m = store.limitFor(b, _month);
      if (m != b.limitCents) {
        _oneMonth = true;
        _monthAmount.text = Money.editable(m);
      }
    } else if (_category.isNotEmpty) {
      final s = store.suggestedBudget(_category);
      if (s > 0) _amount.text = Money.editable(s);
    }
  }

  @override
  void dispose() {
    _amount.dispose();
    _monthAmount.dispose();
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
    if (_category.isEmpty) return setState(() => _error = 'Pick a category.');
    if (cents == null || cents <= 0) return setState(() => _error = 'Enter how much you want to spend each month.');
    int? monthCents;
    if (_oneMonth) {
      monthCents = Money.parse(_monthAmount.text);
      if (monthCents == null || monthCents < 0) return setState(() => _error = 'Enter the amount for ${StoreScope.read(context).monthName(_month)}.');
    }
    final store = StoreScope.read(context);
    final old = widget.existing ?? store.budgetFor(_category);
    var b = old == null ? Budget(category: _category, limitCents: cents) : old.copyWith(limitCents: cents);
    b = b.copyWith(rollover: _rollover);
    store.saveBudget(b);
    final saved = store.budgetFor(_category)!;
    store.setMonthAmount(saved, _month, _oneMonth ? monthCents : null);
    Navigator.pop(context);
    toast(context, _oneMonth ? '$_category: ${Money.format(monthCents!, whole: true)} for ${store.monthName(_month)}' : '$_category: ${Money.format(cents, whole: true)} a month');
  }

  Future<void> _delete() async {
    final b = widget.existing!;
    final ok = await confirm(context,
        title: 'Delete this budget?', message: 'Your ${b.category} transactions stay. Only the monthly limit is removed.', confirmText: 'Delete', danger: true);
    if (!ok || !mounted) return;
    final store = StoreScope.read(context);
    store.deleteBudget(b.id);
    Navigator.pop(context);
    toast(context, 'Deleted the ${b.category} budget', action: 'Undo', onAction: () => store.saveBudget(b.touched()));
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final store = StoreScope.of(context);
    final editing = widget.existing != null;
    final available = store.categoriesFor(income: false).where((c) => c == _category || store.budgetFor(c) == null).toList();
    final suggestion = _category.isEmpty ? 0 : store.suggestedBudget(_category);
    final carry = editing ? store.carryFor(widget.existing!, _month) : 0;

    return SheetFrame(
      title: editing ? _category : 'New budget',
      leading: editing ? CategoryAvatar(_category, size: 44) : null,
      subtitle: editing ? null : 'A monthly limit for one kind of spending.',
      onDelete: editing ? _delete : null,
      error: _error,
      primary: editing ? 'Save budget' : 'Create budget',
      onPrimary: _save,
      children: [
        if (!editing) ...[
          const FieldLabel('Category'),
          Choices(options: available, selected: _category, icons: categoryIcon, onChanged: _pick),
        ],
        const FieldLabel('Each month'),
        MoneyField(controller: _amount, autofocus: editing, hint: '0'),
        if (suggestion > 0) ...[
          const SizedBox(height: 10),
          Row(
            children: [
              Icon(Icons.auto_awesome_rounded, size: 16, color: p.accent),
              const SizedBox(width: 6),
              Expanded(
                child: Text('You\'ve spent about ${Money.format(suggestion, whole: true)} a month here lately.', style: TextStyle(fontSize: 13.5, color: p.muted)),
              ),
            ],
          ),
        ],
        const SizedBox(height: 12),
        SwitchRow(
          title: 'Roll over',
          subtitle: 'Money left at the end of a month carries into the next (and overspending comes out of it).'
              '${carry != 0 ? ' ${carry > 0 ? '+' : '−'}${Money.format(carry.abs(), whole: true)} carried into ${store.monthName(_month)}.' : ''}',
          value: _rollover,
          onChanged: (v) => setState(() => _rollover = v),
        ),
        SwitchRow(
          title: 'Different amount for ${store.monthName(_month)}',
          subtitle: 'Just this month, like a holiday month for Gifts.',
          value: _oneMonth,
          onChanged: (v) => setState(() {
            _oneMonth = v;
            if (v && _monthAmount.text.isEmpty) _monthAmount.text = _amount.text;
          }),
        ),
        if (_oneMonth) ...[
          const SizedBox(height: 6),
          MoneyField(controller: _monthAmount, size: 24),
        ],
      ],
    );
  }
}
