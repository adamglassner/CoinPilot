import 'package:flutter/material.dart';

import '../categories.dart';
import '../dates.dart';
import '../models.dart';
import '../money.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

Future<void> showTransactionSheet(BuildContext context, {Txn? existing}) => showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (_) => TransactionSheet(existing: existing),
    );

/// Add or edit a transaction.
class TransactionSheet extends StatefulWidget {
  const TransactionSheet({super.key, this.existing});
  final Txn? existing;

  @override
  State<TransactionSheet> createState() => _TransactionSheetState();
}

class _TransactionSheetState extends State<TransactionSheet> {
  late final Txn? _old = widget.existing;
  late bool _income = _old?.isIncome ?? false;
  late final _amount = TextEditingController(text: _old == null ? '' : Money.editable(_old!.cents));
  late final _payee = TextEditingController(text: _old?.payee ?? '');
  late final _note = TextEditingController(text: _old?.note ?? '');
  late String _category = _old?.category ?? '';
  late String _account = _old?.account ?? 'Checking';
  late DateTime _date = _old?.date ?? dayOnly(DateTime.now());
  bool _categoryChosen = false;
  String? _error;

  @override
  void dispose() {
    _amount.dispose();
    _payee.dispose();
    _note.dispose();
    super.dispose();
  }

  void _payeeChanged(String text) {
    if (_categoryChosen) return;
    final guess = StoreScope.read(context).categoryForPayee(text, income: _income);
    if (guess != null && guess != _category) setState(() => _category = guess);
  }

  Future<void> _pickDate() async {
    final now = DateTime.now();
    final d = await showDatePicker(
      context: context,
      initialDate: _date,
      firstDate: DateTime(2000),
      lastDate: DateTime(now.year + 1, 12, 31),
    );
    if (d != null) setState(() => _date = d);
  }

  void _save() {
    final cents = Money.parse(_amount.text);
    final payee = _payee.text.trim();
    String? error;
    if (cents == null || cents <= 0) {
      error = 'Enter an amount.';
    } else if (payee.isEmpty) {
      error = _income ? 'Who paid you? Enter a name.' : 'Where did you spend it? Enter a payee.';
    } else if (_category.isEmpty) {
      error = 'Pick a category.';
    }
    if (error != null) {
      setState(() => _error = error);
      return;
    }
    final t = Txn(
      id: _old?.id,
      payee: payee,
      category: _category,
      account: _account,
      date: _date,
      cents: _income ? cents! : -cents!,
      note: _note.text.trim(),
      createdAt: _old?.createdAt,
    );
    StoreScope.read(context).saveTxn(t);
    Navigator.pop(context);
    toast(context, _old == null ? 'Added ${Money.format(t.cents.abs())} · $payee' : 'Saved $payee');
  }

