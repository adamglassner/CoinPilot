import 'package:flutter/material.dart';

import '../dates.dart';
import '../finance.dart';
import '../extras.dart';
import '../kit.dart';
import '../models.dart';
import '../money.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import 'transactions_screen.dart';

IconData accountIcon(String type) => switch (type) {
      AccountTypes.checking => Icons.account_balance_outlined,
      AccountTypes.savings => Icons.savings_outlined,
      AccountTypes.creditCard => Icons.credit_card_rounded,
      AccountTypes.cash => Icons.payments_outlined,
      AccountTypes.paymentApp => Icons.phone_iphone_rounded,
      AccountTypes.investment => Icons.trending_up_rounded,
      _ => Icons.wallet_outlined,
    };

class AccountsScreen extends StatefulWidget {
  const AccountsScreen({super.key});

  @override
  State<AccountsScreen> createState() => _AccountsScreenState();
}

class _AccountsScreenState extends State<AccountsScreen> {
  bool _showClosed = false;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final all = store.accountRecords;
    final open = all.where((a) => !a.isClosed).toList();
    final closed = all.where((a) => a.isClosed).toList();
    final banks = open.where((a) => a.type != AccountTypes.creditCard && a.type != AccountTypes.paymentApp).toList();
    final apps = open.where((a) => a.type == AccountTypes.paymentApp).toList();
    final cards = open.where((a) => a.type == AccountTypes.creditCard).toList();

    Widget group(String title, List<Account> list) => Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SectionTitle(title, action: Money.format(list.fold<int>(0, (s, a) => s + store.balance(a)))),
            ListPanel(children: [for (final a in list) _AccountRow(a)]),
          ],
        );

    return SubPage(
      title: 'Accounts',
      subtitle: 'Bank accounts, cards, cash and payment apps, with what\'s in each.',
      actions: [
        if (open.length >= 2)
          IconButton(tooltip: 'Transfer money', onPressed: () => showTransferSheet(context), icon: const Icon(Icons.swap_horiz_rounded)),
      ],
      fab: () => showAccountSheet(context),
      fabLabel: 'Add account',
      children: [
        HeroCard(
          label: 'Net worth',
          value: Money.format(store.netWorth),
          valueColor: store.netWorth < 0 ? const Color(0xFFFFB38A) : null,
          footer: Row(
            children: [
              _HeroBit('Accounts', Money.format(store.assetsCents, whole: true)),
              _HeroBit('Cards', Money.format(store.cardsCents.abs(), whole: true)),
              _HeroBit('Debts', Money.format(store.debtTotalCents, whole: true)),
            ],
          ),
        ),
        if (all.isEmpty) ...[
          const SizedBox(height: 14),
          EmptyState(
            icon: Icons.account_balance_outlined,
            title: 'Add your accounts',
            text: 'Checking, savings, credit cards, cash and payment apps like PayPal or Venmo. WillowWallet keeps each balance up to date from your transactions.',
            actions: [
              SizedBox(width: double.infinity, child: FilledButton.icon(onPressed: () => showAccountSheet(context), icon: const Icon(Icons.add_rounded), label: const Text('Add an account'))),
            ],
          ),
        ],
        if (banks.isNotEmpty) group('Bank and cash', banks),
        if (apps.isNotEmpty) group('Payment apps', apps),
        if (cards.isNotEmpty) group('Credit cards', cards),
        if (cards.isNotEmpty) _CardHelper(),
        if (all.isNotEmpty) _RunwayCard(),
        if (closed.isNotEmpty) ...[
          const SizedBox(height: 10),
          TextButton(
            onPressed: () => setState(() => _showClosed = !_showClosed),
            child: Text(_showClosed ? 'Hide closed accounts' : 'Show ${closed.length} closed account${closed.length == 1 ? '' : 's'}'),
          ),
          if (_showClosed) ListPanel(children: [for (final a in closed) _AccountRow(a)]),
        ],
        if (all.isNotEmpty) ...[
          const SizedBox(height: 16),
          Text('Balances are the starting balance plus every transaction since. Credit cards show what you owe.',
              textAlign: TextAlign.center, style: TextStyle(fontSize: 13, color: p.muted, height: 1.35)),
        ],
      ],
    );
  }
}

