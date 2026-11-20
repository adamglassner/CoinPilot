import 'package:coinpilot/calculator.dart';
import 'package:coinpilot/finance.dart';
import 'package:coinpilot/forecast.dart';
import 'package:coinpilot/models.dart';
import 'package:coinpilot/money.dart';
import 'package:coinpilot/records.dart';
import 'package:coinpilot/store.dart';
import 'package:flutter_test/flutter_test.dart';

/// The forecast example WillowWallet for Windows checks too (Services/SelfTest.cs, "Forecast:" checks):
/// today is Nov 10, 2026, with $900 in checking, a $1,500 paycheck every two weeks from Nov 14, $1,200 rent on the 20th,
/// a $60 internet bill that's overdue, $300 left in a $400 Groceries budget, and a $360 car registration fund due Mar 10.
Store example() {
  final s = Store.memory();
  s.put(Account.create(name: 'Checking', startingCents: 100000, balanceDate: DateTime(2026, 11, 1)));
  s.saveTxn(Txn(payee: 'Kroger', category: 'Groceries', account: 'Checking', date: DateTime(2026, 11, 5), cents: -10000));
  s.put(IncomeSource.create(name: 'Paycheck', amountCents: 150000, repeats: IncomeSchedule.everyTwoWeeks, nextDate: DateTime(2026, 11, 14), account: 'Checking'));
  s.put(Bill.create(name: 'Rent', amountCents: 120000, nextDue: DateTime(2026, 11, 20), category: 'Housing'));
  s.put(Bill.create(name: 'Internet', amountCents: 6000, nextDue: DateTime(2026, 11, 5), category: 'Utilities'));
  s.saveBudget(Budget(category: 'Groceries', limitCents: 40000, createdAt: DateTime(2026, 11, 1)));
  s.put(Goal.create(name: 'Car registration', targetCents: 36000, targetDate: DateTime(2027, 3, 10))..isFund = true);
  return s;
}

