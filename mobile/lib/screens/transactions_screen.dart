import 'package:flutter/material.dart';

import '../dates.dart';
import '../models.dart';
import '../money.dart';
import '../search.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import '../kit.dart';
import 'import_screen.dart';
import 'transaction_sheet.dart';

/// Opens a transaction to edit it.
Future<void> openTransaction(BuildContext context, Txn t) => showTransactionSheet(context, existing: t);

class TransactionsScreen extends StatefulWidget {
  const TransactionsScreen({super.key});

  @override
  State<TransactionsScreen> createState() => _TransactionsScreenState();
}

class _TransactionsScreenState extends State<TransactionsScreen> {
  DateTime? _month;
  String _account = '';
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
    if (_filter == 3 && !t.isTransfer) return false;
    if (_account.isNotEmpty && t.account != _account && t.transferAccount != _account) return false;
    if (_search.text.trim().isEmpty) return true;
    return TxnQuery.parse(_search.text).matches(t);
  }

  void _pin(Store store) {
    final q = _search.text.trim();
    final saved = store.prefs.savedSearches;
    store.updatePrefs((p) => p.savedSearches = saved.contains(q) ? [for (final x in saved) if (x != q) x] : [...saved, q]);
    toast(context, saved.contains(q) ? 'Unpinned "$q".' : 'Pinned "$q". Tap it any time to search again.');
  }

  void _delete(Txn t) {
    final store = StoreScope.read(context);
    store.deleteTxn(t.id);
    toast(context, 'Deleted ${t.payee}', action: 'Undo', onAction: () => store.saveTxn(t.touched()));
  }

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final month = _month ?? store.budgetMonth(DateTime.now());
    final all = store.inMonth(month);
    // "dining over $50 in october" or "2025" reaches past this month
    final query = _search.text.trim().isEmpty ? null : TxnQuery.parse(_search.text);
    final wide = query?.acrossMonths ?? false;
    final list = (wide ? (store.transactions..sort((a, b) => b.date.compareTo(a.date))) : all).where(_matches).toList();
    final saved = store.prefs.savedSearches;
    final spent = store.spentIn(month);
    final income = store.incomeIn(month);
    final accounts = {for (final t in all) t.account, for (final t in all) if (t.isTransfer) t.transferAccount}.where((a) => a.isNotEmpty).toList()..sort();

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
                ScreenHeader(
                  title: 'Transactions',
                  subtitle: '${all.length} in ${store.monthTitle(month)}',
                  trailing: IconButton.filled(
                    tooltip: 'Import a bank statement',
                    onPressed: () => pushPage(context, const ImportScreen()),
                    style: IconButton.styleFrom(backgroundColor: p.accentSoft, foregroundColor: p.accent, fixedSize: const Size(46, 46)),
                    icon: const Icon(Icons.file_download_outlined),
                  ),
                ),
                MonthSwitcher(month: month, onChanged: (m) => setState(() => _month = store.budgetMonth(m))),
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
                    hintText: 'Search: "dining over \$50 in october"',
                    prefixIcon: Icon(Icons.search_rounded, color: p.muted),
                    suffixIcon: _search.text.isEmpty
                        ? null
                        : Row(mainAxisSize: MainAxisSize.min, children: [
                            IconButton(
                              tooltip: saved.contains(_search.text.trim()) ? 'Unpin this search' : 'Pin this search',
                              icon: Icon(saved.contains(_search.text.trim()) ? Icons.push_pin_rounded : Icons.push_pin_outlined, color: p.accent),
                              onPressed: () => _pin(store),
                            ),
                            IconButton(
                              tooltip: 'Clear search',
                              icon: const Icon(Icons.close_rounded),
                              onPressed: () => setState(_search.clear),
                            ),
                          ]),
                    contentPadding: const EdgeInsets.symmetric(vertical: 14),
                  ),
                ),
                if (saved.isNotEmpty) ...[
                  const SizedBox(height: 10),
                  SizedBox(
                    height: 36,
                    child: ListView(
                      scrollDirection: Axis.horizontal,
                      children: [
                        for (final q in saved)
                          Padding(
                            padding: const EdgeInsets.only(right: 8),
                            child: ActionChip(
                              avatar: Icon(Icons.push_pin_rounded, size: 15, color: p.accent),
                              label: Text(q),
                              onPressed: () => setState(() => _search.text = q),
                            ),
                          ),
                      ],
                    ),
                  ),
                ],
                if (wide) ...[
                  const SizedBox(height: 8),
                  Text('${list.length} match${list.length == 1 ? '' : 'es'} in all months · ${Money.format(list.fold<int>(0, (s, t) => s + t.cents), signed: true)}',
                      style: TextStyle(fontSize: 13, color: p.muted, fontWeight: FontWeight.w600)),
                ],
                const SizedBox(height: 12),
                SizedBox(
                  height: 40,
                  child: ListView(
                    scrollDirection: Axis.horizontal,
                    children: [
                      for (final (i, label) in const ['All', 'Spending', 'Income', 'Transfers'].indexed)
                        Padding(
                          padding: const EdgeInsets.only(right: 8),
                          child: ChoicePill(label: label, selected: _filter == i, onTap: () => setState(() => _filter = i)),
                        ),
                      if (accounts.length > 1)
                        PopupMenuButton<String>(
                          tooltip: 'Account',
                          onSelected: (v) => setState(() => _account = v),
                          itemBuilder: (_) => [
                            const PopupMenuItem(value: '', child: Text('All accounts')),
                            for (final a in accounts) PopupMenuItem(value: a, child: Text(a)),
                          ],
                          child: IgnorePointer(
                            child: ChoicePill(
                              label: _account.isEmpty ? 'All accounts' : _account,
                              icon: Icons.account_balance_wallet_outlined,
                              selected: _account.isNotEmpty,
                              onTap: () {},
                            ),
                          ),
                        ),
                    ],
                  ),
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
                        title: 'Nothing in ${store.monthName(month)}',
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
                  child: TxnTile(t, onTap: () => openTransaction(context, t)),
                ),
              ],
            ],
          ),
        ),
      ],
    );
  }
}
