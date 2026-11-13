import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:flutter/widgets.dart';
import 'package:path_provider/path_provider.dart';

import 'categories.dart';
import 'dates.dart';
import 'models.dart';
import 'money.dart';
import 'sample.dart';

/// Everything CoinPilot keeps, saved as one JSON file in the app's private folder on the phone.
class Store extends ChangeNotifier {
  Store._(this._file);

  /// A store that isn't saved anywhere (tests and screenshots).
  factory Store.memory() => Store._(null);

  static Future<Store> open() async {
    final dir = await getApplicationDocumentsDirectory();
    final store = Store._(File('${dir.path}${Platform.pathSeparator}coinpilot.json'));
    await store._load();
    return store;
  }

  final File? _file;
  final List<Txn> _txns = [];
  final List<Budget> _budgets = [];
  AppSettings settings = AppSettings();
  Future<void> _writing = Future.value();

  List<Txn> get transactions => List.unmodifiable(_txns);
  List<Budget> get budgets => List.unmodifiable(_budgets..sort((a, b) => a.category.compareTo(b.category)));
  bool get isEmpty => _txns.isEmpty && _budgets.isEmpty;

  // ----- Saving -----

  Future<void> _load() async {
    final f = _file;
    if (f == null || !await f.exists()) return;
    try {
      fromJson(jsonDecode(await f.readAsString()) as Map<String, dynamic>);
    } catch (_) {
      // Keep the unreadable file for recovery and start fresh rather than crash.
      try {
        await f.copy('${f.path}.unreadable');
      } catch (_) {}
    }
    Money.currency = currencyFor(settings.currency);
  }

  Map<String, dynamic> toJson() => {
        'Version': 1,
        'Settings': settings.toJson(),
        'Transactions': [for (final t in _txns) t.toJson()],
        'Budgets': [for (final b in _budgets) b.toJson()],
      };

  void fromJson(Map<String, dynamic> j) {
    _txns
      ..clear()
      ..addAll([for (final t in (j['Transactions'] as List? ?? const [])) Txn.fromJson(t as Map<String, dynamic>)]);
    _budgets
      ..clear()
      ..addAll([for (final b in (j['Budgets'] as List? ?? const [])) Budget.fromJson(b as Map<String, dynamic>)]);
    final s = j['Settings'];
    settings = s is Map<String, dynamic> ? AppSettings.fromJson(s) : AppSettings();
    Money.currency = currencyFor(settings.currency);
  }

  void _changed() {
    Money.currency = currencyFor(settings.currency);
    notifyListeners();
    final f = _file;
    if (f == null) return;
    final data = const JsonEncoder.withIndent('  ').convert(toJson());
    // One write at a time; write to a temp file then swap so a crash never leaves half a file.
    _writing = _writing.then((_) async {
      final tmp = File('${f.path}.tmp');
      await tmp.writeAsString(data, flush: true);
      await tmp.rename(f.path);
    }).catchError((Object _) {});
  }

  /// Waits for pending saves (used by tests).
  Future<void> flush() => _writing;

  // ----- Changes -----

  void saveTxn(Txn t) {
    final i = _txns.indexWhere((x) => x.id == t.id);
    if (i >= 0) {
      _txns[i] = t;
    } else {
      _txns.add(t);
    }
    _changed();
  }

  Txn? deleteTxn(String id) {
    final i = _txns.indexWhere((x) => x.id == id);
    if (i < 0) return null;
    final t = _txns.removeAt(i);
    _changed();
    return t;
  }

  void saveBudget(Budget b) {
    final i = _budgets.indexWhere((x) => x.id == b.id || x.category == b.category);
    if (i >= 0) {
      _budgets[i] = b;
    } else {
      _budgets.add(b);
    }
    _changed();
  }

  void deleteBudget(String id) {
    _budgets.removeWhere((b) => b.id == id);
    _changed();
  }

  void updateSettings(void Function(AppSettings s) change) {
    change(settings);
    _changed();
  }

  void eraseAll() {
    _txns.clear();
    _budgets.clear();
    _changed();
  }

  void loadSample({DateTime? now}) {
    final data = sampleData(now ?? DateTime.now());
    _txns.addAll(data.transactions);
    for (final b in data.budgets) {
      if (budgetFor(b.category) == null) _budgets.add(b);
    }
    _changed();
  }

