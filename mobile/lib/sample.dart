import 'dart:math';

import 'models.dart';
import 'records.dart';

class SampleData {
  SampleData(this.transactions, this.budgets, this.records);
  final List<Txn> transactions;
  final List<Budget> budgets;
  /// Accounts, bills, debts, goals and income.
  final List<Rec> records;
}

/// About three months of an ordinary household's money, for trying WillowWallet out (and screenshots).
SampleData sampleData(DateTime now) {
  final rng = Random(42);
  final today = DateTime(now.year, now.month, now.day);
  final start = DateTime(today.year, today.month - 2, 1);
  final tx = <Txn>[];
  int r(double lo, double hi) => ((lo + rng.nextDouble() * (hi - lo)) * 100).round();
  void t(DateTime d, String payee, String cat, String acct, int cents) {
    if (d.isAfter(today)) return;
    tx.add(Txn(payee: payee, category: cat, account: acct, date: d, cents: cents,
        createdAt: d.add(Duration(hours: 9 + rng.nextInt(10))), updatedAt: d));
  }

  const grocers = ['Kroger', 'Aldi', 'Fresh Thyme Market', "Trader Joe's"];
  const restaurants = ['Chipotle', 'Taco Bros', 'Panera Bread', 'Sushi Kame', 'Olive Garden'];
  for (var d = start; !d.isAfter(today); d = DateTime(d.year, d.month, d.day + 1)) {
    final dow = d.weekday;
    if (d.day == 1) t(d, 'Maple Court Apartments', 'Housing', 'Checking', -145000);
    if (d.day == 1 || d.day == 15) t(d, 'Paycheck · Acme Corp', 'Paycheck', 'Checking', 218640);
    if (dow == DateTime.tuesday || dow == DateTime.saturday) {
      t(d, grocers[rng.nextInt(grocers.length)], 'Groceries', 'Credit card', -r(38, 118));
    }
    if ((dow == DateTime.monday || dow == DateTime.wednesday || dow == DateTime.friday) && rng.nextDouble() < 0.7) {
      t(d, 'Starbucks', 'Dining out', 'Credit card', -r(4.5, 7.8));
    }
    if ((dow == DateTime.friday || dow == DateTime.sunday) && rng.nextDouble() < 0.6) {
      t(d, restaurants[rng.nextInt(restaurants.length)], 'Dining out', 'Credit card', -r(14, 58));
    }
    if (dow == DateTime.wednesday) t(d, 'Shell', 'Transportation', 'Credit card', -r(34, 49));
    if (d.day == 18) t(d, 'Duke Energy', 'Utilities', 'Checking', -r(108, 146));
    if (d.day == 22) t(d, 'Comcast Xfinity', 'Utilities', 'Checking', -7999);
    if (d.day == 9) t(d, 'Netflix', 'Subscriptions', 'Credit card', -1549);
    if (d.day == 14) t(d, 'Spotify', 'Subscriptions', 'Credit card', -1099);
    if (d.day == 21) t(d, 'Hulu', 'Subscriptions', 'Credit card', -1799);
    if (d.day == 26) t(d, 'DISNEY PLUS 8842', 'Subscriptions', 'Credit card', d.isBefore(DateTime(today.year, today.month - 1, 1)) ? -1099 : -1399);
    if (d.day == 3) t(d, 'Navient Student Loan', 'Debt payments', 'Checking', -30000);
    if (d.day == 20) {
      tx.add(Txn(payee: 'Monthly savings', category: 'Transfer', account: 'Checking', date: d, cents: -40000,
          createdAt: d, updatedAt: d, extra: {'TransferAccount': 'High-Yield Savings'}));
    }
    if (d.day == 5) t(d, 'Planet Fitness', 'Health', 'Credit card', -2499);
    if (rng.nextDouble() < 0.08) {
      t(d, const ['Target', 'Amazon', 'Best Buy', 'Home Depot'][rng.nextInt(4)], 'Shopping', 'Credit card', -r(18, 120));
    }
    if (rng.nextDouble() < 0.05) {
      t(d, const ['AMC Theatres', 'Steam', 'Topgolf'][rng.nextInt(3)], 'Entertainment', 'Credit card', -r(16, 60));
    }
  }
  t(today, 'Fresh Thyme Market', 'Groceries', 'Credit card', -8642);
  t(today.subtract(const Duration(days: 3)), 'Etsy refund', 'Refund', 'Credit card', 3250);

  final budgets = [
    Budget(category: 'Housing', limitCents: 145000),
    Budget(category: 'Groceries', limitCents: 65000),
    Budget(category: 'Dining out', limitCents: 30000),
    Budget(category: 'Transportation', limitCents: 20000),
    Budget(category: 'Utilities', limitCents: 30000),
    Budget(category: 'Shopping', limitCents: 18000),
    Budget(category: 'Subscriptions', limitCents: 4000),
    Budget(category: 'Entertainment', limitCents: 10000),
  ];
  DateTime next(int day) {
    final d = DateTime(today.year, today.month, day);
    return d.isAfter(today) ? d : DateTime(today.year, today.month + 1, day);
  }

  final loan = Debt.create(name: 'Student loan', type: 'Student loan', balanceCents: 1842000, rate: 5.5, minimumCents: 30000)
    ..originalCents = 2400000;
  final card = Debt.create(name: 'Visa Signature', type: DebtTypes.creditCard, balanceCents: 214000, rate: 22.99, minimumCents: 6500);
  final records = <Rec>[
    Account.create(name: 'Checking', type: AccountTypes.checking, institution: 'Chase', startingCents: 284512, balanceDate: start.subtract(const Duration(days: 1))),
    Account.create(name: 'High-Yield Savings', type: AccountTypes.savings, institution: 'Ally', startingCents: 640000, balanceDate: start.subtract(const Duration(days: 1))),
    Account.create(name: 'Credit card', type: AccountTypes.creditCard, institution: 'Chase', startingCents: -38240, balanceDate: start.subtract(const Duration(days: 1))),
    Account.create(name: 'Venmo', type: AccountTypes.paymentApp, institution: 'PayPal', startingCents: 4200, balanceDate: start),
    loan,
    card,
    Bill.create(name: 'Rent', amountCents: 145000, nextDue: next(1), category: 'Housing', account: 'Checking'),
    Bill.create(name: 'Duke Energy', amountCents: 13000, nextDue: next(18), category: 'Utilities', account: 'Checking'),
    Bill.create(name: 'Comcast Xfinity', amountCents: 7999, nextDue: next(22), category: 'Utilities', account: 'Checking', autoPay: true),
    Bill.create(name: 'Netflix', amountCents: 1549, nextDue: next(9), category: 'Subscriptions', account: 'Credit card', autoPay: true),
    Bill.create(name: 'Amazon Prime', amountCents: 13900, nextDue: today.add(const Duration(days: 19)), repeats: BillSchedule.yearly, category: 'Subscriptions', account: 'Credit card', autoPay: true),
    Bill.create(name: 'Car insurance', amountCents: 61800, nextDue: today.add(const Duration(days: 5)), repeats: BillSchedule.quarterly, category: 'Transportation', account: 'Checking'),
    Bill.create(name: 'Water & sewer', amountCents: 4620, nextDue: today.subtract(const Duration(days: 2)), category: 'Utilities', account: 'Checking'),
    Bill.create(name: 'Student loan', amountCents: 30000, nextDue: next(3), category: 'Debt payments', account: 'Checking', debtId: loan.id),
    Goal.create(name: 'Emergency fund', targetCents: 1200000, linkedAccount: 'High-Yield Savings')..note = 'Six months of expenses',
    Goal.create(name: 'Hawaii trip', targetCents: 450000, targetDate: DateTime(today.year + 1, 6, 1))..savedCents = 182000,
    IncomeSource.create(name: 'Paycheck · Acme Corp', amountCents: 218640, repeats: IncomeSchedule.twiceAMonth, nextDate: next(today.day < 15 ? 15 : 1), account: 'Checking')
      ..day1 = 1
      ..day2 = 15,
    IncomeSource.create(name: 'DoorDash', varies: true, repeats: IncomeSchedule.weekly, nextDate: today.add(const Duration(days: 3)), category: 'Side income', account: 'Checking'),
  ];
  return SampleData(tx, budgets, records);
}
