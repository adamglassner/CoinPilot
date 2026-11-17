import 'dart:math';

import 'dates.dart';
import 'models.dart';
import 'money.dart';
import 'records.dart';
import 'store.dart';

// The same money rules as WillowWallet for Windows (Data/Finance.cs, Income.cs, SubscriptionCuts.cs), in whole cents.

bool _same(String a, String b) => a.toLowerCase() == b.toLowerCase();
DateTime _addMonthsKeepDay(DateTime d, int months, int day) {
  final first = DateTime(d.year, d.month + months, 1);
  return DateTime(first.year, first.month, min(day, daysInMonth(first)));
}

String ordinal(int n) => '$n${(n % 100 >= 11 && n % 100 <= 13) ? 'th' : switch (n % 10) { 1 => 'st', 2 => 'nd', 3 => 'rd', _ => 'th' }}';

// ======================================================================
// Bills
// ======================================================================

enum BillStatus { overdue, dueToday, dueSoon, upcoming, paid }

class Bills {
  /// The due date after [due]. Monthly bills keep their day of the month (31st → 30th → 31st).
  static DateTime next(DateTime due, String repeats, int dayOfMonth) {
    final day = dayOfMonth > 0 ? dayOfMonth : due.day;
    return switch (repeats) {
      BillSchedule.weekly => DateTime(due.year, due.month, due.day + 7),
      BillSchedule.everyTwoWeeks => DateTime(due.year, due.month, due.day + 14),
      BillSchedule.monthly => _addMonthsKeepDay(due, 1, day),
      BillSchedule.quarterly => _addMonthsKeepDay(due, 3, day),
      BillSchedule.yearly => _addMonthsKeepDay(due, 12, day),
      _ => due,
    };
  }

  /// What a bill costs per month on average (a weekly $10 bill is about $43.33 a month).
  static double monthlyCost(int cents, String repeats) => switch (repeats) {
        BillSchedule.weekly => cents * 52 / 12,
        BillSchedule.everyTwoWeeks => cents * 26 / 12,
        BillSchedule.monthly => cents.toDouble(),
        BillSchedule.quarterly => cents / 3,
        BillSchedule.yearly => cents / 12,
        _ => 0,
      };

  static BillStatus status(Bill b, DateTime today) {
    if (b.isDone) return BillStatus.paid;
    final days = dayOnly(b.nextDue).difference(dayOnly(today)).inDays;
    return days < 0
        ? BillStatus.overdue
        : days == 0
            ? BillStatus.dueToday
            : days <= 7
                ? BillStatus.dueSoon
                : BillStatus.upcoming;
  }

  static String statusText(Bill b, DateTime today) {
    final days = dayOnly(b.nextDue).difference(dayOnly(today)).inDays;
    return switch (status(b, today)) {
      BillStatus.paid => 'Paid ${b.lastPaid == null ? '' : shortDate(b.lastPaid!)}',
      BillStatus.overdue => days == -1 ? 'Overdue by 1 day' : 'Overdue by ${-days} days',
      BillStatus.dueToday => 'Due today',
      _ => days == 1 ? 'Due tomorrow' : 'Due in $days days',
    };
  }
}

extension BillMath on Store {
  /// Open bills, soonest first.
  List<Bill> get openBills => bills.where((b) => !b.isDone).toList()..sort((a, b) => a.nextDue.compareTo(b.nextDue));

  /// Bills due within [days] days (and overdue ones).
  List<Bill> billsDueWithin(int days, DateTime today) =>
      openBills.where((b) => dayOnly(b.nextDue).difference(dayOnly(today)).inDays <= days).toList();

  int monthlyBillsCents() => bills.where((b) => !b.isDone).fold<int>(0, (s, b) => s + Bills.monthlyCost(b.amountCents, b.repeats).round());

  /// Marks the current due date paid: records the payment (unless told not to) and moves to the next due date.
  /// A bill that pays a debt lowers its balance and ends when the debt is paid off.
  Txn? markPaid(Bill bill, DateTime paidOn, {bool addTransaction = true}) => batch(() {
        final b = bill.copy();
        final debt = b.paysDebt ? debtById(b.debtId) : null;
        if (debt != null && !debt.isPaidOff) {
          final t = applyDebtPayment(debt, min(b.amountCents, debt.balanceCents), paidOn, b.account,
              addTransaction: addTransaction, payee: b.name, note: 'Bill due ${shortDate(b.nextDue)}');
          final paidOff = debtById(debt.id)?.isPaidOff ?? false;
          b.lastPaid = dayOnly(paidOn);
          if (paidOff) {
            b.repeats = BillSchedule.once;
          } else if (b.repeats != BillSchedule.once) {
            b.nextDue = Bills.next(b.nextDue, b.repeats, b.dueDay);
          }
          put(b);
          return t;
        }
        Txn? t;
        if (addTransaction) {
          t = Txn(
            payee: b.name,
            category: b.category,
            account: b.account.isEmpty ? accountNames().first : b.account,
            date: paidOn,
            cents: -b.amountCents,
            note: 'Bill due ${shortDate(b.nextDue)}',
          );
          saveTxn(t);
        }
        b.lastPaid = dayOnly(paidOn);
        if (b.repeats != BillSchedule.once) b.nextDue = Bills.next(b.nextDue, b.repeats, b.dueDay);
        put(b);
        return t;
      });

  /// Moves a bill to its next due date without recording anything ("skip this one").
  void skipBill(Bill bill) {
    final b = bill.copy();
    if (b.repeats == BillSchedule.once) {
      b.lastPaid = dayOnly(DateTime.now());
    } else {
      b.nextDue = Bills.next(b.nextDue, b.repeats, b.dueDay);
    }
    put(b);
  }

  /// Bills with autopay on whose date has come: marked paid (with a transaction). Returns how many.
  int payAutoPayBills(DateTime today) => batch(() {
        var n = 0;
        for (final b in bills.where((b) => b.autoPay && !b.isDone)) {
          var current = b;
          for (var i = 0; i < 24 && !current.isDone && !dayOnly(current.nextDue).isAfter(dayOnly(today)); i++) {
            markPaid(current, current.nextDue);
            n++;
            final again = bills.where((x) => x.id == b.id).toList();
            if (again.isEmpty) break;
            current = again.first;
          }
        }
        return n;
      });