class _HeroBit extends StatelessWidget {
  const _HeroBit(this.label, this.value);
  final String label, value;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Expanded(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: TextStyle(fontSize: 12.5, color: p.heroMuted)),
          const SizedBox(height: 2),
          FittedBox(fit: BoxFit.scaleDown, alignment: Alignment.centerLeft, child: Text(value, style: display(17, p.heroText))),
        ],
      ),
    );
  }
}

class _AccountRow extends StatelessWidget {
  const _AccountRow(this.a);
  final Account a;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final bal = store.balance(a);
    final last = store.lastActivity(a);
    return RowTile(
      leading: IconBox(accountIcon(a.type), color: a.isClosed ? p.muted : null, background: a.isClosed ? p.surfaceAlt : null),
      title: a.name,
      subtitle: [if (a.institution.isNotEmpty) a.institution, a.type, if (a.isClosed) 'Closed' else if (last != null) 'Last used ${shortDate(last)}'].join(' · '),
      trailing: a.isLiability ? Money.format(bal.abs()) : Money.format(bal),
      trailingSub: a.isLiability ? (bal < 0 ? 'owed' : 'credit') : null,
      trailingColor: !a.isLiability && bal < 0 ? p.danger : null,
      onTap: () => pushPage(context, AccountDetail(a.id)),
    );
  }
}

/// One account: balance, its transactions, edit.
class AccountDetail extends StatelessWidget {
  const AccountDetail(this.id, {super.key});
  final String id;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final matches = store.accountRecords.where((a) => a.id == id).toList();
    if (matches.isEmpty) return const SubPage(title: 'Account', children: []);
    final a = matches.first;
    final bal = store.balance(a);
    final txns = store.transactions.where((t) => t.account.toLowerCase() == a.name.toLowerCase() || t.transferAccount.toLowerCase() == a.name.toLowerCase()).toList()
      ..sort((x, y) => y.date.compareTo(x.date));
    return SubPage(
      title: a.name,
      subtitle: [if (a.institution.isNotEmpty) a.institution, a.type, if (a.isClosed) 'Closed'].join(' · '),
      actions: [IconButton(tooltip: 'Edit account', onPressed: () => showAccountSheet(context, existing: a), icon: const Icon(Icons.edit_outlined))],
      children: [
        HeroCard(
          label: a.isLiability ? 'Owed on this card' : 'Balance',
          value: Money.format(a.isLiability ? bal.abs() : bal),
          footer: Text('Started at ${Money.format(a.startingCents)} on ${shortDate(a.balanceDate)} · ${txns.length} transactions',
              style: TextStyle(fontSize: 13.5, color: p.heroMuted)),
        ),
        const SectionTitle('Transactions'),
        if (txns.isEmpty)
          Text('Nothing in this account yet.', style: TextStyle(color: p.muted))
        else
          ListPanel(children: [for (final t in txns.take(60)) TxnTile(t, showDate: true, onTap: () => openTransaction(context, t))]),
      ],
    );
  }
}

// ----- Add or edit an account -----

Future<void> showAccountSheet(BuildContext context, {Account? existing, String? name, String? type, String? institution}) =>
    showSheet<void>(context, AccountSheet(existing: existing, name: name, type: type, institution: institution));

class AccountSheet extends StatefulWidget {
  const AccountSheet({super.key, this.existing, this.name, this.type, this.institution});
  final Account? existing;
  final String? name, type, institution;

  @override
  State<AccountSheet> createState() => _AccountSheetState();
}

