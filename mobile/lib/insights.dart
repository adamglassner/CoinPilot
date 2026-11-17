import 'dart:math';

import 'dates.dart';
import 'finance.dart';
import 'forecast.dart';
import 'models.dart';
import 'money.dart';
import 'records.dart';
import 'store.dart';

// Insights: spending alerts, the paycheck planner and this year compared with last.
//
// WillowWallet for Windows does the same (Data/Insights.cs); the tests on both sides use the same examples.

/// Something worth knowing now: a budget running low, a bill whose price changed, an unusual charge, a coming dip.
class MoneyAlert {
  const MoneyAlert(this.key, this.kind, this.title, this.body, {this.billId, this.newCents});

  /// Stays the same while it's the same news (so it's shown and notified once, and can be dismissed).
  final String key;

  /// 'over', 'budget', 'bill-price', 'unusual' or 'dip'.
  final String kind;
  final String title, body;

  /// A bill whose price changed, and its new price ("Update the bill").
  final String? billId;
  final int? newCents;
}

extension Insights on Store {
  /// Alerts for today, most urgent first (none when alerts are off; dismissed ones left out).
  List<MoneyAlert> alerts(DateTime now, {bool includeDismissed = false}) {
    final p = prefs;
    if (!p.alertsOn) return [];
    final today = dayOnly(now);
    final month = budgetMonth(today);
    final mKey = isoDay(month).substring(0, 7);
    final daysLeft = monthEnd(month).difference(today).inDays + 1;
    final list = <MoneyAlert>[];

    // 1. Budgets: over, or past the warning level with days to go
    for (final b in budgets) {
      final available = availableFor(b, month);
      if (available <= 0) continue;
      final spent = spentIn(month, category: b.category);
      final pct = spent * 100 / available;
      if (spent > available) {
        list.add(MoneyAlert('over|${b.category}|$mKey', 'over', 'Over budget: ${b.category}',
            '${Money.format(spent, whole: true)} of ${Money.format(available, whole: true)}, ${Money.format(spent - available, whole: true)} over with $daysLeft day${daysLeft == 1 ? '' : 's'} left.'));
      } else if (pct >= p.alertPercent && daysLeft > 1) {
        list.add(MoneyAlert('budget|${b.category}|$mKey', 'budget', '${b.category}: ${pct.floor()}% used',
            '${Money.format(available - spent, whole: true)} left for $daysLeft days.'));
      }
    }

    // 2. Bills whose price changed: the last two charges agree with each other but not with the bill
    for (final b in bills) {
      if (b.isDone || b.amountCents <= 0) continue;
      final name = Subscriptions.normalize(b.name);
      if (name.length < 3) continue;
      final charges = transactions
          .where((t) => t.isSpending && !t.isSplit && Subscriptions.normalize(t.payee).contains(name) && t.date.isAfter(today.subtract(const Duration(days: 100))))
          .toList()
        ..sort((a, c) => c.date.compareTo(a.date));
      if (charges.length < 2) continue;
      final a = -charges[0].cents, c = -charges[1].cents;
      if ((a - c).abs() > max(100, a ~/ 100)) continue; // the two latest charges agree (within $1 or 1%)
      final diff = a - b.amountCents;
      if (diff.abs() < max(100, b.amountCents ~/ 50)) continue; // and differ from the bill by more than $1 and 2%
      list.add(MoneyAlert('bill-price|${b.id}|$a', 'bill-price', diff > 0 ? '${b.name} went up' : '${b.name} went down',
          'The last two charges were ${Money.format(a)}, but the bill says ${Money.format(b.amountCents)}.',
          billId: b.id, newCents: a));
    }

    // 3. Unusual charges in the last week: much more than this payee usually costs
    for (final t in transactions) {
      if (!t.isSpending || today.difference(dayOnly(t.date)).inDays > 7 || t.date.isAfter(today.add(const Duration(days: 1)))) continue;
      final key = Subscriptions.normalize(t.payee);
      if (key.isEmpty) continue;
      final before = [
        for (final x in transactions)
          if (x.isSpending && x.id != t.id && x.date.isBefore(t.date) && Subscriptions.normalize(x.payee) == key) -x.cents,
      ];
      if (before.length < 3) continue;
      final usual = (before.reduce((a, b) => a + b) / before.length).round();
      final amount = -t.cents;
      if (amount < usual * 1.4 || amount - usual < 2000) continue;
      final more = ((amount / usual - 1) * 100).round();
      list.add(MoneyAlert('unusual|${t.id}', 'unusual', '${t.payee}: ${Money.format(amount)}',
          'About $more% more than usual (${Money.format(usual)}).'));
    }

    // 4. The forecast dips below your cushion before the end of the month
    final f = forecast(today, horizon: 'Month');
    final low = f.lowPoint;
    if (low.expected < max(0, f.targetCents) && low.date.isBefore(f.end)) {
      list.add(MoneyAlert('dip|$mKey', 'dip', 'Your balance may dip to ${Money.format(low.expected, whole: true)}',
          'On ${shortDate(low.date)}${f.nextPayday != null && low.date.isBefore(f.nextPayday!) ? ', before payday' : ''}. See Forecast.'));
    }

    const order = {'over': 0, 'dip': 1, 'budget': 2, 'bill-price': 3, 'unusual': 4};
    list.sort((a, b) => order[a.kind]!.compareTo(order[b.kind]!));
    if (includeDismissed) return list;
    final gone = p.dismissedAlerts.toSet();
    return [for (final a in list) if (!gone.contains(a.key)) a];
  }