  // ----- Bills that pay down debts -----

  /// The bill that pays this debt, if any (the open one first).
  Bill? billForDebt(Debt d) {
    final list = bills.where((b) => b.debtId == d.id).toList()..sort((a, b) => (a.isDone ? 1 : 0) - (b.isDone ? 1 : 0));
    return list.isEmpty ? null : list.first;
  }

  /// A monthly bill for the debt's payment, due on [firstDue]'s day each month.
  Bill createDebtBill(Debt d, DateTime firstDue, String account, {int? amountCents, bool autoPay = false}) {
    final b = Bill.create(
      name: d.name,
      amountCents: amountCents ?? d.minimumCents,
      nextDue: firstDue,
      repeats: BillSchedule.monthly,
      category: 'Debt payments',
      account: account,
      autoPay: autoPay,
      debtId: d.id,
    );
    put(b);
    return b;
  }

  /// Lowers the balance, keeps the payment history and (optionally) records the transaction.
  Txn? applyDebtPayment(Debt debt, int cents, DateTime date, String account,
      {bool addTransaction = true, String? payee, String note = 'Debt payment'}) {
    final d = debt.copy();
    d.balanceCents = max(0, d.balanceCents - cents);
    d.payments = [...d.payments, MoneyEntry(dayOnly(date), cents)];
    put(d);
    if (!addTransaction) return null;
    final t = Txn(
      payee: payee ?? d.name,
      category: 'Debt payments',
      account: account.isEmpty ? accountNames().first : account,
      date: date,
      cents: -cents,
      note: note,
    );
    saveTxn(t);
    return t;
  }

  /// A payment recorded on the Debts screen: when the debt has a bill due within 10 days, it counts as that bill.
  void recordDebtPayment(Debt debt, int cents, DateTime date, String account, {bool addTransaction = true}) => batch(() {
        applyDebtPayment(debt, cents, date, account, addTransaction: addTransaction);
        final bill = billForDebt(debt);
        if (bill != null && !bill.isDone && dayOnly(bill.nextDue).difference(dayOnly(date)).inDays <= 10) {
          final b = bill.copy()..lastPaid = dayOnly(date);
          if (debtById(debt.id)?.isPaidOff ?? false) {
            b.repeats = BillSchedule.once;
          } else if (b.repeats != BillSchedule.once) {
            b.nextDue = Bills.next(b.nextDue, b.repeats, b.dueDay);
          }
          put(b);
        }
      });

  /// A deleted debt's bills become ordinary bills.
  void deleteDebt(Debt d) => batch(() {
        for (final b in bills.where((b) => b.debtId == d.id)) {
          put(b.copy()..debtId = '');
        }
        remove(d);
      });
}

// ======================================================================
// Accounts and net worth
// ======================================================================

extension AccountMath on Store {
  static int effect(Txn t, String account) {
    var e = 0;
    if (_same(t.account, account)) e += t.cents;
    if (t.isTransfer && _same(t.transferAccount, account)) e -= t.cents;
    return e;
  }

  /// The account's balance at the end of [date] (works before its balance date too).
  int balanceAt(Account a, DateTime date) {
    final start = dayOnly(a.balanceDate);
    final d = dayOnly(date);
    var total = a.startingCents;
    if (!d.isBefore(start)) {
      for (final t in transactions) {
        final td = dayOnly(t.date);
        if (td.isAfter(start) && !td.isAfter(d)) total += effect(t, a.name);
      }
    } else {
      for (final t in transactions) {
        final td = dayOnly(t.date);
        if (td.isAfter(d) && !td.isAfter(start)) total -= effect(t, a.name);
      }
    }
    return total;
  }

  int balance(Account a) => balanceAt(a, DateTime(9999));

  DateTime? lastActivity(Account a) {
    DateTime? last;
    for (final t in transactions) {
      if (_same(t.account, a.name) || (t.isTransfer && _same(t.transferAccount, a.name))) {
        if (last == null || t.date.isAfter(last)) last = t.date;
      }
    }
    return last;
  }

  /// What a debt was at the end of a day: today's balance plus payments made after it.
  static int debtAt(Debt d, DateTime date) =>
      max(0, d.balanceCents + d.payments.where((p) => dayOnly(p.date).isAfter(dayOnly(date))).fold<int>(0, (s, p) => s + p.cents));

  int get assetsCents => accountRecords.where((a) => !a.isClosed && !a.isLiability).fold<int>(0, (s, a) => s + balance(a));
  int get cardsCents => accountRecords.where((a) => !a.isClosed && a.isLiability).fold<int>(0, (s, a) => s + balance(a));
  int get debtTotalCents => debts.fold<int>(0, (s, d) => s + max(0, d.balanceCents));

  /// Open accounts (assets minus card balances) minus everything on the Debts screen.
  int get netWorth => accountRecords.where((a) => !a.isClosed).fold<int>(0, (s, a) => s + balance(a)) - debtTotalCents;

  /// Net worth at the end of each of the last [months] months (the last point is today).
  List<(DateTime, int)> netWorthHistory(int months, {DateTime? now}) {
    final today = dayOnly(now ?? DateTime.now());
    final result = <(DateTime, int)>[];
    for (var i = months - 1; i >= 0; i--) {
      final date = i == 0 ? today : DateTime(today.year, today.month - i + 1, 0);
      final value = accountRecords.fold<int>(0, (s, a) => s + balanceAt(a, date)) - debts.fold<int>(0, (s, d) => s + debtAt(d, date));
      result.add((date, value));
    }
    return result;
  }