class _AccountSheetState extends State<AccountSheet> {
  late final Account _a = widget.existing?.copy() ??
      Account.create(name: widget.name ?? '', type: widget.type ?? AccountTypes.checking, institution: widget.institution ?? '');
  late final _name = TextEditingController(text: _a.name);
  late final _institution = TextEditingController(text: _a.institution);
  late final _balance = TextEditingController(
      text: widget.existing == null ? '' : Money.editable(_a.isLiability ? -_a.startingCents : _a.startingCents));
  late final _limit = TextEditingController(text: _a.creditLimitCents > 0 ? Money.editable(_a.creditLimitCents) : '');
  late final _statement = TextEditingController(text: _a.statementDay > 0 ? '${_a.statementDay}' : '');
  late final _due = TextEditingController(text: _a.paymentDueDay > 0 ? '${_a.paymentDueDay}' : '');
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _institution.dispose();
    _balance.dispose();
    super.dispose();
  }

  void _quick(String name, String company) => setState(() {
        _name.text = name;
        _institution.text = company;
        _a.type = AccountTypes.paymentApp;
      });

  void _save() {
    final store = StoreScope.read(context);
    final name = _name.text.trim();
    if (name.isEmpty) return setState(() => _error = 'Enter a name, like "Chase Checking".');
    final clash = store.accountRecords.any((x) => x.id != _a.id && x.name.toLowerCase() == name.toLowerCase());
    if (clash) return setState(() => _error = 'You already have an account called $name.');
    final cents = readMoney(_balance, emptyIsZero: true);
    if (cents == null) return setState(() => _error = 'Enter the balance as a number.');
    final old = widget.existing?.name;
    store.batch(() {
      _a
        ..name = name
        ..institution = _institution.text.trim()
        ..startingCents = _a.isLiability ? -cents : cents;
      if (_a.isLiability) {
        _a
          ..creditLimitCents = Money.parse(_limit.text) ?? 0
          ..statementDay = (int.tryParse(_statement.text.trim()) ?? 0).clamp(0, 31)
          ..paymentDueDay = (int.tryParse(_due.text.trim()) ?? 0).clamp(0, 31);
      }
      store.put(_a);
      if (old != null && old != name) store.renameAccount(old, name);
    });
    Navigator.pop(context);
    toast(context, widget.existing == null ? 'Added $name.' : 'Saved $name.');
  }

  Future<void> _delete() async {
    final a = widget.existing!;
    final store = StoreScope.read(context);
    final used = store.transactions.any((t) => t.account == a.name || t.transferAccount == a.name);
    if (used) {
      final close = await confirm(context,
          title: 'Close ${a.name} instead?',
          message: 'It has transactions, so it can\'t be deleted. Closing it hides it and leaves its history in place.',
          confirmText: 'Close account');
      if (!close || !mounted) return;
      store.put(a.copy()..isClosed = true);
      Navigator.pop(context);
      return;
    }
    final ok = await confirm(context, title: 'Delete ${a.name}?', message: 'It has no transactions.', confirmText: 'Delete', danger: true);
    if (!ok || !mounted) return;
    store.remove(a);
    Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final isNew = widget.existing == null;
    return SheetFrame(
      title: isNew ? 'Add account' : 'Edit account',
      onDelete: isNew ? null : _delete,
      error: _error,
      primary: isNew ? 'Add account' : 'Save',
      onPrimary: _save,
      children: [
        if (isNew) ...[
          const FieldLabel('Payment apps'),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final (name, company) in AccountTypes.paymentApps)
                ChoicePill(label: name, icon: Icons.phone_iphone_rounded, selected: _name.text == name && _a.type == AccountTypes.paymentApp, onTap: () => _quick(name, company)),
            ],
          ),
        ],
        LabeledField(label: 'Name', controller: _name, hint: 'Chase Checking, Visa, Wallet…', caps: TextCapitalization.words, onChanged: (_) => setState(() {})),
        const FieldLabel('Type'),
        Choices(options: AccountTypes.all, selected: _a.type, icons: accountIcon, onChanged: (v) => setState(() => _a.type = v)),
        LabeledField(label: 'Bank or company (optional)', controller: _institution, caps: TextCapitalization.words),
        if (_a.isLiability) ...[
          const FieldLabel('Credit limit (optional)'),
          MoneyField(controller: _limit, size: 22),
          Row(children: [
            Expanded(child: LabeledField(label: 'Statement closes on day', controller: _statement, hint: '25', keyboard: TextInputType.number)),
            const SizedBox(width: 10),
            Expanded(child: LabeledField(label: 'Payment due on day', controller: _due, hint: '18', keyboard: TextInputType.number)),
          ]),
        ],
        FieldLabel(_a.isLiability ? 'What you owed on' : 'Balance on'),
        DateTile(date: _a.balanceDate, onChanged: (d) => setState(() => _a.balanceDate = d ?? _a.balanceDate)),
        const SizedBox(height: 10),
        MoneyField(controller: _balance, size: 26, hint: '0.00'),
        const SizedBox(height: 8),
        Text(
          _a.isLiability
              ? 'The card\'s balance that day. Purchases after it add to what you owe; payments lower it.'
              : 'What was in the account that day. Transactions after it are added on top.',
          style: TextStyle(fontSize: 13, color: p.muted, height: 1.35),
        ),
        if (!isNew) ...[
          const SizedBox(height: 8),
          SwitchRow(title: 'Closed', subtitle: 'Hidden from lists; its history stays.', value: _a.isClosed, onChanged: (v) => setState(() => _a.isClosed = v)),
        ],
      ],
    );
  }
}