  void dismissAlert(MoneyAlert a) => updatePrefs((p) {
        // Keep the list short: only this month's and recent keys matter
        final keep = p.dismissedAlerts.length > 200 ? p.dismissedAlerts.sublist(p.dismissedAlerts.length - 150) : p.dismissedAlerts;
        p.dismissedAlerts = [...keep, a.key];
      });

  /// "Update the bill" from a price-change alert.
  void applyBillPrice(MoneyAlert a) {
    final b = bills.where((x) => x.id == a.billId).firstOrNull;
    if (b == null || a.newCents == null) return;
    put(b.copy()..amountCents = a.newCents!);
  }

  // ----- Paycheck planner -----

  /// How a paycheck could be split until the next one: bills due, budget money for those days, debt payments and
  /// savings, and what's left over.
  PaycheckPlan planPaycheck(IncomeSource s, DateTime payday, {int? amountCents}) {
    final from = dayOnly(payday);
    final next = s.repeats == IncomeSchedule.once ? DateTime(from.year, from.month + 1, from.day) : Paydays.next(from, s.repeats, s.day1, s.day2);
    final until = next.subtract(const Duration(days: 1));
    final amount = amountCents ?? (s.amountCents > 0 && !s.varies ? s.amountCents : typicalIncome(s));
    final lines = <PlanLine>[];

    // Bills due before the next paycheck (overdue ones too)
    for (final b in bills) {
      if (b.isDone) continue;
      var d = dayOnly(b.nextDue);
      for (var i = 0; i < 60 && !d.isAfter(until); i++) {
        if (!d.isBefore(from) || i == 0) lines.add(PlanLine('bill', b.name, b.amountCents, d.isBefore(from) ? from : d, category: rootOf(b.category).toLowerCase()));
        if (b.repeats == BillSchedule.once) break;
        d = Bills.next(d, b.repeats, b.dueDay);
      }
    }

    // Budget money for these days: what's left in each budget, shared out over the days left in each budget month
    final days = until.difference(from).inDays + 1;
    final today = dayOnly(DateTime.now());
    var budgetCents = 0;
    for (var d = from; !d.isAfter(until);) {
      final m = budgetMonth(d);
      final mEnd = monthEnd(m);
      final segEnd = mEnd.isBefore(until) ? mEnd : until;
      final segDays = segEnd.difference(d).inDays + 1;
      final left = mEnd.difference(d).inDays + 1;
      final billsHere = <String, int>{};
      for (final l in lines) {
        if (l.kind == 'bill' && inBudgetMonth(l.date, m)) billsHere[l.category] = (billsHere[l.category] ?? 0) + l.cents;
      }
      for (final b in budgets) {
        final current = !m.isAfter(today);
        final base = current ? availableFor(b, m) - spentIn(m, category: b.category) : limitFor(b, m);
        final remaining = max(0, base - (billsHere[b.category.toLowerCase()] ?? 0));
        budgetCents += (remaining * segDays / left).round();
      }
      d = DateTime(segEnd.year, segEnd.month, segEnd.day + 1);
    }
    if (budgetCents > 0) lines.add(PlanLine('budget', 'Budgets for $days days', budgetCents, from));

    // Debt minimums that aren't bills, and goal savings, for the share of a month these days are
    final linked = {for (final b in bills) if (b.paysDebt) b.debtId};
    final share = days / 30.44;
    for (final d in debts.where((d) => !d.isPaidOff && d.minimumCents > 0 && !linked.contains(d.id))) {
      lines.add(PlanLine('debt', d.name, (d.minimumCents * share).round(), from));
    }
    for (final g in goals) {
      final need = monthlyNeeded(g, from);
      if (need != null && need > 0) lines.add(PlanLine('savings', g.name, (need * share).round(), from));
    }
    final planned = lines.fold<int>(0, (x, l) => x + l.cents);
    return PaycheckPlan(source: s, payday: from, next: next, amountCents: amount, lines: lines, leftoverCents: amount - planned);
  }

