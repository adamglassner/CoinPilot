import 'package:flutter/material.dart';

import '../dates.dart';
import '../models.dart';
import '../money.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import 'transaction_sheet.dart';

class TransactionsScreen extends StatefulWidget {
  const TransactionsScreen({super.key});

  @override
  State<TransactionsScreen> createState() => _TransactionsScreenState();
}

class _TransactionsScreenState extends State<TransactionsScreen> {
  DateTime _month = monthStart(DateTime.now());
  final _search = TextEditingController();
  int _filter = 0; // all, spending, income

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  bool _matches(Txn t) {
    if (_filter == 1 && !t.isSpending) return false;
    if (_filter == 2 && !t.isIncome) return false;
    final q = _search.text.trim().toLowerCase();
    if (q.isEmpty) return true;
    return t.payee.toLowerCase().contains(q) ||
        t.category.toLowerCase().contains(q) ||
        t.account.toLowerCase().contains(q) ||
        t.note.toLowerCase().contains(q) ||
        Money.editable(t.cents).contains(q);
  }

  void _delete(Txn t) {
    final store = StoreScope.read(context);
    store.deleteTxn(t.id);
    toast(context, 'Deleted ${t.payee}', action: 'Undo', onAction: () => store.saveTxn(t));
  }

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final all = store.inMonth(_month);
    final list = all.where(_matches).toList();
    final spent = store.spentIn(_month);
    final income = store.incomeIn(_month);

    // Group by day
    final groups = <List<Txn>>[];
    for (final t in list) {
      if (groups.isEmpty || !sameDay(groups.last.first.date, t.date)) groups.add([]);
      groups.last.add(t);
    }

    return SafeArea(
      bottom: false,
      child: CustomScrollView(
        slivers: [
          SliverPadding(
            padding: const EdgeInsets.fromLTRB(20, 18, 20, 0),
            sliver: SliverList.list(
              children: [
                ScreenHeader(title: 'Transactions', subtitle: '${all.length} in ${monthLabel(_month)}'),
                MonthSwitcher(month: _month, onChanged: (m) => setState(() => _month = m)),
                const SizedBox(height: 12),
                Row(
                  children: [
                    Expanded(child: _Mini(label: 'Spent', value: Money.format(spent, whole: true))),
                    const SizedBox(width: 10),
                    Expanded(child: _Mini(label: 'Income', value: Money.format(income, whole: true), color: p.positive)),
                    const SizedBox(width: 10),
                    Expanded(
                      child: _Mini(
                        label: 'Net',
                        value: Money.format(income - spent, signed: true, whole: true),
                        color: income - spent < 0 ? p.danger : p.positive,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 14),
                TextField(
                  controller: _search,
                  onChanged: (_) => setState(() {}),
                  textInputAction: TextInputAction.search,
                  decoration: InputDecoration(
                    hintText: 'Search payee, category or amount',
                    prefixIcon: Icon(Icons.search_rounded, color: p.muted),
                    suffixIcon: _search.text.isEmpty
                        ? null
                        : IconButton(
                            tooltip: 'Clear search',
                            icon: const Icon(Icons.close_rounded),
                            onPressed: () => setState(_search.clear),
                          ),
                    contentPadding: const EdgeInsets.symmetric(vertical: 14),
                  ),
                ),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    for (final (i, label) in const ['All', 'Spending', 'Income'].indexed)
                      ChoicePill(label: label, selected: _filter == i, onTap: () => setState(() => _filter = i)),
                  ],
                ),
                const SizedBox(height: 6),
              ],
            ),
          ),
          if (groups.isEmpty)
            SliverPadding(
              padding: const EdgeInsets.fromLTRB(20, 12, 20, 40),
              sliver: SliverToBoxAdapter(
                child: all.isEmpty
                    ? EmptyState(
                        icon: Icons.receipt_long_outlined,
                        title: 'Nothing in ${monthNames[_month.month - 1]}',
                        text: 'Tap + to add what you spend or earn. WillowWallet fills in the category next time.',
                      )
                    : EmptyState(
                        icon: Icons.search_off_rounded,
                        title: 'No matches',
                        text: 'Nothing this month matches your search or filter.',
                      ),
              ),
            )
          else
            SliverPadding(
              padding: const EdgeInsets.fromLTRB(20, 4, 20, 40),
              sliver: SliverList.builder(
                itemCount: groups.length,
                itemBuilder: (context, i) => _DayGroup(groups[i], onDelete: _delete),
              ),
            ),
        ],
      ),
    );
  }
}

class _Mini extends StatelessWidget {
  const _Mini({required this.label, required this.value, this.color});
  final String label;
  final String value;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Panel(
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: TextStyle(fontSize: 12.5, color: p.muted)),
          const SizedBox(height: 4),
          FittedBox(
            fit: BoxFit.scaleDown,
            alignment: Alignment.centerLeft,
            child: Text(value, style: display(18, color ?? p.text)),
          ),
        ],
      ),
    );
  }
}

class _DayGroup extends StatelessWidget {
  const _DayGroup(this.items, {required this.onDelete});
  final List<Txn> items;
  final ValueChanged<Txn> onDelete;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final net = items.fold<int>(0, (s, t) => s + t.cents);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(4, 16, 4, 8),
          child: Row(
            children: [
              Expanded(
                child: Text(dayLabel(items.first.date),
                    style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w700, color: p.text)),
              ),
              Text(Money.format(net, signed: true), style: TextStyle(fontSize: 13, color: p.muted)),
            ],
          ),
        ),
        Panel(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 2),
          child: Column(
            children: [
              for (final (i, t) in items.indexed) ...[
                if (i > 0) const Divider(),
                Dismissible(
                  key: ValueKey(t.id),
                  direction: DismissDirection.endToStart,
                  onDismissed: (_) => onDelete(t),
                  background: Container(
                    alignment: Alignment.centerRight,
                    padding: const EdgeInsets.only(right: 8),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text('Delete', style: TextStyle(color: p.danger, fontWeight: FontWeight.w600)),
                        const SizedBox(width: 6),
                        Icon(Icons.delete_outline_rounded, color: p.danger),
                      ],
                    ),
                  ),
                  child: TxnTile(t, onTap: () => showTransactionSheet(context, existing: t)),
                ),
              ],
            ],
          ),
        ),
      ],
    );
  }
}
