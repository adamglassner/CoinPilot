import 'dart:convert';

import 'package:coinpilot/dates.dart';
import 'package:coinpilot/models.dart';
import 'package:coinpilot/money.dart';
import 'package:coinpilot/store.dart';
import 'package:coinpilot/theme.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  setUp(() => Money.currency = currencies.first);

  group('Money', () {
    test('formats', () {
      expect(Money.format(123456), r'$1,234.56');
      expect(Money.format(-1250), '−\$12.50');
      expect(Money.format(5000, signed: true), r'+$50.00');
      expect(Money.format(0), r'$0.00');
      expect(Money.format(123456789), r'$1,234,567.89');
      expect(Money.format(123456, whole: true), r'$1,235');
      Money.currency = currencyFor('EUR');
      expect(Money.format(999), '€9.99');
      Money.currency = currencyFor('JPY');
      expect(Money.format(150000), '¥1,500');
    });

    test('parses what people type', () {
      expect(Money.parse('12.5'), 1250);
      expect(Money.parse(r'$1,234.56'), 123456);
      expect(Money.parse('0.1'), 10);
      expect(Money.parse(''), isNull);
      expect(Money.parse('abc'), isNull);
      expect(Money.parse('.'), isNull);
      expect(Money.editable(-1250), '12.50');
    });
  });

  group('Dates', () {
    final now = DateTime(2026, 11, 13, 15);
    test('labels', () {
      expect(dayLabel(DateTime(2026, 11, 13), now: now), 'Today');
      expect(dayLabel(DateTime(2026, 11, 12), now: now), 'Yesterday');
      expect(dayLabel(DateTime(2026, 11, 11), now: now), 'Wed, Nov 11');
      expect(shortDate(DateTime(2025, 3, 2), now: now), 'Mar 2, 2025');
      expect(monthLabel(DateTime(2026, 11)), 'November 2026');
      expect(daysLeftInMonth(now), 18);
      expect(addMonths(DateTime(2026, 12), 1), DateTime(2027, 1));
      expect(greeting(now), 'Good afternoon');
    });
  });

  group('Store', () {
    final nov = DateTime(2026, 11);
    Store make() {
      final s = Store.memory();
      s.saveTxn(Txn(payee: 'Kroger', category: 'Groceries', account: 'Checking', date: DateTime(2026, 11, 2), cents: -5000));
      s.saveTxn(Txn(payee: 'Kroger', category: 'Groceries', account: 'Checking', date: DateTime(2026, 11, 9), cents: -2550));
      s.saveTxn(Txn(payee: 'Chipotle', category: 'Dining out', account: 'Credit card', date: DateTime(2026, 11, 30), cents: -1200));
      s.saveTxn(Txn(payee: 'Acme', category: 'Paycheck', account: 'Checking', date: DateTime(2026, 11, 5), cents: 200000));
      s.saveTxn(Txn(payee: 'Kroger', category: 'Groceries', account: 'Checking', date: DateTime(2026, 10, 20), cents: -9000));
      s.saveBudget(Budget(category: 'Groceries', limitCents: 60000));
      return s;
    }

    test('month totals', () {
      final s = make();
      expect(s.spentIn(nov), 8750);
      expect(s.spentIn(nov, category: 'Groceries'), 7550);
      expect(s.incomeIn(nov), 200000);
      expect(s.inMonth(nov).length, 4);
      expect(s.inMonth(nov).first.payee, 'Chipotle'); // newest first
      expect(s.budgetedTotal, 60000);
      expect(s.leftToSpend(nov), 60000 - 8750);
      expect(s.unbudgeted(nov).single.key, 'Dining out');
      expect(s.weeklySpending(nov), [5000, 2550, 0, 0, 1200]);
    });

    test('edit, delete, payee memory and suggestions', () {
      final s = make();
      final t = s.inMonth(nov).first;
      s.saveTxn(Txn(id: t.id, payee: t.payee, category: t.category, account: t.account, date: t.date, cents: -1500));
      expect(s.transactions.length, 5);
      expect(s.spentIn(nov, category: 'Dining out'), 1500);
      expect(s.deleteTxn(t.id)?.payee, 'Chipotle');
      expect(s.transactions.length, 4);
      expect(s.categoryForPayee('kroger', income: false), 'Groceries');
      expect(s.categoryForPayee('kroger', income: true), isNull);
      expect(s.recentPayees(income: false), ['Kroger']);
      expect(s.suggestedBudget('Groceries', now: DateTime(2026, 11, 13)), 3000); // 90/3 = 30
      // A budget for the same category replaces the old one
      s.saveBudget(Budget(category: 'Groceries', limitCents: 70000));
      expect(s.budgets.single.limitCents, 70000);
    });

    test('saves and loads the same data (desktop field names)', () {
      final s = make()..updateSettings((x) => x.colorTheme = 'Ocean');
      final json = jsonDecode(jsonEncode(s.toJson())) as Map<String, dynamic>;
      expect((json['Transactions'] as List).first, containsPair('Payee', 'Kroger'));
      expect((json['Transactions'] as List).first['Amount'], -50.0);
      final copy = Store.memory()..fromJson(json);
      expect(copy.transactions.length, 5);
      expect(copy.spentIn(nov), 8750);
      expect(copy.budgets.single.limitCents, 60000);
      expect(copy.settings.colorTheme, 'Ocean');
    });

    test('sample data', () {
      final s = Store.memory()..loadSample(now: DateTime(2026, 11, 13));
      expect(s.transactions.length, greaterThan(60));
      expect(s.budgets.length, 8);
      expect(s.incomeIn(nov), greaterThan(0));
    });
  });

  test('every color theme builds in light and dark', () {
    for (final t in colorThemes) {
      for (final dark in [false, true]) {
        final p = Palette.build(t.name, dark);
        expect(p.dark, dark);
        expect(buildTheme(p).extension<Palette>(), isNotNull);
      }
    }
  });
}