  /// Renames an account everywhere it's used (transactions, transfers, bills, income and goals).
  void renameAccount(String oldName, String newName) => batch(() {
        if (oldName == newName || oldName.isEmpty) return;
        for (final t in transactions) {
          final a = _same(t.account, oldName), b = _same(t.transferAccount, oldName);
          if (!a && !b) continue;
          saveTxn(t.copyWith(account: a ? newName : null, extra: b ? {'TransferAccount': newName} : null));
        }
        for (final b in bills.where((b) => _same(b.account, oldName))) {
          put(b.copy()..account = newName);
        }
        for (final s in incomeSources.where((s) => _same(s.account, oldName))) {
          put(s.copy()..account = newName);
        }
        for (final g in goals.where((g) => _same(g.linkedAccount, oldName))) {
          put(g.copy()..linkedAccount = newName);
        }
      });
}

// ======================================================================
// Debts: payoff plan
// ======================================================================

class DebtPlan {
  /// Month number (1 = next month) each debt is paid off in, by debt id. Missing = never at this pace.
  final Map<String, int> payoffMonth = {};
  /// Order the plan attacks debts in (1 = first), by debt id.
  final Map<String, int> order = {};
  int totalInterest = 0;
  int months = 0;
  bool neverPaysOff = false;
  int monthlyPayment = 0;

  DateTime? debtFreeDate(DateTime today) => neverPaysOff || months == 0 ? null : DateTime(today.year, today.month + months);

  static const avalanche = 'Avalanche';
  static const snowball = 'Snowball';

  /// Interest is added each month, every debt gets its minimum, and the extra payment (plus the minimums of debts
  /// already paid off) goes to the first debt in order. Avalanche = highest rate first; snowball = smallest balance first.
  static DebtPlan make(List<Debt> debts, String strategy, int extraCents) {
    final active = debts.where((d) => d.balanceCents > 0).toList();
    if (strategy == snowball) {
      active.sort((a, b) {
        final c = a.balanceCents.compareTo(b.balanceCents);
        return c != 0 ? c : b.rate.compareTo(a.rate);
      });
    } else {
      active.sort((a, b) {
        final c = b.rate.compareTo(a.rate);
        return c != 0 ? c : a.balanceCents.compareTo(b.balanceCents);
      });
    }
    final r = DebtPlan();
    for (var i = 0; i < active.length; i++) {
      r.order[active[i].id] = i + 1;
    }
    if (active.isEmpty) return r;
    final balance = {for (final d in active) d.id: d.balanceCents.toDouble()};
    final int monthly = active.fold<int>(0, (s, d) => s + d.minimumCents) + max<int>(0, extraCents);
    r.monthlyPayment = monthly;
    var interestTotal = 0.0;
    var month = 0;
    while (balance.values.any((b) => b > 0) && month < 600) {
      month++;
      final open = active.where((d) => balance[d.id]! > 0).toList();
      for (final d in open) {
        final interest = (balance[d.id]! * d.rate / 1200).roundToDouble();
        balance[d.id] = balance[d.id]! + interest;
        interestTotal += interest;
      }
      var available = monthly.toDouble();
      for (final d in open) {
        final pay = min(min(d.minimumCents.toDouble(), balance[d.id]!), available);
        balance[d.id] = balance[d.id]! - pay;
        available -= pay;
      }
      for (final d in open) {
        if (available <= 0) break;
        final pay = min(available, balance[d.id]!);
        balance[d.id] = balance[d.id]! - pay;
        available -= pay;
      }
      for (final d in open.where((d) => balance[d.id]! <= 0.5)) {
        balance[d.id] = 0;
        r.payoffMonth[d.id] = month;
      }
      if (open.any((d) => balance[d.id]! > d.balanceCents * 10)) break;
    }
    r.months = month;
    r.totalInterest = interestTotal.round();
    r.neverPaysOff = balance.values.any((b) => b > 0);
    return r;
  }
}

extension DebtMath on Store {
  DebtPlan currentDebtPlan() => DebtPlan.make(debts, debtPlan.strategy, debtPlan.extraCents);
}

// ======================================================================
// Goals
// ======================================================================

extension GoalMath on Store {
  /// What's saved: the linked account's balance, or what was added by hand.
  int savedFor(Goal g) {
    if (g.isLinked) {
      for (final a in accountRecords) {
        if (_same(a.name, g.linkedAccount)) return max(0, balance(a));
      }
    }
    return g.savedCents;
  }

  bool isReached(Goal g) => g.targetCents > 0 && savedFor(g) >= g.targetCents;
  int remaining(Goal g) => max(0, g.targetCents - savedFor(g));
  double fraction(Goal g) => g.targetCents <= 0 ? 0 : min(1.0, savedFor(g) / g.targetCents);

  /// How much to save each month to reach the goal by its date, or null when there's no date or it's reached.
  int? monthlyNeeded(Goal g, DateTime today) {
    final date = g.targetDate;
    if (isReached(g) || date == null) return null;
    if (dayOnly(date).isBefore(dayOnly(today))) return remaining(g);
    final months = max(1, (date.year - today.year) * 12 + date.month - today.month);
    return (remaining(g) / months).ceil();
  }

  /// A sinking fund's cost was paid: what was set aside is used, and saving starts again for the next due date.
  void useFund(Goal fund, DateTime paidOn) {
    final g = fund.copy();
    final used = savedFor(g);
    if (!g.isLinked && used > 0) {
      g.history = [...g.history, MoneyEntry(dayOnly(paidOn), -used)];
      g.savedCents = 0;
    }
    var due = g.targetDate ?? dayOnly(paidOn);
    final months = FundSchedule.months(g.repeats);
    for (var i = 0; i < 40 && !due.isAfter(dayOnly(paidOn)); i++) {
      due = DateTime(due.year, due.month + months, min(due.day, daysInMonth(DateTime(due.year, due.month + months))));
    }
    g.targetDate = due;
    put(g);
  }

  /// Adds (positive) or takes out (negative) money and keeps the history. Returns a milestone crossed (25, 50, 75, 100).
  int? addToGoal(Goal goal, int cents, DateTime date) {
    final g = goal.copy();
    final before = g.savedCents;
    g.savedCents = max(0, g.savedCents + cents);
    g.history = [...g.history, MoneyEntry(dayOnly(date), cents)];
    put(g);
    if (g.targetCents <= 0 || g.savedCents <= before) return null;
    for (final pct in [100, 75, 50, 25]) {
      final line = g.targetCents * pct / 100;
      if (before < line && g.savedCents >= line) return pct;
    }
    return null;
  }
}

