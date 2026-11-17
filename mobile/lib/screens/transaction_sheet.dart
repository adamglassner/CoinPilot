import 'dart:io';

import 'package:flutter/material.dart';

import '../categories.dart';
import '../dates.dart';
import '../kit.dart';
import '../models.dart';
import '../money.dart';
import '../platform.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

Future<void> showTransactionSheet(BuildContext context, {Txn? existing, bool income = false, bool transfer = false}) => showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (_) => TransactionSheet(existing: existing, income: income, transfer: transfer),
    );

/// Add or edit a transaction: spending, income or a transfer between accounts, optionally split across categories,
/// with a receipt photo.
class TransactionSheet extends StatefulWidget {
  const TransactionSheet({super.key, this.existing, this.income = false, this.transfer = false});
  final Txn? existing;
  final bool income;
  final bool transfer;

  @override
  State<TransactionSheet> createState() => _TransactionSheetState();
}

class _SplitLine {
  _SplitLine(this.category, int cents) : amount = TextEditingController(text: cents == 0 ? '' : Money.editable(cents));
  String category;
  final TextEditingController amount;
}

class _TransactionSheetState extends State<TransactionSheet> {
  late final Txn? _old = widget.existing;
  // 0 spending, 1 income, 2 transfer
  late int _kind = _old == null ? (widget.transfer ? 2 : widget.income ? 1 : 0) : (_old!.isTransfer ? 2 : _old!.isIncome ? 1 : 0);
  late final _amount = TextEditingController(text: _old == null ? '' : Money.editable(_old!.cents));
  late final _payee = TextEditingController(text: _old?.payee ?? '');
  late final _note = TextEditingController(text: _old?.note ?? '');
  late final _tags = TextEditingController(text: _old?.tags.join(', ') ?? '');
  late String _category = _old?.category ?? '';
  late String _account = _old?.account ?? '';
  late String _to = _old?.transferAccount ?? '';
  late DateTime _date = _old?.date ?? dayOnly(DateTime.now());
  late String _receipt = _old?.receipt ?? '';
  late final List<_SplitLine> _splits = [for (final (c, cents) in _old?.splits ?? const <(String, int)>[]) _SplitLine(c, cents)];
  bool _categoryChosen = false;
  bool _more = false;
  String? _error;

  bool get _income => _kind == 1;
  bool get _transfer => _kind == 2;

  @override
  void initState() {
    super.initState();
    _more = (_old?.note.isNotEmpty ?? false) || (_old?.tags.isNotEmpty ?? false);
  }

  @override
  void dispose() {
    _amount.dispose();
    _payee.dispose();
    _note.dispose();
    _tags.dispose();
    for (final s in _splits) {
      s.amount.dispose();
    }
    super.dispose();
  }

  void _payeeChanged(String text) {
    if (_categoryChosen || _transfer) return;
    final guess = StoreScope.read(context).categoryForPayee(text, income: _income);
    if (guess != null && guess != _category) setState(() => _category = guess);
  }

  int get _splitTotal => _splits.fold<int>(0, (s, x) => s + (Money.parse(x.amount.text) ?? 0));

  Future<void> _addReceipt(bool camera) async {
    final store = StoreScope.read(context);
    try {
      final photo = camera ? await Platform2.photo(camera: true) : await Platform2.pickFile(extensions: const ['jpg', 'jpeg', 'png', 'pdf', 'heic', 'webp']);
      if (photo == null) return;
      final name = await store.keepFile(photo.bytes, photo.extension.isEmpty ? '.jpg' : photo.extension, folder: 'receipts');
      setState(() => _receipt = name);
    } catch (e) {
      if (mounted) toast(context, 'Couldn\'t add the receipt: $e');
    }
  }

