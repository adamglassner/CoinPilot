import 'dart:math';

import 'dates.dart';
import 'finance.dart';
import 'records.dart';
import 'store.dart';

// Forecast: where your money is headed.
//
// Starting from what's in your spending accounts today, it adds paychecks on their paydays and takes away bills on
// their due dates, what's left in each budget (spread over the days left in the month), other everyday spending,
// debt payments and money set aside for goals and sinking funds. The result is a balance for every day up to the end
// of the month (or 30, 60 or 90 days), with a likely range, the lowest point, and what moves the number most.
//
// WillowWallet for Windows does exactly the same math (Data/Forecast.cs); the tests on both sides use the same example.

/// Something that changes the balance on a day.
class ForecastEvent {
  const ForecastEvent(this.date, this.label, this.group, this.cents, {this.lowCents, this.highCents, this.kind = 'other'});
  final DateTime date;
  final String label;

  /// What it's added up under in "What moves the number" ("Paychecks", "Bills"…).
  final String group;

  /// Expected change (+ in, − out), and the best and worst cases when they differ.
  final int cents;
  final int? lowCents, highCents;

  /// 'income', 'bill', 'budget', 'other', 'debt', 'savings' or 'whatif'.
  final String kind;

  int get worst => lowCents ?? cents;
  int get best => highCents ?? cents;
}

/// A "what if" to try without saving anything.
class WhatIf {
  const WhatIf(this.label, this.cents, this.date);
  final String label;

  /// + money coming in, − going out.
  final int cents;
  final DateTime date;
}

class ForecastDay {
  const ForecastDay(this.date, this.expected, this.low, this.high);
  final DateTime date;
  final int expected, low, high;
}

class ForecastDriver {
  const ForecastDriver(this.label, this.cents, this.kind);
  final String label;
  final int cents;
  final String kind;
}

enum ForecastVerdict { over, under, onTarget }

class Forecast {
  Forecast({
    required this.today,
    required this.end,
    required this.startCents,
    required this.days,
    required this.events,
    required this.targetCents,
    required this.accounts,
    required this.nextPayday,
  });

  final DateTime today, end;
  final int startCents, targetCents;
  final List<ForecastDay> days;
  final List<ForecastEvent> events;

  /// The accounts counted.
  final List<String> accounts;
  final DateTime? nextPayday;

  /// Allowed difference for "right on target": $25.
  static const tolerance = 2500;

  int get endExpected => days.last.expected;
  int get endLow => days.last.low;
  int get endHigh => days.last.high;
  int get difference => endExpected - targetCents;

  ForecastVerdict get verdict => difference.abs() <= tolerance
      ? ForecastVerdict.onTarget
      : difference > 0
          ? ForecastVerdict.over
          : ForecastVerdict.under;

  /// The lowest expected balance and its day.
  ForecastDay get lowPoint => days.reduce((a, b) => b.expected < a.expected ? b : a);

  /// Spending beyond your budgets that still leaves the target (or your cushion) in place until the next payday.
  int get safeToSpend {
    final until = nextPayday ?? end;
    var low = days.first.expected;
    for (final d in days) {
      if (!d.date.isBefore(until)) break;
      low = min(low, d.expected);
    }
    return max(0, low - max(0, targetCents));
  }

  /// "High", "Medium" or "Low": how far apart the best and worst cases are.
  String get confidence {
    final spread = endHigh - endLow;
    final scale = max(1, events.where((e) => e.cents > 0).fold<int>(0, (s, e) => s + e.cents));
    final r = spread / scale;
    return r < 0.04 ? 'High' : r < 0.15 ? 'Medium' : 'Low';
  }