// ======================================================================
// Income
// ======================================================================

class Paydays {
  /// The payday after [date].
  static DateTime next(DateTime date, String repeats, int day1, int day2) {
    DateTime on(DateTime month, int day) => DateTime(month.year, month.month, min(max(day, 1), daysInMonth(month)));
    switch (repeats) {
      case IncomeSchedule.weekly:
        return DateTime(date.year, date.month, date.day + 7);
      case IncomeSchedule.everyTwoWeeks:
        return DateTime(date.year, date.month, date.day + 14);
      case IncomeSchedule.monthly:
        return on(DateTime(date.year, date.month + 1), day1 > 0 ? day1 : date.day);
      case IncomeSchedule.twiceAMonth:
        final d1 = day1 > 0 ? day1 : 1;
        final a = min(d1, day2), b = max(d1, day2);
        if (a == b) return on(DateTime(date.year, date.month + 1), a);
        final second = on(DateTime(date.year, date.month), b);
        return dayOnly(date).isBefore(second) ? second : on(DateTime(date.year, date.month + 1), a);
      default:
        return date;
    }
  }

  static String describe(IncomeSource s) => s.repeats == IncomeSchedule.twiceAMonth
      ? 'Twice a month (${ordinal(min(s.day1 > 0 ? s.day1 : 1, s.day2))} and ${ordinal(max(s.day1 > 0 ? s.day1 : 1, s.day2))})'
      : s.repeats;
}

extension IncomeMath on Store {
  /// For an amount that changes: the average of the last few times it came in.
  int typicalIncome(IncomeSource s) {
    final last = transactions.where((t) => t.isIncome && _same(t.payee, s.name)).toList()..sort((a, b) => b.date.compareTo(a.date));
    final take = last.take(4).toList();
    if (take.isEmpty) return s.amountCents;
    return (take.fold<int>(0, (x, t) => x + t.cents) / take.length).round();
  }

  /// About how much comes in per month (every 2 weeks: 26 a year).
  int monthlyIncome(IncomeSource s) {
    final each = s.amountCents > 0 ? s.amountCents : (s.varies ? typicalIncome(s) : 0);
    return switch (s.repeats) {
      IncomeSchedule.weekly => (each * 52 / 12).round(),
      IncomeSchedule.everyTwoWeeks => (each * 26 / 12).round(),
      IncomeSchedule.twiceAMonth => each * 2,
      IncomeSchedule.monthly => each,
      _ => 0,
    };
  }

  int expectedMonthlyIncome() => incomeSources.where((s) => !s.isDone).fold<int>(0, (x, s) => x + monthlyIncome(s));

  /// The paydays in [from, to].
  List<DateTime> paydaysBetween(IncomeSource s, DateTime from, DateTime to) {
    final result = <DateTime>[];
    if (s.isDone) return result;
    var d = dayOnly(s.nextDate);
    for (var i = 0; i < 400 && !d.isAfter(dayOnly(to)); i++) {
      if (!d.isBefore(dayOnly(from))) result.add(d);
      if (s.repeats == IncomeSchedule.once) break;
      d = Paydays.next(d, s.repeats, s.day1, s.day2);
    }
    return result;
  }

  /// Income whose payday has come and that needs an amount or a confirmation.
  List<IncomeSource> askingPaydays(DateTime today) =>
      incomeSources.where((s) => s.needsAsking && !dayOnly(s.nextDate).isAfter(dayOnly(today))).toList()
        ..sort((a, b) => a.nextDate.compareTo(b.nextDate));

  /// Adds the income to Transactions and moves the source to its next payday.
  Txn receiveIncome(IncomeSource source, int cents, DateTime date, {String? account}) => batch(() {
        final t = Txn(
          payee: source.name,
          category: source.category,
          account: account ?? (source.account.isNotEmpty ? source.account : accountNames().first),
          date: date,
          cents: cents.abs(),
          note: source.varies ? '' : 'Added on payday',
        );
        saveTxn(t);
        advancePayday(source, date);
        return t;
      });

  /// Moves to the next payday without recording anything.
  void advancePayday(IncomeSource source, DateTime received) {
    final s = incomeSources.firstWhere((x) => x.id == source.id, orElse: () => source).copy();
    s.lastReceived = dayOnly(received);
    if (s.repeats != IncomeSchedule.once) s.nextDate = Paydays.next(s.nextDate, s.repeats, s.day1, s.day2);
    put(s);
  }

  /// Same-amount income set to add itself: records every payday up to today. Returns what was added.
  List<Txn> addDuePaydays(DateTime today) => batch(() {
        final added = <Txn>[];
        for (final src in incomeSources.where((s) => !s.varies && s.autoAdd && s.amountCents > 0)) {
          var s = src;
          for (var i = 0; i < 60 && !s.isDone && !dayOnly(s.nextDate).isAfter(dayOnly(today)); i++) {
            added.add(receiveIncome(s, s.amountCents, s.nextDate));
            s = incomeSources.firstWhere((x) => x.id == src.id);
          }
        }
        return added;
      });
}

// ======================================================================
// Subscriptions: found in transactions, and ones you could cut
// ======================================================================

class SubscriptionGuess {
  SubscriptionGuess(this.payee, this.cents, this.repeats, this.lastCharge, this.nextDue, this.category, this.account, this.charges);
  final String payee;
  final int cents;
  final String repeats;
  final DateTime lastCharge, nextDue;
  final String category, account;
  final int charges;
}

class Subscription {
  Subscription(this.key, this.name, this.cents, this.repeats, this.monthly, this.nextCharge, this.bill, this.guess);
  final String key, name;
  final int cents;
  final String repeats;
  final double monthly;
  final DateTime nextCharge;
  final Bill? bill;
  final SubscriptionGuess? guess;
}