  Future<void> _delete() async {
    final t = _old!;
    final ok = await confirm(context,
        title: 'Delete this transaction?',
        message: '${t.payee}, ${Money.format(t.cents.abs())} on ${shortDate(t.date)}.',
        confirmText: 'Delete',
        danger: true);
    if (!ok || !mounted) return;
    final store = StoreScope.read(context);
    store.deleteTxn(t.id);
    Navigator.pop(context);
    toast(context, 'Deleted ${t.payee}', action: 'Undo', onAction: () => store.saveTxn(t));
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final store = StoreScope.of(context);
    final cats = _income ? incomeCategories : spendingCategories;
    final quick = _old == null && _payee.text.isEmpty ? store.recentPayees(income: _income) : const <String>[];

    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(22, 0, 22, 24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(_old == null ? 'Add transaction' : 'Edit transaction', style: display(24, p.text)),
                ),
                if (_old != null)
                  IconButton(
                    tooltip: 'Delete',
                    onPressed: _delete,
                    icon: Icon(Icons.delete_outline_rounded, color: p.danger),
                  ),
                IconButton(
                  tooltip: 'Close',
                  onPressed: () => Navigator.pop(context),
                  icon: Icon(Icons.close_rounded, color: p.muted),
                ),
              ],
            ),
            const SizedBox(height: 14),
            PillToggle(
              options: const ['Spending', 'Income'],
              selected: _income ? 1 : 0,
              onChanged: (i) => setState(() {
                _income = i == 1;
                _category = '';
                _categoryChosen = false;
                _payeeChanged(_payee.text);
              }),
            ),
            const FieldLabel('Amount'),
            TextField(
              controller: _amount,
              autofocus: _old == null,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              textInputAction: TextInputAction.next,
              style: display(30, _income ? p.positive : p.text),
              decoration: InputDecoration(
                hintText: Money.currency.decimals == 0 ? '0' : '0.00',
                hintStyle: display(30, p.border),
                prefixIcon: Padding(
                  padding: const EdgeInsets.only(left: 18, right: 6),
                  child: Text(Money.currency.symbol, style: display(30, p.muted)),
                ),
                prefixIconConstraints: const BoxConstraints(minWidth: 0, minHeight: 0),
                contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 14),
              ),
            ),
            FieldLabel(_income ? 'From' : 'Payee'),
            TextField(
              controller: _payee,
              textCapitalization: TextCapitalization.words,
              textInputAction: TextInputAction.done,
              onChanged: (t) {
                _payeeChanged(t);
                setState(() {});
              },
              decoration: InputDecoration(hintText: _income ? 'Employer or person' : 'Store, restaurant, person…'),
            ),
            if (quick.isNotEmpty) ...[
              const SizedBox(height: 10),
              SizedBox(
                height: 38,
                child: ListView.separated(
                  scrollDirection: Axis.horizontal,
                  itemCount: quick.length,
                  separatorBuilder: (_, __) => const SizedBox(width: 8),
                  itemBuilder: (context, i) => ChoicePill(
                    label: quick[i],
                    icon: Icons.history_rounded,
                    selected: false,
                    onTap: () {
                      _payee.text = quick[i];
                      _payeeChanged(quick[i]);
                      setState(() {});
                    },
                  ),
                ),
              ),
            ],
            const FieldLabel('Category'),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final c in cats)
                  ChoicePill(
                    label: c,
                    icon: categoryIcon(c),
                    selected: c == _category,
                    onTap: () => setState(() {
                      _category = c;
                      _categoryChosen = true;
                    }),
                  ),
              ],
            ),
            const FieldLabel('Account'),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final a in store.accounts)
                  ChoicePill(label: a, selected: a == _account, onTap: () => setState(() => _account = a)),
              ],
            ),
            const FieldLabel('Date'),
            Panel(
              onTap: _pickDate,
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 15),
              child: Row(
                children: [
                  Icon(Icons.calendar_today_rounded, size: 19, color: p.accent),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Text(dayLabel(_date) == 'Today' ? 'Today, ${shortDate(_date)}' : dayLabel(_date),
                        style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w600)),
                  ),
                  Icon(Icons.chevron_right_rounded, color: p.muted),
                ],
              ),
            ),
            const FieldLabel('Note (optional)'),
            TextField(
              controller: _note,
              textCapitalization: TextCapitalization.sentences,
              maxLines: 2,
              minLines: 1,
              decoration: const InputDecoration(hintText: 'Anything to remember'),
            ),
            if (_error != null) ...[
              const SizedBox(height: 16),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                decoration: BoxDecoration(
                  color: p.danger.withValues(alpha: 0.1),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: p.danger.withValues(alpha: 0.5)),
                ),
                child: Row(
                  children: [
                    Icon(Icons.error_outline_rounded, color: p.danger, size: 20),
                    const SizedBox(width: 10),
                    Expanded(child: Text(_error!, style: TextStyle(color: p.danger, fontWeight: FontWeight.w600))),
                  ],
                ),
              ),
            ],
            const SizedBox(height: 22),
            FilledButton(
              onPressed: _save,
              child: Text(_old == null ? (_income ? 'Add income' : 'Add spending') : 'Save changes'),
            ),
          ],
        ),
      ),
    );
  }
}