  void _save() {
    final store = StoreScope.read(context);
    final cents = Money.parse(_amount.text);
    final payee = _payee.text.trim();
    final accounts = store.accountNames(current: _account);
    final account = _account.isEmpty ? accounts.first : _account;
    String? error;
    if (cents == null || cents <= 0) {
      error = 'Enter an amount.';
    } else if (_transfer) {
      if (_to.isEmpty) {
        error = 'Pick the account the money goes to.';
      } else if (_to == account) {
        error = 'Pick two different accounts.';
      }
    } else if (payee.isEmpty) {
      error = _income ? 'Who paid you? Enter a name.' : 'Where did you spend it? Enter a payee.';
    } else if (_splits.isNotEmpty) {
      if (_splits.any((s) => s.category.isEmpty || (Money.parse(s.amount.text) ?? 0) <= 0)) {
        error = 'Each part of the split needs a category and an amount.';
      } else if (_splitTotal != cents) {
        error = 'The split adds up to ${Money.format(_splitTotal)}, not ${Money.format(cents)}.';
      }
    } else if (_category.isEmpty) {
      error = 'Pick a category.';
    }
    if (error != null) {
      setState(() => _error = error);
      return;
    }
    final tags = [for (final t in _tags.text.split(',')) if (t.trim().isNotEmpty) t.trim()];
    final extra = <String, dynamic>{
      ...?_old?.extra,
      'TransferAccount': _transfer ? _to : '',
      'Splits': _transfer ? <dynamic>[] : [for (final s in _splits) {'Category': s.category, 'Amount': (Money.parse(s.amount.text) ?? 0) / 100}],
      'Tags': tags,
      'Receipt': _receipt,
    };
    final t = Txn(
      id: _old?.id,
      payee: _transfer ? (payee.isEmpty || payee.startsWith('Transfer to') ? 'Transfer to $_to' : payee) : payee,
      category: _transfer ? 'Transfer' : (_splits.isNotEmpty ? _splits.first.category : _category),
      account: account,
      date: _date,
      cents: _income ? cents! : -cents!,
      note: _note.text.trim(),
      createdAt: _old?.createdAt,
      extra: extra,
    );
    store.saveTxn(t);
    Navigator.pop(context);
    toast(context, _old == null ? (_transfer ? 'Moved ${Money.format(cents)} to $_to' : 'Added ${Money.format(cents)} · $payee') : 'Saved');
  }