class CutSuggestion {
  CutSuggestion(this.sub, this.reason, this.priority);
  final Subscription sub;
  final String reason;
  final int priority;
  String get key => sub.key;
  int get monthlySaving => sub.monthly.round();
  int get yearlySaving => (sub.monthly * 12).round();
  CutSuggestion withReason(String r) => CutSuggestion(sub, r, priority);
}

class Subscriptions {
  /// "NETFLIX.COM #1234" → "netflix.com": lower case, without trailing reference numbers.
  static String normalize(String payee) => payee
      .trim()
      .toLowerCase()
      .split(' ')
      .where((w) => w.isNotEmpty && !RegExp(r'^\d+$').hasMatch(w.replaceAll(RegExp(r'^[#*]+'), '')))
      .map((w) => w.replaceAll(RegExp(r'^[*#]+|[*#]+$'), ''))
      .where((w) => w.isNotEmpty)
      .join(' ')
      .trim();

  /// Kinds of services people often pay for twice.
  static const groups = <(String, String, List<String>)>[
    ('streaming service', 'streaming services', ['netflix', 'hulu', 'disney', 'max', 'hbo', 'peacock', 'paramount', 'apple tv', 'prime video', 'youtube premium', 'youtube tv', 'sling', 'fubo', 'espn', 'crunchyroll', 'starz', 'showtime', 'discovery', 'britbox', 'philo', 'mubi']),
    ('music app', 'music apps', ['spotify', 'apple music', 'youtube music', 'amazon music', 'pandora', 'tidal', 'siriusxm', 'sirius xm', 'deezer']),
    ('cloud storage plan', 'cloud storage plans', ['icloud', 'google one', 'google storage', 'dropbox', 'onedrive', 'microsoft 365', 'box.com', 'pcloud']),
    ('news subscription', 'news subscriptions', ['new york times', 'nytimes', 'wsj', 'wall street journal', 'washington post', 'the athletic', 'medium', 'substack', 'apple news', 'economist']),
    ('meal kit', 'meal kits', ['hellofresh', 'home chef', 'blue apron', 'factor', 'everyplate', 'green chef', 'sunbasket']),
    ('game subscription', 'game subscriptions', ['xbox', 'game pass', 'playstation plus', 'ps plus', 'nintendo', 'ea play', 'ubisoft']),
    ('delivery membership', 'delivery memberships', ['dashpass', 'doordash', 'uber one', 'instacart+', 'instacart plus', 'grubhub+', 'walmart+', 'walmart plus']),
    ('fitness app', 'fitness apps', ['peloton', 'apple fitness', 'strava', 'beachbody', 'noom', 'fitbit premium', 'myfitnesspal']),
  ];

  static bool _matches(String name, String service) =>
      service.length <= 4 ? name.split(RegExp(r'[ .*\-/]')).contains(service) : name.contains(service);

  static bool isKnownService(String name) {
    final n = name.toLowerCase();
    return groups.any((g) => g.$3.any((s) => _matches(n, s)));
  }

  static String? groupOf(String name) {
    final n = name.toLowerCase();
    for (final g in groups) {
      if (g.$3.any((s) => _matches(n, s))) return g.$1;
    }
    return null;
  }

  /// "DISNEY PLUS 8842" → "Disney Plus" for display.
  static String pretty(String payee) {
    final words = payee.trim().split(' ').where((w) => w.isNotEmpty).toList();
    while (words.length > 1 && RegExp(r'\d').hasMatch(words.last.replaceAll(RegExp(r'^[#*]+'), ''))) {
      words.removeLast();
    }
    final p = words.join(' ');
    if (p.contains(RegExp('[a-z]'))) return p;
    return p.split(' ').map((w) => w.isEmpty ? w : w[0].toUpperCase() + w.substring(1).toLowerCase()).join(' ');
  }
}

extension SubscriptionMath on Store {
  /// Payees charged the same amount on a regular schedule that aren't on the Bills screen and weren't dismissed.
  List<SubscriptionGuess> findSubscriptions(DateTime today) {
    final since = DateTime(today.year, today.month - 14, today.day);
    final billKeys = [for (final b in bills) Subscriptions.normalize(b.name)];
    final dismissed = {for (final d in prefs.dismissedSubscriptions) Subscriptions.normalize(d)};
    final groups = <String, List<Txn>>{};
    for (final t in transactions) {
      if (!t.isSpending || t.date.isBefore(since) || t.payee.trim().isEmpty) continue;
      (groups[Subscriptions.normalize(t.payee)] ??= []).add(t);
    }
    final result = <SubscriptionGuess>[];
    for (final MapEntry(:key, :value) in groups.entries) {
      if (key.length < 2 || dismissed.contains(key)) continue;
      if (billKeys.any((b) => b.isNotEmpty && (b.contains(key) || key.contains(b)))) continue;
      final charges = [...value]..sort((a, b) => a.date.compareTo(b.date));
      if (charges.length < 2) continue;
      // The last few charges are the same amount (within 10%); older ones may differ by a price change.
      final recent = charges.skip(max(0, charges.length - 3)).map((t) => -t.cents).toList()..sort();
      final median = recent[recent.length ~/ 2];
      if (median <= 0 || recent.any((a) => (a - median).abs() > median * 0.10)) continue;
      if (charges.any((t) => -t.cents <= 0 || (-t.cents - median).abs() > median * 0.5)) continue;
      final gaps = [for (var i = 1; i < charges.length; i++) dayOnly(charges[i].date).difference(dayOnly(charges[i - 1].date)).inDays.toDouble()]..sort();
      final gap = gaps[gaps.length ~/ 2];
      final repeats = gap >= 6 && gap <= 8
          ? BillSchedule.weekly
          : gap >= 13 && gap <= 16
              ? BillSchedule.everyTwoWeeks
              : gap >= 26 && gap <= 35
                  ? BillSchedule.monthly
                  : gap >= 85 && gap <= 98
                      ? BillSchedule.quarterly
                      : gap >= 350 && gap <= 380
                          ? BillSchedule.yearly
                          : null;
      if (repeats == null) continue;
      if (gaps.any((g) => (g - gap).abs() > max(4, gap * 0.2))) continue;
      if (charges.length < (repeats == BillSchedule.yearly || repeats == BillSchedule.quarterly ? 2 : 3)) continue;
      final last = charges.last;
      if (dayOnly(today).difference(dayOnly(last.date)).inDays > gap * 2 + 7) continue;
      var next = Bills.next(last.date, repeats, last.date.day);
      while (next.isBefore(dayOnly(today))) {
        next = Bills.next(next, repeats, last.date.day);
      }
      result.add(SubscriptionGuess(last.payee.trim(), median, repeats, last.date, next, last.isSplit ? 'Subscriptions' : last.category, last.account, charges.length));
    }
    result.sort((a, b) => Bills.monthlyCost(b.cents, b.repeats).compareTo(Bills.monthlyCost(a.cents, a.repeats)));
    return result;
  }