  /// What moves the balance most, biggest first, added up by what they are.
  List<ForecastDriver> drivers({int count = 6}) {
    final totals = <String, int>{};
    final kinds = <String, String>{};
    for (final e in events) {
      totals[e.group] = (totals[e.group] ?? 0) + e.cents;
      kinds[e.group] = e.kind;
    }
    final list = [for (final e in totals.entries) if (e.value != 0) ForecastDriver(e.key, e.value, kinds[e.key]!)];
    list.sort((a, b) => b.cents.abs().compareTo(a.cents.abs()));
    return list.take(count).toList();
  }

  /// The events on a day (for the bill calendar).
  List<ForecastEvent> on(DateTime day) => [for (final e in events) if (sameDay(e.date, day)) e];

  ForecastDay? dayOf(DateTime d) {
    for (final x in days) {
      if (sameDay(x.date, d)) return x;
    }
    return null;
  }
}

/// Accounts the forecast counts: checking, cash and payment apps (and savings when chosen).
const forecastAccountTypes = [AccountTypes.checking, AccountTypes.cash, AccountTypes.paymentApp];

extension ForecastMath on Store {
  /// The day the forecast runs to: the end of the budget month, or 30, 60 or 90 days from today.
  DateTime forecastEnd(DateTime today, {String? horizon}) {
    final h = horizon ?? prefs.forecastHorizon;
    final t = dayOnly(today);
    return switch (h) {
      '30' => DateTime(t.year, t.month, t.day + 30),
      '60' => DateTime(t.year, t.month, t.day + 60),
      '90' => DateTime(t.year, t.month, t.day + 90),
      _ => monthEnd(budgetMonth(t)),
    };
  }

  List<Account> forecastAccounts() {
    final types = [...forecastAccountTypes, if (prefs.forecastIncludeSavings) AccountTypes.savings];
    return [for (final a in accountRecords) if (!a.isClosed && types.contains(a.type)) a];
  }

  /// How much more (or less) than its budget a category usually spends: the average of the last three months'
  /// spent ÷ budget, and the highest. 1.0 when there isn't enough history.
  (double, double) budgetHabit(Budget b, DateTime today) {
    final month = budgetMonth(today);
    final ratios = <double>[];
    for (var i = 1; i <= 3; i++) {
      final m = DateTime(month.year, month.month - i, month.day);
      if (m.isBefore(budgetMonth(b.startMonth ?? b.createdAt))) continue;
      final limit = limitFor(b, m);
      if (limit <= 0) continue;
      ratios.add(spentIn(m, category: b.category) / limit);
    }
    if (ratios.length < 2) return (1.0, 1.0);
    final avg = ratios.reduce((a, b) => a + b) / ratios.length;
    final worst = ratios.reduce(max);
    return (avg.clamp(1.0, 1.5).toDouble(), worst.clamp(1.0, 2.0).toDouble());
  }

  /// Average monthly spending in categories with no budget and no bills (the last three months).
  int otherSpendingPerMonth(DateTime today) {
    final month = budgetMonth(today);
    final billCats = {for (final b in bills) b.category.toLowerCase()};
    var total = 0;
    for (var i = 1; i <= 3; i++) {
      final m = DateTime(month.year, month.month - i, month.day);
      total += _otherSpent(m, billCats);
    }
    return total ~/ 3;
  }

  int _otherSpent(DateTime month, Set<String> billCats) {
    var total = 0;
    for (final e in spentByCategory(month).entries) {
      final c = e.key;
      if (billCats.contains(c.toLowerCase()) || billCats.contains(rootOf(c).toLowerCase())) continue;
      if (budgetFor(c) != null || budgetFor(rootOf(c)) != null) continue;
      total += e.value;
    }
    return total;
  }

