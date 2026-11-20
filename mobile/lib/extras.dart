import 'dart:math';

import 'dates.dart';
import 'finance.dart';
import 'insights.dart';
import 'models.dart';
import 'money.dart';
import 'records.dart';
import 'store.dart';

// Batch 4: money owed to you, credit cards, runway, paycheck breakdown, no-spend days, taxes (mileage and
// deductions), documents, the wishlist and gift plans. WillowWallet for Windows does the same (Data/Extras.cs).

/// Categories that count as essentials for the emergency fund runway.
const essentialCategories = ['Housing', 'Rent', 'Mortgage', 'Utilities', 'Groceries', 'Transportation', 'Insurance', 'Health', 'Healthcare', 'Medical', 'Childcare', 'Phone'];

class Runway {
  const Runway(this.savedCents, this.essentialsPerMonth);
  final int savedCents, essentialsPerMonth;

  /// How many months your savings would cover your essentials (null when there's nothing to cover).
  double? get months => essentialsPerMonth <= 0 ? null : savedCents / essentialsPerMonth;

  /// Three months of essentials: a common first emergency fund target.
  int get threeMonths => essentialsPerMonth * 3;
}

class CardStatus {
  const CardStatus({required this.account, required this.owedCents, this.limitCents, this.nextDue, this.statementDay, this.bills = const [], this.billsPerMonth = 0});
  final Account account;

  /// Bills charged to this card, and what they add to it in a typical month.
  final List<Bill> bills;
  final int billsPerMonth;
  final int owedCents;
  final int? limitCents;
  final DateTime? nextDue;
  final int? statementDay;

  /// 0.0–1.0+ of the limit used (null without a limit).
  double? get used => limitCents == null || limitCents! <= 0 ? null : owedCents / limitCents!;
}

extension Extras on Store {
  // ----- Alerts from batch 4 (added to the Heads up list) -----

  List<MoneyAlert> extraAlerts(DateTime now) {
    final today = dayOnly(now);
    final list = <MoneyAlert>[];
    for (final t in transactions) {
      final r = t.returnBy;
      if (r != null && !r.isBefore(today) && r.difference(today).inDays <= 3) {
        final days = r.difference(today).inDays;
        list.add(MoneyAlert('return|${t.id}|${isoDay(r)}', 'return', 'Return ${t.payee} ${days == 0 ? 'today' : days == 1 ? 'by tomorrow' : 'by ${shortDate(r)}'}',
            'If you\'re sending it back. ${Money.format(-t.cents)} on ${shortDate(t.date)}.'));
      }
      final w = t.warrantyUntil;
      if (w != null && !w.isBefore(today) && w.difference(today).inDays <= 30) {
        list.add(MoneyAlert('warranty|${t.id}|${isoDay(w)}', 'warranty', '${t.payee} warranty ends ${shortDate(w)}', 'Check it works before then.'));
      }
      if (t.expectingBack && today.difference(dayOnly(t.date)).inDays >= 14) {
        final weeks = today.difference(dayOnly(t.date)).inDays ~/ 7;
        list.add(MoneyAlert('owed|${t.id}|$weeks', 'owed', 'Still waiting on ${Money.format(t.expectBackCents)}${t.expectFrom.isEmpty ? '' : ' from ${t.expectFrom}'}',
            'For ${t.payee} on ${shortDate(t.date)}. Mark it received under Owed to you.'));
      }
    }
    for (final d in vaultDocs) {
      final e = d.expires;
      if (e != null && !e.isBefore(today) && e.difference(today).inDays <= 30) {
        list.add(MoneyAlert('document|${d.id}|${isoDay(e)}', 'document', '${d.title} ends ${shortDate(e)}', 'Renew it, or shop around first. See Documents.'));
      }
    }
    for (final w in wishlist) {
      if (w.waiting && !w.decideOn.isAfter(today)) {
        list.add(MoneyAlert('wish|${w.id}|${isoDay(w.decideOn)}', 'wish', 'Still want ${w.name}?', 'You added it ${today.difference(w.added).inDays} days ago. Decide on your Wishlist.'));
      }
    }
    return list;
  }