// ----- Transfers -----

Future<void> showTransferSheet(BuildContext context, {Txn? existing}) => showSheet<void>(context, TransferSheet(existing: existing));

/// Moves money between two accounts (paying a card, saving): neither spending nor income.
class TransferSheet extends StatefulWidget {
  const TransferSheet({super.key, this.existing});
  final Txn? existing;

  @override
  State<TransferSheet> createState() => _TransferSheetState();
}

class _TransferSheetState extends State<TransferSheet> {
  late final Txn? _old = widget.existing;
  late final _amount = TextEditingController(text: _old == null ? '' : Money.editable(_old!.cents));
  late final _note = TextEditingController(text: _old?.note ?? '');
  late String _from = _old?.account ?? '';
  late String _to = _old?.transferAccount ?? '';
  late DateTime _date = _old?.date ?? dayOnly(DateTime.now());
  String? _error;

  @override
  void dispose() {
    _amount.dispose();
    _note.dispose();
    super.dispose();
  }

  void _save() {
    final cents = readMoney(_amount);
    if (cents == null || cents <= 0) return setState(() => _error = 'Enter how much to move.');
    if (_from.isEmpty || _to.isEmpty) return setState(() => _error = 'Pick both accounts.');
    if (_from == _to) return setState(() => _error = 'Pick two different accounts.');
    final store = StoreScope.read(context);
    final t = _old == null
        ? Txn(payee: 'Transfer to $_to', category: 'Transfer', account: _from, date: _date, cents: -cents, note: _note.text.trim(), extra: {'TransferAccount': _to})
        : _old!.copyWith(payee: _old!.payee.startsWith('Transfer to') ? 'Transfer to $_to' : null, account: _from, date: _date, cents: -cents, note: _note.text.trim(), extra: {'TransferAccount': _to});
    store.saveTxn(t);
    Navigator.pop(context);
    toast(context, 'Moved ${Money.format(cents)} from $_from to $_to.');
  }