void main() {
  setUp(() => Money.currency = currencies.first);
  final today = DateTime(2026, 11, 10);

  test('forecast: the example ends the month 2,250 dollars over, lowest on Nov 13', () {
    final f = example().forecast(today);
    expect(f.end, DateTime(2026, 11, 30));
    expect(f.startCents, 90000);
    expect(f.days.length, 21);
    // 900 + 2 × 1,500 − 1,200 − 60 − 300 groceries − 90 set aside for the car registration
    expect(f.endExpected, 225000);
    expect(f.endLow, f.endHigh);
    expect(f.verdict, ForecastVerdict.over);
    expect(f.days.first.expected, 82571); // 900 − 60 − one day of groceries (300 ÷ 21, rounded)
    expect(f.lowPoint.date, DateTime(2026, 11, 13));
    expect(f.lowPoint.expected, 78286);
    expect(f.nextPayday, DateTime(2026, 11, 14));
    expect(f.safeToSpend, 78286);
    expect([for (final d in f.drivers()) (d.label, d.cents)], [
      ('Paycheck: Paycheck', 300000),
      ('Bill: Rent', -120000),
      ('Budget: Groceries', -30000),
      ('Sinking fund: Car registration', -9000),
      ('Bill: Internet', -6000),
    ]);
  });

  test('bills charged to a credit card come out of cash when the card is paid', () {
    expect(cardPaymentDate(DateTime(2026, 11, 12), 25), DateTime(2026, 12, 25)); // statement closes 21 days before: Nov 4
    expect(cardPaymentDate(DateTime(2026, 11, 3), 25), DateTime(2026, 11, 25));
    expect(cardPaymentDate(DateTime(2026, 11, 12), 25, statementDay: 15), DateTime(2026, 11, 25));
    expect(cardPaymentDate(DateTime(2026, 11, 16), 25, statementDay: 15), DateTime(2026, 12, 25));
    expect(cardPaymentDate(DateTime(2026, 11, 12), 5, statementDay: 31), DateTime(2026, 12, 5)); // closes Nov 30
    expect(cardPaymentDate(DateTime(2027, 1, 30), 31, statementDay: 3), DateTime(2027, 2, 28));

    final s = example();
    final visa = Account.create(name: 'Visa', type: AccountTypes.creditCard, balanceDate: DateTime(2026, 11, 1))..paymentDueDay = 25;
    s.put(visa);
    s.put(Bill.create(name: 'Netflix', amountCents: 2000, nextDue: DateTime(2026, 11, 12), category: 'Subscriptions', account: 'Visa'));
    // Charged Nov 12, after the statement closed (Nov 4), so it's paid Dec 25: not this month
    expect(s.forecast(today).endExpected, 225000);
    s.put(visa.copy()..statementDay = 15);
    final f = s.forecast(today);
    expect(f.endExpected, 223000); // on the statement that closes Nov 15, paid Nov 25
    expect(f.events.firstWhere((e) => e.group == 'Card payment: Visa').date, DateTime(2026, 11, 25));
    // Without a due day the card isn't known, so the bill comes out on its own date
    s.put(visa.copy()
      ..statementDay = 0
      ..paymentDueDay = 0);
    expect(s.forecast(today).events.any((e) => e.group == 'Bill: Netflix' && e.date == DateTime(2026, 11, 12)), isTrue);

    // Marking it paid puts the charge on the card
    final t = s.markPaid(s.bills.firstWhere((b) => b.name == 'Netflix'), DateTime(2026, 11, 12));
    expect(t!.account, 'Visa');
    expect(s.balance(s.accountRecords.firstWhere((a) => a.name == 'Visa')), -2000);
  });

  test('forecast: what ifs, a target, and looking 30 days ahead', () {
    final s = example();
    final tires = s.forecast(today, whatIfs: [WhatIf('Tires', -50000, DateTime(2026, 11, 12))]);
    expect(tires.endExpected, 175000);
    expect(tires.lowPoint.expected, 28286);
    expect(s.forecast(today, skipBills: {s.bills.firstWhere((b) => b.name == 'Rent').id}).endExpected, 345000);
    expect(s.forecast(today, budgetChanges: {'Groceries': 20000}).endExpected, 245000); // $100 left instead of $300

    s.updatePrefs((p) => p.forecastTargetCents = 300000);
    final short = s.forecast(today);
    expect(short.verdict, ForecastVerdict.under);
    expect(short.difference, -75000);
    expect(short.safeToSpend, 0);

    s.updatePrefs((p) => p
      ..forecastTargetCents = 0
      ..forecastHorizon = '30');
    final f30 = s.forecast(today);
    expect(f30.end, DateTime(2026, 12, 10));
    expect(f30.events.where((e) => e.kind == 'income').length, 2); // Nov 14 and Nov 28 (Dec 12 is after Dec 10)
  });

  test('forecast: a paycheck that varies gives a range', () {
    final s = example();
    final src = s.incomeSources.first.copy()
      ..varies = true
      ..amountCents = 0;
    s.put(src);
    for (final (d, c) in [(DateTime(2026, 10, 3), 140000), (DateTime(2026, 10, 17), 160000), (DateTime(2026, 10, 31), 150000)]) {
      s.saveTxn(Txn(payee: 'Paycheck', category: 'Paycheck', account: 'Savings', date: d, cents: c));
    }
    final f = s.forecast(today);
    expect(f.endExpected, 225000); // average 1,500
    expect(f.endLow, 225000 - 20000);
    expect(f.endHigh, 225000 + 20000);
  });

  test('sinking fund: paying it starts saving for next year', () {
    final s = Store.memory();
    s.put(Goal.create(name: 'Car registration', targetCents: 36000, targetDate: DateTime(2026, 11, 15))
      ..isFund = true
      ..savedCents = 30000);
    final g = s.goals.first;
    expect(g.isFund, isTrue);
    expect(g.repeats, FundSchedule.yearly);
    s.useFund(g, DateTime(2026, 11, 16));
    final after = s.goals.first;
    expect(after.savedCents, 0);
    expect(after.targetDate, DateTime(2027, 11, 15));
    expect(after.history.last.cents, -30000);
    expect(s.monthlyNeeded(after, DateTime(2026, 11, 16)), 3000);
  });

  test('calculator', () {
    final c = CalcEngine();
    for (final k in ['1', '2']) {
      c.digit(k);
    }
    c.op('+');
    c.digit('3');
    c.digit('0');
    c.op('+');
    expect(c.display, '42');
    c.digit('8');
    c.equals();
    expect(c.display, '50');
    expect(c.history, '42 + 8 =');
    c.op('+');
    c.digit('1');
    c.digit('0');
    c.percent();
    expect(c.display, '5');
    c.equals();
    expect(c.display, '55');
    c.clear();
    c.digit('7');
    c.op('÷');
    c.digit('0');
    c.equals();
    expect(c.isError, isTrue);
    c.digit('2');
    expect(c.display, '2');
    c.op('×');
    c.digit('.');
    c.digit('5');
    c.equals();
    expect(c.display, '1');
    c.clear();
    c.digit('0');
    c.digit('.');
    c.digit('1');
    c.op('+');
    c.digit('0');
    c.digit('.');
    c.digit('2');
    c.equals();
    expect(c.display, '0.3');
  });
}