  void dismissSubscription(String payee) => updatePrefs((p) {
        if (!p.dismissedSubscriptions.any((d) => _same(d, payee))) p.dismissedSubscriptions = [...p.dismissedSubscriptions, payee];
      });

  /// Every subscription: Bills in Subscriptions (or known services), then repeating charges found in Transactions.
  List<Subscription> allSubscriptions(DateTime today) {
    final list = <Subscription>[];
    for (final b in bills) {
      if (b.isDone || b.paysDebt || b.repeats == BillSchedule.once) continue;
      if (!_same(b.category, 'Subscriptions') && !Subscriptions.isKnownService(b.name)) continue;
      list.add(Subscription(Subscriptions.normalize(b.name), b.name, b.amountCents, b.repeats, Bills.monthlyCost(b.amountCents, b.repeats), b.nextDue, b, null));
    }
    for (final g in findSubscriptions(today)) {
      if (!_same(g.category, 'Subscriptions') && !Subscriptions.isKnownService(g.payee)) continue;
      final key = Subscriptions.normalize(g.payee);
      if (list.any((s) => s.key == key)) continue;
      list.add(Subscription(key, Subscriptions.pretty(g.payee), g.cents, g.repeats, Bills.monthlyCost(g.cents, g.repeats), g.nextDue, null, g));
    }
    return list;
  }

  bool isCutSkipped(String key, DateTime month) {
    final skipped = prefs.skippedCuts;
    final m = budgetMonth(month);
    return skipped.contains('$key|forever') || skipped.contains('$key|${m.year}-${m.month.toString().padLeft(2, '0')}');
  }

  /// Skips a suggestion for one budget month, or for good ([month] null).
  void skipCut(String key, DateTime? month) => updatePrefs((p) {
        String entry;
        if (month == null) {
          entry = '$key|forever';
        } else {
          final m = budgetMonth(month);
          entry = '$key|${m.year}-${m.month.toString().padLeft(2, '0')}';
        }
        final list = [...p.skippedCuts];
        if (!list.contains(entry)) list.add(entry);
        final yearAgo = DateTime.now().subtract(const Duration(days: 365));
        list.removeWhere((e) {
          final parts = e.split('|');
          if (parts.length != 2 || parts[1] == 'forever') return false;
          final d = DateTime.tryParse('${parts[1]}-01');
          return d != null && d.isBefore(yearAgo);
        });
        p.skippedCuts = list;
      });

  void resetCutSkips() => updatePrefs((p) => p.skippedCuts = []);

  /// "I canceled it": the bill ends, the charge stops being suggested, and it's not suggested again.
  void markCanceled(Subscription s) => batch(() {
        if (s.bill != null) remove(s.bill!);
        if (s.guess != null) dismissSubscription(s.guess!.payee);
        skipCut(s.key, null);
      });

  /// Suggestions for a budget month, best first. Skipped ones are left out.
  List<CutSuggestion> suggestCuts(DateTime month, DateTime today) {
    final subs = allSubscriptions(today);
    final found = <String, CutSuggestion>{};
    void offer(Subscription s, String reason, int priority) {
      if (s.monthly <= 0) return;
      final old = found[s.key];
      if (old == null || old.priority < priority) found[s.key] = CutSuggestion(s, reason, priority);
    }

    // 1. Overlapping services: keep the one you pay most for, suggest the others
    final byGroup = <String, List<Subscription>>{};
    for (final s in subs) {
      final g = Subscriptions.groupOf(s.name);
      if (g != null) (byGroup[g] ??= []).add(s);
    }
    for (final MapEntry(:key, :value) in byGroup.entries) {
      if (value.length < 2) continue;
      final plural = Subscriptions.groups.firstWhere((g) => g.$1 == key).$2;
      final ordered = [...value]..sort((a, b) => b.monthly.compareTo(a.monthly));
      final names = ordered.map((s) => s.name).join(', ');
      for (final s in ordered.skip(1)) {
        offer(s, 'You pay for ${ordered.length} $plural ($names). You probably only need one: keep the one you use most.', 40);
      }
    }

    // 2. The price went up
    final since = DateTime(today.year, today.month - 13, today.day);
    for (final s in subs) {
      final charges = transactions.where((t) => t.isSpending && !t.date.isBefore(since) && Subscriptions.normalize(t.payee) == s.key).toList()
        ..sort((a, b) => a.date.compareTo(b.date));
      if (charges.length < 2) continue;
      final first = -charges.first.cents, last = -charges.last.cents;
      if (last - first < 100 || last < first * 1.10) continue;
      final existing = found[s.key];
      if (existing != null && existing.priority > 35) {
        found[s.key] = existing.withReason('${existing.reason} Its price also went up, from ${Money.format(first)} to ${Money.format(last)}.');
      } else {
        offer(s, 'The price went up from ${Money.format(first)} to ${Money.format(last)}. A good moment to ask whether it\'s still worth it, or to look for a cheaper plan.', 35);
      }
    }

    // 3. A yearly renewal coming up
    for (final s in subs.where((s) => s.repeats == BillSchedule.yearly && s.cents >= 3000)) {
      final days = dayOnly(s.nextCharge).difference(dayOnly(today)).inDays;
      if (days >= 0 && days <= 45) {
        offer(s, 'Renews on ${monthNames[s.nextCharge.month - 1]} ${s.nextCharge.day} for ${Money.format(s.cents)}. Cancel before then if you don\'t use it much.', 30);
      }
    }

    // 4. Over the Subscriptions budget: the priciest ones
    final budget = budgetFor('Subscriptions');
    if (budget != null) {
      final spent = spentIn(month, category: 'Subscriptions');
      final available = availableFor(budget, month);
      if (spent > available) {
        final top = [...subs]..sort((a, b) => b.monthly.compareTo(a.monthly));
        for (final s in top.take(2)) {
          offer(s, 'Your Subscriptions budget is over by ${Money.format(spent - available)} this month. This is one of the biggest.', 25);
        }
      }
    }

    // 5. The pricey ones: what they cost in a year
    for (final s in subs.where((s) => s.monthly >= 1500)) {
      offer(s, 'Costs about ${Money.format((s.monthly * 12).round(), whole: true)} a year. If you don\'t use it every week, it may not be worth it.', 10);
    }

    return found.values.where((c) => !isCutSkipped(c.key, month)).toList()
      ..sort((a, b) {
        final p = b.priority.compareTo(a.priority);
        return p != 0 ? p : b.monthlySaving.compareTo(a.monthlySaving);
      });
  }
}

