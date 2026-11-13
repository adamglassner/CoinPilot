import 'dart:math';

import 'models.dart';

class SampleData {
  SampleData(this.transactions, this.budgets);
  final List<Txn> transactions;
  final List<Budget> budgets;
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
  return SampleData(tx, budgets);
}