  // ----- Owed to you -----

  /// Purchases waiting for money back, oldest first.
  List<Txn> expectingBack() => transactions.where((t) => t.expectingBack).toList()..sort((a, b) => a.date.compareTo(b.date));

  /// Marks money as back, optionally adding it to Transactions as income (a refund or a reimbursement).
  void markBack(Txn t, {bool addIncome = true, DateTime? on}) => batch(() {
        saveTxn(t.copyWith(extra: {'BackReceived': true}));
        if (addIncome) {
          saveTxn(Txn(
            payee: t.expectFrom.isEmpty ? t.payee : t.expectFrom,
            category: 'Refund',
            account: t.account,
            date: dayOnly(on ?? DateTime.now()),
            cents: t.expectBackCents,
            note: 'Back for ${t.payee} on ${shortDate(t.date)}',
          ));
        }
      });

  /// Each person's balance: positive, they owe you; negative, you owe them.
  Map<String, int> iouBalances() {
    final m = <String, int>{};
    for (final i in ious) {
      if (i.left != 0) m[i.person] = (m[i.person] ?? 0) + i.left;
    }
    return m;
  }

  void payIou(Iou iou, int cents, DateTime date) {
    final i = iou.copy();
    i.payments = [...i.payments, MoneyEntry(dayOnly(date), cents.abs())];
    if (i.paidBack >= i.cents.abs()) i.settled = true;
    put(i);
  }

  /// Returns and warranties still open, soonest first: (transaction, date, "Return" or "Warranty").
  List<(Txn, DateTime, String)> openReturns(DateTime now) {
    final today = dayOnly(now);
    return [
      for (final t in transactions) ...[
        if (t.returnBy != null && !t.returnBy!.isBefore(today)) (t, t.returnBy!, 'Return'),
        if (t.warrantyUntil != null && !t.warrantyUntil!.isBefore(today)) (t, t.warrantyUntil!, 'Warranty'),
      ],
    ]..sort((a, b) => a.$2.compareTo(b.$2));
  }

  // ----- Credit cards and runway -----

  List<CardStatus> creditCards(DateTime now) {
    final today = dayOnly(now);
    return [
      for (final a in accountRecords)
        if (a.isLiability && !a.isClosed)
          CardStatus(
            account: a,
            bills: billsOnCard(a),
            billsPerMonth: billsOnCard(a).fold<double>(0, (s, b) => s + Bills.monthlyCost(b.amountCents, b.repeats)).round(),
            owedCents: max(0, -balance(a)),
            limitCents: a.creditLimitCents > 0 ? a.creditLimitCents : null,
            statementDay: a.statementDay > 0 ? a.statementDay : null,
            nextDue: a.paymentDueDay > 0 ? _nextDay(today, a.paymentDueDay) : null,
          ),
    ];
  }

  bool isCardAccount(String name) => accountRecords.any((a) => a.isLiability && a.name.toLowerCase() == name.toLowerCase());

  /// What charging a bill to a card means, for the bill sheet.
  String cardHint(String name) {
    final a = accountRecords.firstWhere((a) => a.isLiability && a.name.toLowerCase() == name.toLowerCase());
    return a.paymentDueDay > 0
        ? 'Charged to $name: marking it paid adds it to what you owe on the card. The forecast takes it out of your cash when you pay the card (due on the ${ordinal(a.paymentDueDay)}).'
        : 'Charged to $name: marking it paid adds it to what you owe on the card. Add the card\'s payment due day (edit it under Accounts) so the forecast knows when the money leaves your bank.';
  }

  /// Open bills paid with (charged to) a credit card account.
  List<Bill> billsOnCard(Account card) => [for (final b in bills) if (!b.isDone && b.account.toLowerCase() == card.name.toLowerCase()) b];

  static DateTime _nextDay(DateTime today, int day) {
    final thisMonth = DateTime(today.year, today.month, min(day, daysInMonth(today)));
    if (!thisMonth.isBefore(today)) return thisMonth;
    final next = DateTime(today.year, today.month + 1);
    return DateTime(next.year, next.month, min(day, daysInMonth(next)));
  }

