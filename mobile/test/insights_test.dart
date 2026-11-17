import 'package:coinpilot/insights.dart';
import 'package:coinpilot/models.dart';
import 'package:coinpilot/money.dart';
import 'package:coinpilot/records.dart';
import 'package:coinpilot/store.dart';
import 'package:flutter_test/flutter_test.dart';

/// The alerts example WillowWallet for Windows checks too (Services/SelfTest.cs, "Alerts:" checks). Today is Nov 20, 2026.
Store example() {
  final s = Store.memory();
  s.put(Account.create(name: 'Checking', startingCents: 500000, balanceDate: DateTime(2026, 8, 1)));
  Txn t(String payee, String cat, DateTime d, int cents) => Txn(payee: payee, category: cat, account: 'Checking', date: d, cents: cents);
  s.saveBudget(Budget(category: 'Dining out', limitCents: 25000, createdAt: DateTime(2026, 11, 1)));
  s.saveBudget(Budget(category: 'Groceries', limitCents: 40000, createdAt: DateTime(2026, 11, 1)));
  s.saveTxns([
    t('Taco Bros', 'Dining out', DateTime(2026, 11, 8), -21000), // 84% of $250
    t('Kroger', 'Groceries', DateTime(2026, 11, 9), -45000), // over by $50
    t('NETFLIX.COM 1234', 'Subscriptions', DateTime(2026, 10, 5), -1799),
    t('NETFLIX.COM 5678', 'Subscriptions', DateTime(2026, 11, 5), -1799),
    for (final m in [8, 9, 10]) t('Duke Energy', 'Utilities', DateTime(2026, m, 18), -13000),
    t('Duke Energy', 'Utilities', DateTime(2026, 11, 18), -18200), // 40% more than usual
    t('Kroger', 'Groceries', DateTime(2025, 11, 10), -30000), // a year ago
  ]);
  s.put(Bill.create(name: 'Netflix', amountCents: 1549, nextDue: DateTime(2026, 12, 5), category: 'Subscriptions'));
  return s;
}

void main() {
  setUp(() => Money.currency = currencies.first);
  final today = DateTime(2026, 11, 20);

  test('alerts: over budget, a budget at 84%, a bill that went up and an unusual charge', () {
    final s = example();
    final a = s.alerts(today);
    expect([for (final x in a) x.kind], ['over', 'budget', 'bill-price', 'unusual']);
    expect(a[0].title, 'Over budget: Groceries');
    expect(a[0].body, '\$450 of \$400, \$50 over with 11 days left.');
    expect(a[1].title, 'Dining out: 84% used');
    expect(a[1].body, '\$40 left for 11 days.');
    expect(a[2].title, 'Netflix went up');
    expect(a[2].newCents, 1799);
    expect(a[3].title, 'Duke Energy: \$182.00');
    expect(a[3].body, 'About 40% more than usual (\$130.00).');

    s.dismissAlert(a[1]);
    expect(s.alerts(today).length, 3);
    s.applyBillPrice(a[2]);
    expect(s.bills.first.amountCents, 1799);
    expect(s.alerts(today).any((x) => x.kind == 'bill-price'), isFalse);
    s.updatePrefs((p) => p.alertPercent = 90);
    expect(s.alerts(today, includeDismissed: true).any((x) => x.kind == 'budget'), isFalse);
    s.updatePrefs((p) => p.alertsOn = false);
    expect(s.alerts(today), isEmpty);
  });

  test('alerts: a forecast dip', () {
    final s = Store.memory();
    s.put(Account.create(name: 'Checking', startingCents: 50000, balanceDate: DateTime(2026, 11, 1)));
    s.put(Bill.create(name: 'Rent', amountCents: 120000, nextDue: DateTime(2026, 11, 25), category: 'Housing'));
    s.put(IncomeSource.create(name: 'Paycheck', amountCents: 150000, repeats: IncomeSchedule.monthly, nextDate: DateTime(2026, 11, 28)));
    final a = s.alerts(today);
    expect(a.single.kind, 'dip');
    expect(a.single.title, 'Your balance may dip to −\$700');
    expect(a.single.body, 'On Nov 25, before payday. See Forecast.');
  });

  test('paycheck plan: bills before the next paycheck, budget money for those days, and what is left', () {
    final s = example();
    s.put(Bill.create(name: 'Rent', amountCents: 120000, nextDue: DateTime(2026, 11, 25), category: 'Housing'));
    final pay = IncomeSource.create(name: 'Paycheck', amountCents: 150000, repeats: IncomeSchedule.everyTwoWeeks, nextDate: today);
    s.put(pay);
    final plan = s.planPaycheck(pay, today);
    expect(plan.next, DateTime(2026, 12, 4));
    expect([for (final l in plan.lines.where((l) => l.kind == 'bill')) l.label], ['Rent']); // Netflix is due Dec 5
    // Dining out: $40 left for Nov 20–30, then 3 of December's 31 days of $250 and $400
    expect(plan.total('budget'), 4000 + 2419 + 3871);
    expect(plan.leftoverCents, 150000 - 120000 - 10290);
  });

  test('this year vs last', () {
    final y = example().yearOverYear(DateTime(2026, 11, 1), today);
    expect(y.hasLastYear, isTrue);
    expect(y.spentBefore, 30000);
    expect(y.categories.firstWhere((c) => c.$1 == 'Groceries'), ('Groceries', 45000, 30000));
    expect(YearOverYear.change(45000, 30000), 50);
    expect(YearOverYear.change(100, 0), isNull);
  });
}
