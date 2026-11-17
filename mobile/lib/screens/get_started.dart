import 'package:flutter/material.dart';

import '../dates.dart';
import '../finance.dart';
import '../kit.dart';
import '../money.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import 'accounts_screen.dart';
import 'debts_screen.dart';
import 'settings_screen.dart' show restoreBackup;

/// The first-run setup, one step per screen: accounts, income (and budgets), regular bills, loans and cards.
/// Shown the first time WillowWallet opens with no data, and from Settings → Run Get started again.
class GetStartedScreen extends StatefulWidget {
  const GetStartedScreen({super.key, this.again = false, this.onDone});

  /// Opened from Settings (a page that closes when done).
  final bool again;

  /// Called when it's finished or skipped (first run).
  final VoidCallback? onDone;

  @override
  State<GetStartedScreen> createState() => _GetStartedScreenState();
}

class _BillPick {
  _BillPick(this.name, this.category, this.icon, {this.repeats = BillSchedule.monthly});
  final String name, category, repeats;
  final IconData icon;
  bool on = false;
  final amount = TextEditingController();
  final day = TextEditingController();
}

class _GetStartedScreenState extends State<GetStartedScreen> {
  static const _steps = ['Welcome', 'Accounts', 'Income', 'Bills', 'Debts', 'Done'];
  int _step = 0;
  String? _error;

  // Income
  final _pay = TextEditingController();
  String _repeats = IncomeSchedule.everyTwoWeeks;
  bool _varies = false;
  DateTime _payday = DateTime.now().add(const Duration(days: 3));
  bool _budgets = true;

  // Bills
  final _bills = [
    _BillPick('Rent or mortgage', 'Housing', Icons.home_outlined),
    _BillPick('Electric', 'Utilities', Icons.bolt_rounded),
    _BillPick('Water', 'Utilities', Icons.water_drop_outlined),
    _BillPick('Internet', 'Utilities', Icons.wifi_rounded),
    _BillPick('Phone', 'Utilities', Icons.phone_iphone_rounded),
    _BillPick('Car insurance', 'Transportation', Icons.directions_car_outlined),
    _BillPick('Streaming', 'Subscriptions', Icons.live_tv_rounded),
    _BillPick('Gym', 'Health', Icons.fitness_center_rounded),
  ];

  @override
  void dispose() {
    _pay.dispose();
    for (final b in _bills) {
      b.amount.dispose();
      b.day.dispose();
    }
    super.dispose();
  }

  void _finish() {
    final store = StoreScope.read(context);
    store.updatePrefs((p) => p.setupDone = true);
    widget.onDone?.call();
    if (widget.again && Navigator.of(context).canPop()) Navigator.of(context).pop();
  }

  bool _saveStep() {
    final store = StoreScope.read(context);
    setState(() => _error = null);
    if (_steps[_step] == 'Income' && _pay.text.trim().isNotEmpty) {
      final cents = Money.parse(_pay.text);
      if (cents == null || cents <= 0) {
        setState(() => _error = 'Enter your take-home pay as a number, or leave it empty.');
        return false;
      }
      final exists = store.incomeSources.any((s) => s.name == 'Paycheck');
      if (!exists) {
        store.put(IncomeSource.create(name: 'Paycheck', amountCents: cents, varies: _varies, repeats: _repeats, nextDate: _payday, account: store.accountNames().first));
      }
      if (_budgets) {
        final monthly = store.expectedMonthlyIncome();
        final plan = BudgetTemplates.plan(BudgetTemplates.fiftyThirtyTwenty, monthly > 0 ? monthly : cents, store.averageSpending(), store.categoriesFor(income: false), parentOf: store.parentOf);
        store.applyBudgetPlan(plan, replace: false);
      }
    }
    if (_steps[_step] == 'Bills') {
      for (final b in _bills.where((b) => b.on)) {
        final cents = Money.parse(b.amount.text);
        final day = int.tryParse(b.day.text.trim());
        if (cents == null || cents <= 0 || day == null || day < 1 || day > 31) {
          setState(() => _error = 'Enter the amount and the day of the month for ${b.name}, or untick it.');
          return false;
        }
      }
      store.batch(() {
        for (final b in _bills.where((b) => b.on)) {
          if (store.bills.any((x) => x.name == b.name)) continue;
          final today = dayOnly(DateTime.now());
          final day = int.parse(b.day.text.trim());
          var due = DateTime(today.year, today.month, day.clamp(1, daysInMonth(today)));
          if (due.isBefore(today)) due = DateTime(today.year, today.month + 1, day.clamp(1, daysInMonth(DateTime(today.year, today.month + 1))));
          store.put(Bill.create(name: b.name, amountCents: Money.parse(b.amount.text)!, nextDue: due, repeats: b.repeats, category: b.category, account: store.accountNames().first)..dueDay = day);
          b.on = false;
        }
      });
    }
    return true;
  }