  Forecast forecast(DateTime now, {String? horizon, DateTime? until, List<WhatIf> whatIfs = const [], Set<String> skipBills = const {}, Map<String, int> budgetChanges = const {}}) {
    final today = dayOnly(now);
    final end = until != null ? dayOnly(until) : forecastEnd(today, horizon: horizon);
    final p = prefs;
    final accounts = forecastAccounts();
    final start = accounts.fold<int>(0, (s, a) => s + balanceAt(a, today));
    final events = <ForecastEvent>[];
    DateTime clampDay(DateTime d) => dayOnly(d).isBefore(today) ? today : dayOnly(d);
    DateTime? nextPayday;

    // 1. Paychecks
    for (final s in incomeSources) {
      if (s.isDone) continue;
      final fixed = s.amountCents > 0 && !s.varies;
      final each = fixed ? s.amountCents : typicalIncome(s);
      var low = each, high = each;
      if (!fixed) {
        final last = (transactions.where((t) => t.isIncome && t.payee.toLowerCase() == s.name.toLowerCase()).toList()
              ..sort((a, b) => b.date.compareTo(a.date)))
            .take(4)
            .map((t) => t.cents)
            .toList();
        if (last.isNotEmpty) {
          low = last.reduce(min);
          high = last.reduce(max);
        }
      }
      if (each <= 0) continue;
      for (final d in paydaysBetween(s, DateTime(2000), end)) {
        final day = clampDay(d);
        if (day.isAfter(today) && (nextPayday == null || day.isBefore(nextPayday))) nextPayday = day;
        events.add(ForecastEvent(day, s.name, 'Paycheck: ${s.name}', each, lowCents: low, highCents: high, kind: 'income'));
      }
    }

    // 2. Bills (and how much of each month's bills land in each category, so budgets don't count them twice)
    final billsByMonthCat = <String, int>{};
    for (final b in bills) {
      if (b.isDone || skipBills.contains(b.id)) continue;
      var d = dayOnly(b.nextDue);
      for (var i = 0; i < 200 && !d.isAfter(end); i++) {
        final day = clampDay(d);
        events.add(ForecastEvent(day, b.name, 'Bill: ${b.name}', -b.amountCents, kind: 'bill'));
        final key = '${isoDay(budgetMonth(day))}|${rootOf(b.category).toLowerCase()}';
        billsByMonthCat[key] = (billsByMonthCat[key] ?? 0) + b.amountCents;
        if (b.repeats == BillSchedule.once) break;
        d = Bills.next(d, b.repeats, b.dueDay);
      }
    }

    // 3. Budgets and other everyday spending, spread over the days left in each budget month
    final other = otherSpendingPerMonth(today);
    final billCats = {for (final b in bills) b.category.toLowerCase()};
    for (var m = budgetMonth(today); !m.isAfter(end); m = DateTime(m.year, m.month + 1, m.day)) {
      final mEnd = monthEnd(m);
      final from = m.isBefore(today) ? today : m;
      final to = mEnd.isAfter(end) ? end : mEnd;
      final daysLeft = mEnd.difference(from).inDays + 1;
      final daysIn = to.difference(from).inDays + 1;
      if (daysIn <= 0) continue;
      final current = !m.isAfter(today);
      for (final b in budgets) {
        final limit = budgetChanges[b.category] ?? limitFor(b, m);
        final available = current ? limit + carryFor(b, m) : limit;
        final spent = current ? spentIn(m, category: b.category) : 0;
        final billsHere = billsByMonthCat['${isoDay(m)}|${b.category.toLowerCase()}'] ?? 0;
        final (usual, worst) = budgetHabit(b, today);
        int left(double habit) => max(0, (max(available, (limit * habit).round()) - spent - billsHere));
        final expected = left(usual), best = max(0, available - spent - billsHere), worstLeft = left(worst);
        if (expected == 0 && worstLeft == 0) continue;
        final share = daysIn / daysLeft;
        events.add(ForecastEvent(to, '${b.category} budget', 'Budget: ${b.category}', -(expected * share).round(),
            lowCents: -(worstLeft * share).round(), highCents: -(best * share).round(), kind: 'budget'));
      }
      if (other > 0) {
        final spentSoFar = current ? _otherSpent(m, billCats) : 0;
        final left = max(0, other - spentSoFar);
        if (left > 0) {
          final share = daysIn / daysLeft;
          events.add(ForecastEvent(to, 'Other spending', 'Other everyday spending', -(left * share).round(), kind: 'other'));
        }
      }
    }

    // 4. Debt payments that aren't bills: the minimum (and the plan's extra) at the end of each month
    final linked = {for (final b in bills) if (b.paysDebt) b.debtId};
    final unlinked = [for (final d in debts) if (!d.isPaidOff && d.minimumCents > 0 && !linked.contains(d.id)) d];
    for (var m = budgetMonth(today); !m.isAfter(end); m = DateTime(m.year, m.month + 1, m.day)) {
      if (monthEnd(m).isAfter(end)) continue;
      final day = monthEnd(m);
      for (final d in unlinked) {
        if (d.payments.any((x) => inBudgetMonth(x.date, m))) continue;
        events.add(ForecastEvent(day, d.name, 'Debt payments', -d.minimumCents, kind: 'debt'));
      }
      if (unlinked.isNotEmpty && debtPlan.extraCents > 0) events.add(ForecastEvent(day, 'Extra debt payment', 'Debt payments', -debtPlan.extraCents, kind: 'debt'));
    }

    // 5. Money set aside for goals and sinking funds (unless it stays in a counted account)
    if (p.forecastCountSavings) {
      final counted = {for (final a in accounts) a.name.toLowerCase()};
      for (final g in goals) {
        if (g.isLinked && counted.contains(g.linkedAccount.toLowerCase())) continue;
        for (var m = budgetMonth(today); !m.isAfter(end); m = DateTime(m.year, m.month + 1, m.day)) {
          if (monthEnd(m).isAfter(end)) continue;
          if (g.history.any((x) => x.cents > 0 && inBudgetMonth(x.date, m))) continue;
          final need = monthlyNeeded(g, m.isBefore(today) ? today : m);
          if (need == null || need <= 0) continue;
          events.add(ForecastEvent(monthEnd(m), g.name, g.isFund ? 'Sinking fund: ${g.name}' : 'Goal: ${g.name}', -need, kind: 'savings'));
        }
      }
    }

    // 6. What ifs
    for (final w in whatIfs) {
      final d = clampDay(w.date);
      if (!d.isAfter(end)) events.add(ForecastEvent(d, w.label, 'What if: ${w.label}', w.cents, kind: 'whatif'));
    }

    events.sort((a, b) => a.date.compareTo(b.date));

    // Budget and everyday spending is spread evenly; everything else lands on its day
    final days = <ForecastDay>[];
    var exp = start, lo = start, hi = start;
    final spread = [for (final e in events) if (e.kind == 'budget' || e.kind == 'other') e];
    for (var d = today; !d.isAfter(end); d = DateTime(d.year, d.month, d.day + 1)) {
      for (final e in events) {
        if (e.kind == 'budget' || e.kind == 'other' || !sameDay(e.date, d)) continue;
        exp += e.cents;
        lo += e.worst;
        hi += e.best;
      }
      for (final e in spread) {
        // Spread from the later of today and the start of its budget month, through its date
        final m = budgetMonth(e.date);
        final from = m.isBefore(today) ? today : m;
        if (d.isBefore(from) || d.isAfter(e.date)) continue;
        final n = e.date.difference(from).inDays + 1;
        final i = d.difference(from).inDays; // 0-based: spread whole cents so the days add up exactly
        int part(int total) => (total * (i + 1) / n).round() - (total * i / n).round();
        exp += part(e.cents);
        lo += part(e.worst);
        hi += part(e.best);
      }
      days.add(ForecastDay(d, exp, lo, hi));
    }

    return Forecast(
      today: today,
      end: end,
      startCents: start,
      days: days,
      events: events,
      targetCents: p.forecastTargetCents,
      accounts: [for (final a in accounts) a.name],
      nextPayday: nextPayday,
    );
  }
}