  Future<void> _delete() async {
    final t = _old!;
    final ok = await confirm(context,
        title: 'Delete this transaction?', message: '${t.payee}, ${Money.format(t.cents.abs())} on ${shortDate(t.date)}.', confirmText: 'Delete', danger: true);
    if (!ok || !mounted) return;
    final store = StoreScope.read(context);
    store.deleteTxn(t.id);
    Navigator.pop(context);
    toast(context, 'Deleted ${t.payee}', action: 'Undo', onAction: () => store.saveTxn(t.touched()));
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final store = StoreScope.of(context);
    final cats = store.categoriesFor(income: _income);
    final accounts = store.accountNames(current: _account);
    final account = _account.isEmpty ? accounts.first : _account;
    final quick = _old == null && _payee.text.isEmpty && !_transfer ? store.recentPayees(income: _income) : const <String>[];
    final receiptPath = store.receiptPath(_receipt);
    final receiptHere = receiptPath != null && File(receiptPath).existsSync();

    return SheetFrame(
      title: _old == null ? 'Add transaction' : 'Edit transaction',
      onDelete: _old == null ? null : _delete,
      error: _error,
      primary: _old == null ? (_transfer ? 'Transfer' : _income ? 'Add income' : 'Add spending') : 'Save changes',
      onPrimary: _save,
      children: [
        const SizedBox(height: 14),
        PillToggle(
          options: const ['Spending', 'Income', 'Transfer'],
          selected: _kind,
          onChanged: (i) => setState(() {
            _kind = i;
            _category = '';
            _categoryChosen = false;
            if (i != 0) _splits.clear();
            _payeeChanged(_payee.text);
          }),
        ),
        const FieldLabel('Amount'),
        MoneyField(controller: _amount, autofocus: _old == null, color: _income ? p.positive : null, onChanged: (_) => setState(() {})),
        if (!_transfer) ...[
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
          Row(
            children: [
              const Expanded(child: FieldLabel('Category')),
              if (!_income)
                Padding(
                  padding: const EdgeInsets.only(top: 12),
                  child: TextButton.icon(
                    onPressed: () => setState(() {
                      if (_splits.isEmpty) {
                        final total = Money.parse(_amount.text) ?? 0;
                        _splits.add(_SplitLine(_category, total));
                        _splits.add(_SplitLine('', 0));
                      } else {
                        _category = _splits.first.category;
                        for (final s in _splits) {
                          s.amount.dispose();
                        }
                        _splits.clear();
                      }
                    }),
                    icon: Icon(_splits.isEmpty ? Icons.call_split_rounded : Icons.close_rounded, size: 18),
                    label: Text(_splits.isEmpty ? 'Split' : 'Don\'t split'),
                  ),
                ),
            ],
          ),
          if (_splits.isEmpty)
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final c in cats)
                  ChoicePill(
                    label: c,
                    icon: categoryIcon(store.rootOf(c)),
                    selected: c == _category,
                    onTap: () => setState(() {
                      _category = c;
                      _categoryChosen = true;
                    }),
                  ),
              ],
            )
          else ...[
            for (final (i, s) in _splits.indexed) _splitRow(context, i, s, cats),
            Row(
              children: [
                TextButton.icon(onPressed: () => setState(() => _splits.add(_SplitLine('', 0))), icon: const Icon(Icons.add_rounded, size: 18), label: const Text('Add a part')),
                const Spacer(),
                Text(
                  'Split ${Money.format(_splitTotal)} of ${Money.format(Money.parse(_amount.text) ?? 0)}',
                  style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: _splitTotal == (Money.parse(_amount.text) ?? 0) ? p.positive : p.warn),
                ),
              ],
            ),
          ],
        ],
        FieldLabel(_transfer ? 'From' : 'Account'),
        Choices(options: accounts, selected: account, onChanged: (a) => setState(() => _account = a)),
        if (_transfer) ...[
          const FieldLabel('To'),
          Choices(options: [for (final a in accounts) if (a != account) a, if (_to.isNotEmpty && !accounts.contains(_to)) _to], selected: _to, onChanged: (a) => setState(() => _to = a)),
        ],
        const FieldLabel('Date'),
        DateTile(date: _date, last: DateTime(DateTime.now().year + 1, 12, 31), onChanged: (d) => setState(() => _date = d ?? _date)),
        const FieldLabel('Receipt'),
        if (_receipt.isEmpty)
          Row(
            children: [
              Expanded(child: OutlinedButton.icon(onPressed: () => _addReceipt(true), icon: const Icon(Icons.photo_camera_outlined, size: 19), label: const Text('Take photo'))),
              const SizedBox(width: 10),
              Expanded(child: OutlinedButton.icon(onPressed: () => _addReceipt(false), icon: const Icon(Icons.attach_file_rounded, size: 19), label: const Text('Choose file'))),
            ],
          )
        else
          Panel(
            onTap: receiptHere ? () => Platform2.open(receiptPath) : null,
            padding: const EdgeInsets.all(10),
            child: Row(
              children: [
                ClipRRect(
                  borderRadius: BorderRadius.circular(10),
                  child: receiptHere && !_receipt.toLowerCase().endsWith('.pdf')
                      ? Image.file(File(receiptPath), width: 56, height: 56, fit: BoxFit.cover)
                      : Container(width: 56, height: 56, color: p.surfaceAlt, child: Icon(Icons.receipt_long_outlined, color: p.accent)),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(receiptHere ? 'Receipt attached · tap to open' : 'Receipt on another device · downloads when syncing',
                      style: const TextStyle(fontSize: 14.5, fontWeight: FontWeight.w600)),
                ),
                IconButton(tooltip: 'Remove receipt', onPressed: () => setState(() => _receipt = ''), icon: Icon(Icons.close_rounded, color: p.muted)),
              ],
            ),
          ),
        if (!_more)
          Align(
            alignment: Alignment.centerLeft,
            child: Padding(
              padding: const EdgeInsets.only(top: 8),
              child: TextButton.icon(onPressed: () => setState(() => _more = true), icon: const Icon(Icons.add_rounded, size: 18), label: const Text('Note and tags')),
            ),
          )
        else ...[
          LabeledField(label: 'Note (optional)', controller: _note, maxLines: 2, hint: 'Anything to remember'),
          LabeledField(label: 'Tags (optional)', controller: _tags, hint: 'vacation, work trip', caps: TextCapitalization.none),
        ],
        if (_old?.importId.isNotEmpty ?? false) ...[
          const SizedBox(height: 10),
          Text('Imported from a bank statement.', style: TextStyle(fontSize: 12.5, color: p.muted)),
        ],
      ],
    );
  }

  Widget _splitRow(BuildContext context, int i, _SplitLine s, List<String> cats) {
    final p = context.pal;
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Row(
        children: [
          Expanded(
            flex: 5,
            child: PopupMenuButton<String>(
              tooltip: 'Category for part ${i + 1}',
              onSelected: (v) => setState(() => s.category = v),
              itemBuilder: (_) => [for (final c in cats) PopupMenuItem(value: c, child: Text(c))],
              child: Container(
                height: 52,
                padding: const EdgeInsets.symmetric(horizontal: 12),
                decoration: BoxDecoration(color: p.dark ? p.surfaceAlt : p.surface, borderRadius: BorderRadius.circular(14), border: Border.all(color: p.border)),
                child: Row(
                  children: [
                    Icon(s.category.isEmpty ? Icons.label_outline_rounded : categoryIcon(s.category), size: 18, color: p.accent),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(s.category.isEmpty ? 'Category' : s.category,
                          maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontWeight: FontWeight.w600, color: s.category.isEmpty ? p.muted : p.text)),
                    ),
                    Icon(Icons.arrow_drop_down_rounded, color: p.muted),
                  ],
                ),
              ),
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            flex: 4,
            child: TextField(
              controller: s.amount,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              onChanged: (_) => setState(() {}),
              decoration: InputDecoration(hintText: '0.00', prefixText: Money.currency.symbol),
            ),
          ),
          IconButton(
            tooltip: 'Remove part',
            onPressed: _splits.length <= 2
                ? null
                : () => setState(() {
                      _splits.removeAt(i).amount.dispose();
                    }),
            icon: const Icon(Icons.remove_circle_outline_rounded),
          ),
        ],
      ),
    );
  }
}