  void _next() {
    if (!_saveStep()) return;
    if (_step < _steps.length - 1) setState(() => _step++);
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final step = _steps[_step];
    return Scaffold(
      backgroundColor: p.bg,
      body: SafeArea(
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(12, 8, 12, 0),
              child: Row(
                children: [
                  if (_step > 0 && _step < _steps.length - 1)
                    IconButton(tooltip: 'Back', onPressed: () => setState(() => _step--), icon: const Icon(Icons.arrow_back_rounded))
                  else
                    const SizedBox(width: 48),
                  Expanded(
                    child: Row(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        for (var i = 1; i < _steps.length; i++)
                          AnimatedContainer(
                            duration: const Duration(milliseconds: 200),
                            width: i == _step ? 22 : 8,
                            height: 8,
                            margin: const EdgeInsets.symmetric(horizontal: 3),
                            decoration: BoxDecoration(color: i <= _step ? p.accent : p.border, borderRadius: BorderRadius.circular(99)),
                          ),
                      ],
                    ),
                  ),
                  if (_step < _steps.length - 1)
                    TextButton(onPressed: _finish, child: Text(_step == 0 ? 'Skip' : 'Skip setup', style: TextStyle(color: p.muted)))
                  else
                    const SizedBox(width: 48),
                ],
              ),
            ),
            Expanded(
              child: AnimatedSwitcher(
                duration: const Duration(milliseconds: 220),
                child: ListView(
                  key: ValueKey(step),
                  padding: const EdgeInsets.fromLTRB(22, 12, 22, 24),
                  children: switch (step) {
                    'Welcome' => _welcome(context),
                    'Accounts' => _accounts(context),
                    'Income' => _income(context),
                    'Bills' => _billsStep(context),
                    'Debts' => _debts(context),
                    _ => _done(context),
                  },
                ),
              ),
            ),
            if (step != 'Welcome')
              Padding(
                padding: const EdgeInsets.fromLTRB(22, 0, 22, 16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    if (_error != null) ...[ErrorNote(_error!), const SizedBox(height: 10)],
                    FilledButton(
                      onPressed: step == 'Done' ? _finish : _next,
                      child: Text(step == 'Done' ? 'Go to my dashboard' : 'Next'),
                    ),
                  ],
                ),
              ),
          ],
        ),
      ),
    );
  }

  Widget _title(String title, String text, IconData icon) {
    final p = context.pal;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SizedBox(height: 8),
        IconBox(icon, size: 56),
        const SizedBox(height: 16),
        Text(title, style: display(28, p.text, weight: FontWeight.w800)),
        const SizedBox(height: 8),
        Text(text, style: TextStyle(fontSize: 15, color: p.muted, height: 1.4)),
        const SizedBox(height: 8),
      ],
    );
  }

  List<Widget> _welcome(BuildContext context) {
    final p = context.pal;
    final store = StoreScope.read(context);
    Widget choice(IconData icon, String title, String text, VoidCallback onTap, {bool main = false}) => Padding(
          padding: const EdgeInsets.only(bottom: 10),
          child: Panel(
            onTap: onTap,
            color: main ? p.accentSoft : null,
            child: Row(
              children: [
                IconBox(icon, background: main ? p.surface : null),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(title, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
                      const SizedBox(height: 2),
                      Text(text, style: TextStyle(fontSize: 13.5, color: p.muted, height: 1.35)),
                    ],
                  ),
                ),
                Icon(Icons.chevron_right_rounded, color: p.muted),
              ],
            ),
          ),
        );
    return [
      const SizedBox(height: 20),
      const Center(child: CoinLogo(size: 76)),
      const SizedBox(height: 18),
      Text('Welcome to WillowWallet', textAlign: TextAlign.center, style: display(28, p.text, weight: FontWeight.w800)),
      const SizedBox(height: 8),
      Text('Budgets, bills, debts and goals, private on your phone. Let\'s set it up.',
          textAlign: TextAlign.center, style: TextStyle(fontSize: 15, color: p.muted, height: 1.4)),
      const SizedBox(height: 28),
      choice(Icons.checklist_rounded, 'Set up step by step', 'About 3 minutes: accounts, income and budgets, bills, and debts.', () => setState(() => _step = 1), main: true),
      choice(Icons.settings_backup_restore_rounded, 'Restore a backup', 'From WillowWallet for Windows or another phone.', () async {
        await restoreBackup(context);
        if (mounted && !store.isEmpty) _finish();
      }),
      choice(Icons.auto_awesome_outlined, 'Explore with sample data', 'Three months of example money. Delete it any time in Settings.', () {
        store.loadSample();
        _finish();
      }),
      const SizedBox(height: 6),
      Text('Syncing with your PC? Skip this and connect in Settings → Sync.', textAlign: TextAlign.center, style: TextStyle(fontSize: 13, color: p.muted)),
    ];
  }

  List<Widget> _accounts(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final added = store.accountRecords;
    final quick = [
      ('Checking', AccountTypes.checking, ''),
      ('Savings', AccountTypes.savings, ''),
      ('Credit card', AccountTypes.creditCard, ''),
      ('Cash', AccountTypes.cash, ''),
      for (final (n, c) in AccountTypes.paymentApps) (n, AccountTypes.paymentApp, c),
    ];
    return [
      _title('Your accounts', 'Where your money is: bank accounts, cards, cash and payment apps. Add a balance and WillowWallet keeps it up to date.', Icons.account_balance_outlined),
      const FieldLabel('Tap to add'),
      Wrap(
        spacing: 8,
        runSpacing: 8,
        children: [
          for (final (name, type, company) in quick)
            ChoicePill(
              label: name,
              icon: accountIcon(type),
              selected: added.any((a) => a.name == name),
              onTap: () => added.any((a) => a.name == name) ? null : showAccountSheet(context, name: name, type: type, institution: company),
            ),
          ChoicePill(label: 'Other…', icon: Icons.add_rounded, selected: false, onTap: () => showAccountSheet(context)),
        ],
      ),
      if (added.isNotEmpty) ...[
        const FieldLabel('Added'),
        ListPanel(
          children: [
            for (final a in added)
              RowTile(
                leading: IconBox(accountIcon(a.type), size: 38),
                title: a.name,
                subtitle: a.type,
                trailing: Money.format(store.balance(a).abs()),
                onTap: () => showAccountSheet(context, existing: a),
              ),
          ],
        ),
      ] else ...[
        const SizedBox(height: 14),
        Text('You can add these later on the Accounts screen.', style: TextStyle(fontSize: 13, color: p.muted)),
      ],
    ];
  }

  List<Widget> _income(BuildContext context) {
    return [
      _title('Income and budgets', 'Your paycheck after taxes. WillowWallet adds it on payday (or asks how much, if it changes) and can set budgets from it.', Icons.payments_outlined),
      const FieldLabel('Take-home pay'),
      MoneyField(controller: _pay, onChanged: (_) => setState(() {})),
      const SizedBox(height: 12),
      PillToggle(options: const ['Same every time', 'Changes'], selected: _varies ? 1 : 0, onChanged: (i) => setState(() => _varies = i == 1)),
      const FieldLabel('How often'),
      Choices(options: const [IncomeSchedule.everyTwoWeeks, IncomeSchedule.twiceAMonth, IncomeSchedule.weekly, IncomeSchedule.monthly], selected: _repeats, onChanged: (v) => setState(() => _repeats = v)),
      const FieldLabel('Next payday'),
      DateTile(date: _payday, onChanged: (d) => setState(() => _payday = d ?? _payday)),
      const SizedBox(height: 12),
      SwitchRow(
        title: 'Set up budgets with 50/30/20',
        subtitle: '50% for needs, 30% for wants, 20% for savings. Adjust them any time.',
        value: _budgets,
        onChanged: (v) => setState(() => _budgets = v),
      ),
    ];
  }

  List<Widget> _billsStep(BuildContext context) {
    final p = context.pal;
    final store = StoreScope.of(context);
    final existing = store.bills;
    return [
      _title('Regular bills', 'Tick the ones you pay and enter the amount and the day of the month it\'s due. Loans and cards come next.', Icons.event_note_outlined),
      for (final b in _bills)
        Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: Panel(
            padding: const EdgeInsets.fromLTRB(10, 6, 12, 6),
            child: Column(
              children: [
                Row(
                  children: [
                    Checkbox(
                      value: b.on || existing.any((x) => x.name == b.name),
                      onChanged: existing.any((x) => x.name == b.name) ? null : (v) => setState(() => b.on = v ?? false),
                    ),
                    Icon(b.icon, size: 20, color: p.accent),
                    const SizedBox(width: 10),
                    Expanded(child: Text(b.name, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600))),
                    if (existing.any((x) => x.name == b.name)) Text('Added', style: TextStyle(color: p.positive, fontWeight: FontWeight.w600)),
                  ],
                ),
                if (b.on)
                  Padding(
                    padding: const EdgeInsets.fromLTRB(8, 0, 0, 8),
                    child: Row(
                      children: [
                        Expanded(
                          flex: 3,
                          child: TextField(
                            controller: b.amount,
                            keyboardType: const TextInputType.numberWithOptions(decimal: true),
                            decoration: InputDecoration(hintText: 'Amount', prefixText: Money.currency.symbol, isDense: true),
                          ),
                        ),
                        const SizedBox(width: 10),
                        Expanded(
                          flex: 2,
                          child: TextField(
                            controller: b.day,
                            keyboardType: TextInputType.number,
                            decoration: const InputDecoration(hintText: 'Due day', isDense: true),
                          ),
                        ),
                      ],
                    ),
                  ),
              ],
            ),
          ),
        ),
    ];
  }

  List<Widget> _debts(BuildContext context) {
    final store = StoreScope.of(context);
    final debts = store.debts;
    return [
      _title('Loans and cards', 'Credit card balances and loans (student, car, medical). Each can get a monthly bill that lowers the balance when you pay it.', Icons.credit_card_rounded),
      if (debts.isNotEmpty) ...[
        ListPanel(
          children: [
            for (final d in debts)
              RowTile(
                leading: IconBox(debtIcon(d.type), size: 38),
                title: d.name,
                subtitle: '${d.type} · ${d.rate}% APR',
                trailing: Money.format(d.balanceCents),
                onTap: () => showDebtSheet(context, existing: d),
              ),
          ],
        ),
        const SizedBox(height: 10),
      ],
      OutlinedButton.icon(onPressed: () => showDebtSheet(context), icon: const Icon(Icons.add_rounded), label: Text(debts.isEmpty ? 'Add a loan or card' : 'Add another')),
      const SizedBox(height: 10),
      Text(debts.isEmpty ? 'No debts? Tap Next.' : 'Tap Next when you\'re done.', style: TextStyle(fontSize: 13, color: context.pal.muted)),
    ];
  }

  List<Widget> _done(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final lines = [
      ('${store.accountRecords.length} account${store.accountRecords.length == 1 ? '' : 's'}', Icons.account_balance_outlined),
      ('${store.incomeSources.length} income source${store.incomeSources.length == 1 ? '' : 's'}', Icons.payments_outlined),
      ('${store.budgets.length} budget${store.budgets.length == 1 ? '' : 's'}', Icons.pie_chart_outline_rounded),
      ('${store.bills.length} bill${store.bills.length == 1 ? '' : 's'}', Icons.event_note_outlined),
      ('${store.debts.length} debt${store.debts.length == 1 ? '' : 's'}', Icons.credit_card_rounded),
    ];
    return [
      const SizedBox(height: 30),
      Center(child: IconBox(Icons.check_rounded, size: 76, color: p.positive)),
      const SizedBox(height: 18),
      Text('All set', textAlign: TextAlign.center, style: display(30, p.text, weight: FontWeight.w800)),
      const SizedBox(height: 8),
      Text('Add what you spend with the + button. Everything can be changed later.', textAlign: TextAlign.center, style: TextStyle(fontSize: 15, color: p.muted, height: 1.4)),
      const SizedBox(height: 22),
      ListPanel(children: [for (final (t, i) in lines) RowTile(leading: IconBox(i, size: 36), title: t)]),
    ];
  }
}