// ======================================================================
// Budgets: templates, copying a month, averages
// ======================================================================

class BudgetTemplates {
  static const fiftyThirtyTwenty = '50/30/20';
  static const zeroBased = 'Zero-based';
  static const starter = 'Starter amounts';
  static const all = [fiftyThirtyTwenty, zeroBased, starter];

  static const needs = ['Housing', 'Utilities', 'Groceries', 'Transportation', 'Health', 'Kids', 'Pets', 'Personal care', 'Debt payments', 'Taxes'];
  static const wants = ['Dining out', 'Entertainment', 'Shopping', 'Travel', 'Gifts', 'Subscriptions'];
  static const savingsGroup = ['Savings'];

  static String explain(String template) => switch (template) {
        fiftyThirtyTwenty =>
          '50% of income for needs (housing, bills, groceries, transport), 30% for wants (dining out, shopping, fun) and 20% for savings. Split within each group by what you\'ve spent recently.',
        zeroBased => 'Every dollar gets a job: each category gets what you\'ve spent on it recently, and anything left over goes to Savings.',
        _ => 'Typical starting amounts for common categories. Adjust them to fit your life.',
      };

  static int suggestedLimit(String category) => switch (category) {
        'Housing' => 150000,
        'Groceries' => 60000,
        'Utilities' => 25000,
        'Transportation' => 30000,
        'Dining out' => 25000,
        'Shopping' => 20000,
        'Entertainment' => 15000,
        'Health' => 10000,
        'Subscriptions' => 6000,
        'Debt payments' => 30000,
        'Savings' => 20000,
        _ => 10000,
      };

  /// Amounts for a template, in cents (rounded to $5).
  static Map<String, int> plan(String template, int incomeCents, Map<String, int> averageSpending, List<String> visible, {String? Function(String)? parentOf}) {
    final plan = <String, int>{};
    int round5(double v) => max(500, (v / 500).round() * 500);
    if (template == starter) {
      for (final c in ['Housing', 'Groceries', 'Utilities', 'Transportation', 'Dining out', 'Shopping', 'Entertainment', 'Health', 'Subscriptions', 'Savings']) {
        if (visible.contains(c)) plan[c] = suggestedLimit(c);
      }
      return plan;
    }
    if (template == zeroBased) {
      for (final MapEntry(:key, :value) in averageSpending.entries) {
        if (value > 0 && visible.contains(key) && (parentOf?.call(key) == null)) plan[key] = round5(((value + 99) ~/ 100 * 100).toDouble());
      }
      final left = incomeCents - plan.values.fold<int>(0, (s, v) => s + v);
      if (left > 0 && visible.contains('Savings')) plan['Savings'] = (plan['Savings'] ?? 0) + (left ~/ 500) * 500;
      return plan;
    }
    void group(List<String> cats, double share) {
      final present = cats.where(visible.contains).toList();
      if (present.isEmpty) return;
      final weights = {for (final c in present) c: (averageSpending[c] ?? 0) > 0 ? averageSpending[c]!.toDouble() : 0.0};
      if (weights.values.fold<double>(0, (s, v) => s + v) == 0) {
        for (final c in present.where((c) => c != 'Kids' && c != 'Pets' && c != 'Taxes')) {
          weights[c] = suggestedLimit(c).toDouble();
        }
      }
      final total = weights.values.fold<double>(0, (s, v) => s + v);
      if (total == 0) return;
      for (final MapEntry(:key, :value) in weights.entries) {
        if (value > 0) plan[key] = round5(incomeCents * share * value / total);
      }
    }

    group(needs, 0.50);
    group(wants, 0.30);
    group(savingsGroup, 0.20);
    return plan;
  }
}

extension BudgetMath on Store {
  /// Average monthly spending per category over the last few complete budget months.
  Map<String, int> averageSpending({int months = 3, DateTime? now}) {
    final current = budgetMonth(now ?? DateTime.now());
    final totals = <String, int>{};
    for (var i = 1; i <= months; i++) {
      final m = DateTime(current.year, current.month - i, current.day);
      for (final MapEntry(:key, :value) in spentByCategory(m).entries) {
        totals[key] = (totals[key] ?? 0) + value;
      }
    }
    return {for (final MapEntry(:key, :value) in totals.entries) key: (value / months).round()};
  }

  /// Monthly income for templates: expected regular income, else the recent average.
  int incomeForTemplates({DateTime? now}) {
    final expected = expectedMonthlyIncome();
    if (expected > 0) return expected;
    final current = budgetMonth(now ?? DateTime.now());
    final values = [for (var i = 1; i <= 3; i++) incomeIn(DateTime(current.year, current.month - i, current.day))].where((v) => v > 0).toList();
    return values.isEmpty ? incomeIn(current) : (values.fold<int>(0, (s, v) => s + v) / values.length).round();
  }