  // ----- Monthly report -----

  int netWorthAt(DateTime date) =>
      accountRecords.fold<int>(0, (s, a) => s + balanceAt(a, date)) - debts.fold<int>(0, (s, d) => s + AccountMath.debtAt(d, date));

  MonthlyReport monthlyReport(DateTime anyDay) {
    final month = budgetMonth(anyDay);
    final end = monthEnd(month);
    final prev = DateTime(month.year, month.month - 1, month.day);
    final inMonthList = inMonth(month);
    final spending = inMonthList.where((t) => t.isSpending).toList()..sort((a, b) => a.cents.compareTo(b.cents));
    return MonthlyReport(
      month: month,
      end: end,
      income: incomeIn(month),
      spent: spentIn(month),
      incomeBefore: incomeIn(prev),
      spentBefore: spentIn(prev),
      budgets: [for (final b in budgets) (b.category, spentIn(month, category: b.category), availableFor(b, month))],
      topCategories: spendingByCategory(month, end).take(5).toList(),
      biggest: spending.isEmpty ? null : spending.first,
      billsPaid: bills.where((b) => b.lastPaid != null && inBudgetMonth(b.lastPaid!, month)).length,
      savedToGoals: goals.expand((g) => g.history).where((h) => inBudgetMonth(h.date, month)).fold(0, (s, h) => s + h.cents),
      debtPaid: debts.expand((d) => d.payments).where((p) => inBudgetMonth(p.date, month)).fold(0, (s, p) => s + p.cents),
      netWorthStart: netWorthAt(month.subtract(const Duration(days: 1))),
      netWorthEnd: netWorthAt(end),
      transactions: inMonthList.length,
    );
  }

  // ----- Shared household -----

  /// Shared spending in a budget month (transactions with a "Paid by"), split by the household's shares.
  HouseholdMonth householdMonth(DateTime anyDay) {
    final people = prefs.householdPeople;
    final month = budgetMonth(anyDay);
    final shares = people.length == 2 ? [prefs.householdShare, 100 - prefs.householdShare] : [for (final _ in people) 100 / max(1, people.length)];
    final list = [for (final t in inMonth(month)) if (t.isSpending && people.contains(t.paidBy)) t];
    final total = list.fold<int>(0, (s, t) => s - t.cents);
    final paid = {for (final p in people) p: list.where((t) => t.paidBy == p).fold<int>(0, (s, t) => s - t.cents)};
    final fair = <String, int>{};
    var given = 0;
    for (final (i, p) in people.indexed) {
      final f = i == people.length - 1 ? total - given : (total * shares[i] / 100).round();
      fair[p] = f;
      given += f;
    }
    // Whoever paid more than their share gets paid back by those who paid less
    final owed = [for (final p in people) (p, paid[p]! - fair[p]!)];
    final givers = [for (final o in owed) if (o.$2 < 0) [o.$1, -o.$2]];
    final takers = [for (final o in owed) if (o.$2 > 0) [o.$1, o.$2]];
    final settle = <(String, String, int)>[];
    var gi = 0, ti = 0;
    while (gi < givers.length && ti < takers.length) {
      final amount = min(givers[gi][1] as int, takers[ti][1] as int);
      if (amount > 0) settle.add((givers[gi][0] as String, takers[ti][0] as String, amount));
      givers[gi][1] = (givers[gi][1] as int) - amount;
      takers[ti][1] = (takers[ti][1] as int) - amount;
      if (givers[gi][1] == 0) gi++;
      if (takers[ti][1] == 0) ti++;
    }
    return HouseholdMonth(people: people, paid: paid, fair: fair, total: total, settle: settle, transactions: list);
  }