  // ----- Questions -----

  /// The month's transactions, newest first.
  List<Txn> inMonth(DateTime month) {
    final list = _txns.where((t) => sameMonth(t.date, month)).toList();
    list.sort((a, b) {
      final c = b.date.compareTo(a.date);
      return c != 0 ? c : b.createdAt.compareTo(a.createdAt);
    });
    return list;
  }

  /// Newest first across all months.
  List<Txn> recent([int count = 5]) {
    final list = [..._txns]..sort((a, b) {
        final c = b.date.compareTo(a.date);
        return c != 0 ? c : b.createdAt.compareTo(a.createdAt);
      });
    return list.take(count).toList();
  }

  /// Money spent in a month (positive), optionally for one category.
  int spentIn(DateTime month, {String? category}) => -_txns
      .where((t) => t.isSpending && sameMonth(t.date, month) && (category == null || t.category == category))
      .fold<int>(0, (s, t) => s + t.cents);

  int incomeIn(DateTime month) =>
      _txns.where((t) => t.isIncome && sameMonth(t.date, month)).fold<int>(0, (s, t) => s + t.cents);

  int get budgetedTotal => _budgets.fold<int>(0, (s, b) => s + b.limitCents);

  Budget? budgetFor(String category) {
    for (final b in _budgets) {
      if (b.category == category) return b;
    }
    return null;
  }

  /// Budgeted minus everything spent this month.
  int leftToSpend(DateTime month) => budgetedTotal - spentIn(month);

  /// Spending this month in categories without a budget, biggest first.
  List<MapEntry<String, int>> unbudgeted(DateTime month) {
    final totals = <String, int>{};
    for (final t in _txns) {
      if (!t.isSpending || !sameMonth(t.date, month) || budgetFor(t.category) != null) continue;
      totals[t.category] = (totals[t.category] ?? 0) - t.cents;
    }
    return totals.entries.toList()..sort((a, b) => b.value.compareTo(a.value));
  }

  /// Spending in each week of the month: days 1–7, 8–14, 15–21, 22–28 and 29 to the end.
  List<int> weeklySpending(DateTime month) {
    final weeks = List<int>.filled(5, 0);
    for (final t in _txns) {
      if (t.isSpending && sameMonth(t.date, month)) weeks[min(4, (t.date.day - 1) ~/ 7)] -= t.cents;
    }
    return weeks;
  }

  /// The category last used for this payee, so new transactions fill it in.
  String? categoryForPayee(String payee, {required bool income}) {
    final p = payee.trim().toLowerCase();
    if (p.isEmpty) return null;
    for (final t in recent(_txns.length)) {
      if (t.payee.toLowerCase() == p && t.isIncome == income) return t.category;
    }
    return null;
  }

  /// Payees used most recently (for quick picks), without repeats.
  List<String> recentPayees({required bool income, int count = 6}) {
    final seen = <String>{};
    final result = <String>[];
    for (final t in recent(_txns.length)) {
      if (t.isIncome != income || t.payee.isEmpty || !seen.add(t.payee.toLowerCase())) continue;
      result.add(t.payee);
      if (result.length == count) break;
    }
    return result;
  }

  List<String> get accounts {
    final used = {for (final t in _txns) t.account}..removeWhere((a) => a.isEmpty);
    return [...defaultAccounts, ...used.where((a) => !defaultAccounts.contains(a))];
  }

  /// A starting amount for a new budget: the average of the last three months, rounded up to $10.
  int suggestedBudget(String category, {DateTime? now}) {
    final month = monthStart(now ?? DateTime.now());
    var total = 0;
    for (var i = 1; i <= 3; i++) {
      total += spentIn(addMonths(month, -i), category: category);
    }
    if (total <= 0) return 0;
    final avg = total ~/ 3;
    return ((avg + 999) ~/ 1000) * 1000;
  }
}

/// Makes the [Store] available to every screen and sheet, and rebuilds them when it changes.
class StoreScope extends InheritedNotifier<Store> {
  const StoreScope({super.key, required Store store, required super.child}) : super(notifier: store);

  static Store of(BuildContext context) => context.dependOnInheritedWidgetOfExactType<StoreScope>()!.notifier!;

  /// Without listening for changes (for button handlers).
  static Store read(BuildContext context) => context.getInheritedWidgetOfExactType<StoreScope>()!.notifier!;
}