  /// Savings (savings accounts) against a month of essentials: bills, essential spending and debt minimums.
  Runway runway(DateTime now) {
    final saved = accountRecords.where((a) => !a.isClosed && a.type == AccountTypes.savings).fold<int>(0, (s, a) => s + max(0, balance(a)));
    final billCats = <String>{};
    var perMonth = 0.0;
    for (final b in bills.where((b) => !b.isDone)) {
      perMonth += Bills.monthlyCost(b.amountCents, b.repeats);
      billCats.add(rootOf(b.category).toLowerCase());
    }
    final month = budgetMonth(now);
    var essentials = 0;
    for (var i = 1; i <= 3; i++) {
      final m = DateTime(month.year, month.month - i, month.day);
      for (final e in spendingByCategory(m, monthEnd(m))) {
        if (essentialCategories.any((c) => c.toLowerCase() == e.key.toLowerCase()) && !billCats.contains(e.key.toLowerCase())) essentials += e.value;
      }
    }
    perMonth += essentials / 3;
    final linked = {for (final b in bills) if (b.paysDebt) b.debtId};
    perMonth += debts.where((d) => !d.isPaidOff && !linked.contains(d.id)).fold<int>(0, (s, d) => s + d.minimumCents);
    return Runway(saved, perMonth.round());
  }

  // ----- No-spend days -----

  /// The days of a budget month (up to today) with no spending.
  List<DateTime> noSpendDays(DateTime anyDay, {DateTime? now}) {
    final today = dayOnly(now ?? DateTime.now());
    final month = budgetMonth(anyDay);
    final end = monthEnd(month).isAfter(today) ? today : monthEnd(month);
    final spent = {for (final t in transactions) if (t.isSpending) isoDay(dayOnly(t.date))};
    return [
      for (var d = month; !d.isAfter(end); d = DateTime(d.year, d.month, d.day + 1))
        if (!spent.contains(isoDay(d))) d,
    ];
  }

  /// No-spend days in a row, ending yesterday or today.
  int noSpendStreak(DateTime now) {
    final spent = {for (final t in transactions) if (t.isSpending) isoDay(dayOnly(t.date))};
    var d = dayOnly(now);
    if (spent.contains(isoDay(d))) return 0;
    var n = 0;
    while (!spent.contains(isoDay(d)) && n < 400) {
      n++;
      d = DateTime(d.year, d.month, d.day - 1);
    }
    return transactions.isEmpty ? 0 : n;
  }

  // ----- Taxes: deductions and mileage -----

  double mileageRate(String kind) => prefs.mileageRates[kind] ?? MileageKinds.defaultRates[kind] ?? 0;

  /// Per kind for a tax year: (miles, deduction in cents).
  Map<String, (double, int)> mileageTotals(int year) {
    final m = <String, (double, int)>{};
    for (final t in mileage.where((t) => t.date.year == year)) {
      final (miles, cents) = m[t.kind] ?? (0.0, 0);
      m[t.kind] = (miles + t.miles, cents + (t.miles * mileageRate(t.kind)).round());
    }
    return m;
  }

  /// Tax-deductible spending by kind for a year.
  Map<String, int> deductibleTotals(int year) {
    final m = <String, int>{};
    for (final t in transactions) {
      if (t.deductible.isEmpty || t.date.year != year || !t.isSpending) continue;
      m[t.deductible] = (m[t.deductible] ?? 0) - t.cents;
    }
    return m;
  }

  // ----- Gifts -----

  /// Occasions with their plans, newest first.
  Map<String, List<GiftPlan>> giftOccasions() {
    final m = <String, List<GiftPlan>>{};
    for (final g in giftPlans) {
      (m[g.occasion] ??= []).add(g);
    }
    for (final l in m.values) {
      l.sort((a, b) => a.person.toLowerCase().compareTo(b.person.toLowerCase()));
    }
    return m;
  }
}