  // ----- This year vs last -----

  /// Spending by category for [from]–[to] this year and the same days a year earlier, biggest changes first.
  YearOverYear yearOverYear(DateTime from, DateTime to) {
    DateTime back(DateTime d) => DateTime(d.year - 1, d.month, min(d.day, daysInMonth(DateTime(d.year - 1, d.month))));
    final pf = back(from), pt = back(to);
    final now = {for (final e in spendingByCategory(from, to)) e.key: e.value};
    final then = {for (final e in spendingByCategory(pf, pt)) e.key: e.value};
    int income(DateTime a, DateTime b) => transactions
        .where((t) => t.isIncome && !dayOnly(t.date).isBefore(a) && !dayOnly(t.date).isAfter(b))
        .fold<int>(0, (s, t) => s + t.cents);
    final cats = {...now.keys, ...then.keys}.map((c) => (c, now[c] ?? 0, then[c] ?? 0)).toList()
      ..sort((a, b) => (b.$2 - b.$3).abs().compareTo((a.$2 - a.$3).abs()));
    return YearOverYear(
      from: from,
      to: to,
      spent: now.values.fold(0, (a, b) => a + b),
      spentBefore: then.values.fold(0, (a, b) => a + b),
      income: income(from, to),
      incomeBefore: income(pf, pt),
      categories: cats,
    );
  }
}

/// One month on a page: in and out, budgets, where it went, bills, savings and net worth.
class MonthlyReport {
  const MonthlyReport({
    required this.month,
    required this.end,
    required this.income,
    required this.spent,
    required this.incomeBefore,
    required this.spentBefore,
    required this.budgets,
    required this.topCategories,
    required this.biggest,
    required this.billsPaid,
    required this.savedToGoals,
    required this.debtPaid,
    required this.netWorthStart,
    required this.netWorthEnd,
    required this.transactions,
  });
  final DateTime month, end;
  final int income, spent, incomeBefore, spentBefore, billsPaid, savedToGoals, debtPaid, netWorthStart, netWorthEnd, transactions;

  /// (category, spent, available) for each budget.
  final List<(String, int, int)> budgets;
  final List<MapEntry<String, int>> topCategories;
  final Txn? biggest;

  int get saved => income - spent;
  double? get savingsRate => income <= 0 ? null : saved / income;
  int get budgetsOver => budgets.where((b) => b.$2 > b.$3).length;
}

/// Shared household spending for a month: who paid what, each person's fair share, and how to settle up.
class HouseholdMonth {
  const HouseholdMonth({required this.people, required this.paid, required this.fair, required this.total, required this.settle, required this.transactions});
  final List<String> people;
  final Map<String, int> paid, fair;
  final int total;

  /// (who pays, who gets paid, how much) to even things up.
  final List<(String, String, int)> settle;
  final List<Txn> transactions;
}

class PlanLine {
  const PlanLine(this.kind, this.label, this.cents, this.date, {this.category = ''});

  /// A bill's category (lower case), so budget money doesn't count it twice.
  final String category;

  /// 'bill', 'budget', 'debt' or 'savings'.
  final String kind;
  final String label;
  final int cents;
  final DateTime date;
}

class PaycheckPlan {
  const PaycheckPlan({required this.source, required this.payday, required this.next, required this.amountCents, required this.lines, required this.leftoverCents});
  final IncomeSource source;
  final DateTime payday, next;
  final int amountCents, leftoverCents;
  final List<PlanLine> lines;

  int total(String kind) => lines.where((l) => l.kind == kind).fold(0, (s, l) => s + l.cents);
}

class YearOverYear {
  const YearOverYear({required this.from, required this.to, required this.spent, required this.spentBefore, required this.income, required this.incomeBefore, required this.categories});
  final DateTime from, to;
  final int spent, spentBefore, income, incomeBefore;

  /// (category, this year, last year), biggest change first.
  final List<(String, int, int)> categories;

  bool get hasLastYear => spentBefore > 0 || incomeBefore > 0;

  /// +12 for 12% more than last year; null without last year's numbers.
  static int? change(int now, int before) => before <= 0 ? null : ((now - before) * 100 / before).round();
}