  Future<void> _delete() async {
    final t = _old!;
    final ok = await confirm(context, title: 'Delete this transfer?', message: '${Money.format(t.cents.abs())} from ${t.account} to ${t.transferAccount}.', confirmText: 'Delete', danger: true);
    if (!ok || !mounted) return;
    final store = StoreScope.read(context);
    store.deleteTxn(t.id);
    Navigator.pop(context);
    toast(context, 'Deleted the transfer', action: 'Undo', onAction: () => store.saveTxn(t.touched()));
  }

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final names = store.accountNames(current: _from);
    if (_to.isNotEmpty && !names.contains(_to)) names.add(_to);
    return SheetFrame(
      title: _old == null ? 'Transfer money' : 'Edit transfer',
      subtitle: 'Paying a card or moving money to savings. It isn\'t spending or income.',
      onDelete: _old == null ? null : _delete,
      error: _error,
      primary: _old == null ? 'Transfer' : 'Save',
      onPrimary: _save,
      children: [
        const FieldLabel('Amount'),
        MoneyField(controller: _amount, autofocus: _old == null),
        const FieldLabel('From'),
        Choices(options: names, selected: _from, onChanged: (v) => setState(() => _from = v)),
        const FieldLabel('To'),
        Choices(options: names, selected: _to, onChanged: (v) => setState(() => _to = v)),
        const FieldLabel('Date'),
        DateTile(date: _date, onChanged: (d) => setState(() => _date = d ?? _date)),
        LabeledField(label: 'Note (optional)', controller: _note),
      ],
    );
  }
}

/// Credit cards: how much of each limit you're using, and paying it in full by the due date to avoid interest.
class _CardHelper extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final cards = store.creditCards(DateTime.now());
    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: Panel(
        key: const Key('card-helper'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('CREDIT CARD HELPER', style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700, letterSpacing: 0.6, color: p.muted)),
            for (final c in cards)
              Padding(
                padding: const EdgeInsets.only(top: 10),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Row(children: [
                      Expanded(child: Text(c.account.name, style: const TextStyle(fontWeight: FontWeight.w700))),
                      if (c.used != null)
                        Text('${(c.used! * 100).round()}% used',
                            style: TextStyle(fontWeight: FontWeight.w700, color: c.used! > 0.3 ? p.warn : p.positive)),
                    ]),
                    if (c.used != null) ...[
                      const SizedBox(height: 6),
                      ProgressBar(value: c.used!.clamp(0, 1).toDouble(), color: c.used! > 0.3 ? p.warn : p.accent, height: 6),
                    ],
                    const SizedBox(height: 4),
                    Text(
                      c.owedCents == 0
                          ? 'Paid off.'
                          : c.nextDue == null
                              ? 'You owe ${Money.format(c.owedCents)}. Add the due day (edit the card) for a reminder of when to pay.'
                              : 'Pay ${Money.format(c.owedCents)} by ${shortDate(c.nextDue!)} to avoid interest.',
                      style: TextStyle(fontSize: 13, color: p.muted, height: 1.3),
                    ),
                    if (c.used != null && c.used! > 0.3)
                      Text('Keeping it under 30% of the limit helps your credit score.', style: TextStyle(fontSize: 12.5, color: p.warn)),
                  ],
                ),
              ),
          ],
        ),
      ),
    );
  }
}

/// Emergency fund runway: how many months your savings would cover your essentials.
class _RunwayCard extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final r = store.runway(DateTime.now());
    final months = r.months;
    if (months == null) return const SizedBox.shrink();
    final color = months >= 3 ? p.positive : months >= 1 ? p.warn : p.danger;
    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: Panel(
        key: const Key('runway'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('EMERGENCY FUND', style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700, letterSpacing: 0.6, color: p.muted)),
            const SizedBox(height: 4),
            Text('${months.toStringAsFixed(1)} months', style: display(26, color, weight: FontWeight.w800)),
            Text('Your savings (${Money.format(r.savedCents, whole: true)}) would cover your bills and essentials (about ${Money.format(r.essentialsPerMonth, whole: true)} a month) for that long.',
                style: TextStyle(fontSize: 13, color: p.muted, height: 1.35)),
            const SizedBox(height: 8),
            ProgressBar(value: (months / 6).clamp(0, 1).toDouble(), color: color, height: 7),
            const SizedBox(height: 6),
            Text(months >= 6 ? 'Six months or more: a strong cushion.' : 'Three to six months is a common goal: ${Money.format(r.threeMonths, whole: true)} for three.',
                style: TextStyle(fontSize: 12.5, color: p.muted)),
          ],
        ),
      ),
    );
  }
}