  /// Creates (or replaces, with [replace]) budgets from a plan. Returns how many were set.
  int applyBudgetPlan(Map<String, int> plan, {required bool replace}) => batch(() {
        var n = 0;
        for (final MapEntry(:key, :value) in plan.entries) {
          final existing = budgetFor(key);
          if (existing != null && !replace) continue;
          saveBudget(existing == null ? Budget(category: key, limitCents: value) : existing.copyWith(limitCents: value));
          n++;
        }
        return n;
      });

  /// Uses last month's amounts for [month] (as one-month amounts). Returns how many changed.
  int copyLastMonth(DateTime month) => batch(() {
        final m = budgetMonth(month);
        final prev = DateTime(m.year, m.month - 1, m.day);
        final key = isoDay(m).substring(0, 10);
        var n = 0;
        for (final b in budgets) {
          final last = limitFor(b, prev);
          if (last == limitFor(b, m)) continue;
          final o = {...b.overrides};
          if (last == b.limitCents) {
            o.remove(key);
          } else {
            o[key] = last;
          }
          saveBudget(b.copyWith(overrides: o));
          n++;
        }
        return n;
      });

  /// Sets (or clears, with null) a one-month amount.
  void setMonthAmount(Budget b, DateTime month, int? cents) {
    final key = isoDay(budgetMonth(month)).substring(0, 10);
    final o = {...b.overrides};
    if (cents == null || cents == b.limitCents) {
      o.remove(key);
    } else {
      o[key] = cents;
    }
    saveBudget(b.copyWith(overrides: o));
  }
}

// ======================================================================
// Reports
// ======================================================================

class MonthTotals {
  MonthTotals(this.month, this.income, this.spent);
  final DateTime month;
  final int income, spent;
  int get net => income - spent;
  double? get savingsRate => income <= 0 ? null : (income - spent) / income;
}

class YearReview {
  YearReview({
    required this.year,
    required this.income,
    required this.spent,
    required this.count,
    required this.months,
    required this.topCategories,
    required this.topPayees,
    required this.biggest,
    required this.debtPaid,
    required this.savedToGoals,
    required this.billsPaid,
  });
  final int year, income, spent, count;
  final List<MonthTotals> months;
  final List<MapEntry<String, int>> topCategories;
  final List<(String, int, int)> topPayees;
  final Txn? biggest;
  final int debtPaid, savedToGoals, billsPaid;
  int get net => income - spent;
  double? get savingsRate => income <= 0 ? null : (income - spent) / income;
}

extension Reports on Store {
  /// Totals for [count] budget months ending with [last] (oldest first).
  List<MonthTotals> monthlyTotals(DateTime last, int count) {
    final end = budgetMonth(last);
    return [
      for (var i = count - 1; i >= 0; i--)
        () {
          final m = DateTime(end.year, end.month - i, end.day);
          return MonthTotals(m, incomeIn(m), spentIn(m));
        }(),
    ];
  }

  /// Spending per category between two dates (inclusive), largest first. Subcategories roll up into their parent.
  List<MapEntry<String, int>> spendingByCategory(DateTime from, DateTime to, {Iterable<Txn>? list}) {
    final totals = <String, int>{};
    for (final t in list ?? transactions) {
      if (!t.isSpending || dayOnly(t.date).isBefore(dayOnly(from)) || dayOnly(t.date).isAfter(dayOnly(to))) continue;
      for (final (c, cents) in t.parts()) {
        final k = rootOf(c);
        totals[k] = (totals[k] ?? 0) - cents;
      }
    }
    return totals.entries.toList()..sort((a, b) => b.value.compareTo(a.value));
  }

  /// Spending per payee between two dates: (payee, spent, count), largest first.
  List<(String, int, int)> spendingByPayee(DateTime from, DateTime to, {Iterable<Txn>? list}) {
    final totals = <String, (String, int, int)>{};
    for (final t in list ?? transactions) {
      if (!t.isSpending || dayOnly(t.date).isBefore(dayOnly(from)) || dayOnly(t.date).isAfter(dayOnly(to))) continue;
      final k = t.payee.trim().toLowerCase();
      final old = totals[k];
      totals[k] = (old?.$1 ?? t.payee.trim(), (old?.$2 ?? 0) - t.cents, (old?.$3 ?? 0) + 1);
    }
    return totals.values.toList()..sort((a, b) => b.$2.compareTo(a.$2));
  }

  /// Years that have any transactions, newest first (always includes this year).
  List<int> reviewYears() {
    final years = {DateTime.now().year, for (final t in transactions) t.date.year}.toList()..sort((a, b) => b.compareTo(a));
    return years;
  }

  YearReview yearReview(int year) {
    final tx = transactions.where((t) => t.date.year == year).toList();
    final from = DateTime(year), to = DateTime(year, 12, 31);
    Txn? biggest;
    for (final t in tx.where((t) => t.isSpending)) {
      if (biggest == null || t.cents < biggest.cents) biggest = t;
    }
    return YearReview(
      year: year,
      income: tx.where((t) => t.isIncome).fold<int>(0, (s, t) => s + t.cents),
      spent: tx.where((t) => t.isSpending).fold<int>(0, (s, t) => s - t.cents),
      count: tx.length,
      months: [
        for (var m = 1; m <= 12; m++)
          MonthTotals(
            DateTime(year, m),
            tx.where((t) => t.date.month == m && t.isIncome).fold<int>(0, (s, t) => s + t.cents),
            tx.where((t) => t.date.month == m && t.isSpending).fold<int>(0, (s, t) => s - t.cents),
          ),
      ],
      topCategories: spendingByCategory(from, to, list: tx).take(5).toList(),
      topPayees: spendingByPayee(from, to, list: tx).take(5).toList(),
      biggest: biggest,
      debtPaid: debts.expand((d) => d.payments).where((p) => p.date.year == year).fold<int>(0, (s, p) => s + p.cents),
      savedToGoals: goals.expand((g) => g.history).where((h) => h.date.year == year).fold<int>(0, (s, h) => s + h.cents),
      billsPaid: bills.where((b) => b.lastPaid?.year == year).length,
    );
  }
}
