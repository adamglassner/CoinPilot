import 'package:coinpilot/extras.dart';
import 'package:coinpilot/insights.dart';
import 'package:coinpilot/models.dart';
import 'package:coinpilot/money.dart';
import 'package:coinpilot/records.dart';
import 'package:coinpilot/search.dart';
import 'package:coinpilot/store.dart';
import 'package:flutter_test/flutter_test.dart';

/// Batch 4 examples. WillowWallet for Windows checks the same ones (Services/SelfTest.cs, "Owed:", "Taxes:"… checks).
void main() {
  setUp(() => Money.currency = currencies.first);
  final today = DateTime(2026, 11, 20);
  Txn t(String payee, int cents, DateTime d, {Map<String, dynamic> extra = const {}, String cat = 'Shopping'}) =>
      Txn(payee: payee, category: cat, account: 'Checking', date: d, cents: cents, extra: extra);

  test('owed to you: money back, returns, warranties and IOUs', () {
    final s = Store.memory();
    s.saveTxns([
      t('Amazon', -4500, DateTime(2026, 11, 1), extra: {'ExpectBack': 45, 'ExpectFrom': 'Amazon'}),
      t('Best Buy', -12999, DateTime(2026, 11, 15), extra: {'ReturnBy': '2026-11-22', 'WarrantyUntil': '2026-12-10'}),
    ]);
    final a = s.alerts(today);
    expect([for (final x in a) x.kind], ['return', 'owed', 'warranty']);
    expect(a[0].title, 'Return Best Buy by Nov 22');
    expect(a[1].title, 'Still waiting on \$45.00 from Amazon');
    expect(s.openReturns(today).map((r) => r.$3).toList(), ['Return', 'Warranty']);

    s.markBack(s.expectingBack().single, on: today);
    expect(s.expectingBack(), isEmpty);
    expect(s.transactions.where((x) => x.category == 'Refund').single.cents, 4500);

    s.put(Iou.create(person: 'Sam', cents: 2000, date: today));
    s.put(Iou.create(person: 'Sam', cents: -500, date: today));
    s.put(Iou.create(person: 'Jo', cents: -1000, date: today));
    expect(s.iouBalances(), {'Sam': 1500, 'Jo': -1000});
    s.payIou(s.ious.firstWhere((i) => i.cents == 2000), 2000, today);
    expect(s.iouBalances(), {'Sam': -500, 'Jo': -1000});
  });

  test('credit cards, runway and no-spend days', () {
    final s = Store.memory();
    s.put(Account.create(name: 'Visa', type: AccountTypes.creditCard, startingCents: -150000, balanceDate: DateTime(2026, 11, 1))
      ..creditLimitCents = 500000
      ..paymentDueDay = 18);
    s.put(Account.create(name: 'Savings', type: AccountTypes.savings, startingCents: 600000, balanceDate: DateTime(2026, 11, 1)));
    s.put(Bill.create(name: 'Rent', amountCents: 150000, nextDue: DateTime(2026, 12, 1), category: 'Housing'));
    final card = s.creditCards(today).single;
    expect(card.owedCents, 150000);
    expect(card.used, 0.3);
    expect(card.nextDue, DateTime(2026, 12, 18));
    final r = s.runway(today);
    expect(r.essentialsPerMonth, 150000);
    expect(r.months, 4.0);

    s.saveTxns([t('Cafe', -500, DateTime(2026, 11, 18)), t('Pay', 100000, DateTime(2026, 11, 19), cat: 'Paycheck')]);
    final free = s.noSpendDays(today, now: today);
    expect(free.length, 19); // Nov 1–20 except the 18th
    expect(s.noSpendStreak(today), 2); // the 19th and 20th
  });

  test('taxes: deductions and mileage', () {
    final s = Store.memory();
    s.saveTxns([t('Red Cross', -10000, DateTime(2026, 3, 1), extra: {'Deductible': 'Charity'}), t('Dentist', -25000, DateTime(2026, 4, 1), extra: {'Deductible': 'Medical'})]);
    s.put(MileageTrip.create(date: DateTime(2026, 5, 1), miles: 100, purpose: 'Client', kind: 'Business'));
    expect(s.deductibleTotals(2026), {'Charity': 10000, 'Medical': 25000});
    expect(s.mileageTotals(2026)['Business'], (100.0, 7000));
    s.updatePrefs((p) => p.mileageRates = {'Business': 72.5});
    expect(s.mileageTotals(2026)['Business']!.$2, 7250);
  });

  test('documents, wishlist and gifts', () {
    final s = Store.memory();
    s.put(VaultDoc.create(title: 'Car insurance', kind: 'Insurance', fileName: 'x.pdf', expires: DateTime(2026, 12, 1)));
    s.put(WishItem.create(name: 'Headphones', cents: 19900, added: DateTime(2026, 10, 1)));
    expect([for (final a in s.alerts(today)) a.kind], ['document', 'wish']);
    final g = GiftPlan.create(occasion: 'Holidays 2026', person: 'Mom', budgetCents: 10000)..items = [const GiftItem('Scarf', 4000, true), const GiftItem('Book', 2500, false)];
    s.put(g);
    expect(s.giftOccasions()['Holidays 2026']!.single.boughtCents, 4000);
    expect(s.giftPlans.single.plannedCents, 6500);
  });

  test('paycheck breakdown', () {
    final i = IncomeSource.create(name: 'Paycheck', amountCents: 200000, nextDate: today)
      ..grossCents = 280000
      ..deductions = [('Federal tax', 40000), ('401(k)', 40000)];
    expect(i.hasBreakdown, isTrue);
    expect(i.deductions.fold<int>(0, (s, d) => s + d.$2), 80000);
  });

  test('search in plain words', () {
    final dining = t('Taco Bros', -6200, DateTime(2026, 10, 3), cat: 'Dining out');
    final small = t('Taco Bros', -1200, DateTime(2026, 10, 3), cat: 'Dining out');
    final nov = t('Taco Bros', -6200, DateTime(2026, 11, 3), cat: 'Dining out');
    final q = TxnQuery.parse('dining over \$50 in october');
    expect(q.minCents, 5000);
    expect(q.month, 10);
    expect([dining, small, nov].where(q.matches).toList(), [dining]);
    expect(TxnQuery.parse('taco under 20').matches(small), isTrue);
    expect(TxnQuery.parse('2025').matches(dining), isFalse);
    expect(TxnQuery.parse('tacos').acrossMonths, isFalse);
  });

  test('change history and undo', () {
    final s = Store.memory();
    final b = Bill.create(name: 'Rent', amountCents: 150000, nextDue: today, category: 'Housing');
    s.put(b);
    s.put(s.bills.first.copy()..amountCents = 160000);
    expect([for (final h in s.history) h['Action']], ['Added', 'Changed']);
    s.undo(s.history.last);
    expect(s.bills.first.amountCents, 150000);
    s.undo(s.history.first);
    expect(s.bills, isEmpty);
    s.saveTxn(t('Cafe', -500, today));
    final id = s.transactions.single.id;
    s.deleteTxn(id);
    s.undo(s.history.last);
    expect(s.transactions.single.payee, 'Cafe');
  });
}
