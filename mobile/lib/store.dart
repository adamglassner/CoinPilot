import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:flutter/widgets.dart';
import 'package:path_provider/path_provider.dart';

import 'categories.dart';
import 'dates.dart';
import 'form.dart';
import 'models.dart';
import 'money.dart';
import 'records.dart';
import 'sample.dart';

/// Everything WillowWallet keeps, saved as one JSON file in the app's private folder on the phone
/// (receipts and tax documents are files next to it).
class Store extends ChangeNotifier {
  Store._(this._file, this.filesDir);

  /// A store that isn't saved anywhere (tests and screenshots).
  factory Store.memory() => Store._(null, null);

  static Future<Store> open() async {
    // The phone's private app folder; on a Mac or Linux computer the app's support folder
    // (~/Library/Application Support/… or ~/.local/share/…), not Documents.
    final dir = isDesktop ? await getApplicationSupportDirectory() : await getApplicationDocumentsDirectory();
    await dir.create(recursive: true);
    final store = Store._(File('${dir.path}${Platform.pathSeparator}coinpilot.json'), dir);
    await store._load();
    return store;
  }

  final File? _file;

  /// The app's private folder (receipts/ and taxes/<year>/ live here). Null in tests.
  final Directory? filesDir;

  final List<Txn> _txns = [];
  final List<Budget> _budgets = [];
  AppSettings settings = AppSettings();
  Future<void> _writing = Future.value();
  int _batch = 0;
  bool _batchChanged = false, _batchLocal = false;

  /// Kinds of records kept as JSON (the same as the Windows app's files), by collection then id.
  static const recordCollections = ['accounts', 'debts', 'bills', 'goals', 'categories', 'rules', 'income', 'tax-documents', 'settings'];
  final Map<String, Map<String, Map<String, dynamic>>> _raw = {for (final c in recordCollections) c: {}};

  /// The sync engine's saved state (server, session, what the server has). Empty when not syncing.
  Map<String, dynamic> syncState = {};

  /// Called after every change made on this phone (the sync engine syncs a few seconds later).
  VoidCallback? onLocalChange;

  List<Txn> get transactions => List.unmodifiable(_txns);
  List<Budget> get budgets => List.unmodifiable(_budgets..sort((a, b) => a.category.compareTo(b.category)));

  List<Map<String, dynamic>> _list(String c) => _raw[c]!.values.toList();
  List<Account> get accountRecords => [for (final j in _list('accounts')) Account(j)]..sort((a, b) => a.name.toLowerCase().compareTo(b.name.toLowerCase()));
  List<Debt> get debts => [for (final j in _list('debts')) Debt(j)];
  List<Bill> get bills => [for (final j in _list('bills')) Bill(j)];
  List<Goal> get goals => [for (final j in _list('goals')) Goal(j)];
  List<CategoryDef> get categoryDefs => [for (final j in _list('categories')) CategoryDef(j)]..sort((a, b) => a.order.compareTo(b.order));
  List<Rule> get rules => [for (final j in _list('rules')) Rule(j)];
  List<IncomeSource> get incomeSources => [for (final j in _list('income')) IncomeSource(j)];
  List<TaxDoc> get taxDocs => [for (final j in _list('tax-documents')) TaxDoc(j)];
  Prefs get prefs => _raw['settings']!['preferences'] == null ? Prefs.defaults() : Prefs(_raw['settings']!['preferences']!);
  DebtPlanSettings get debtPlan =>
      _raw['settings']!['debt-plan'] == null ? DebtPlanSettings.defaults() : DebtPlanSettings(_raw['settings']!['debt-plan']!);

  Debt? debtById(String id) => _raw['debts']![id] == null ? null : Debt(_raw['debts']![id]!);

  /// Nothing entered yet.
  bool get isEmpty =>
      _txns.isEmpty &&
      _budgets.isEmpty &&
      ['accounts', 'debts', 'bills', 'goals', 'income', 'tax-documents'].every((c) => _raw[c]!.isEmpty);

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
        'Version': 2,
        'Settings': settings.toJson(),
        'Transactions': [for (final t in _txns) t.toJson()],
        'Budgets': [for (final b in _budgets) b.toJson()],
        'Raw': _raw,
        'Sync': syncState,
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
    final raw = j['Raw'];
    for (final c in recordCollections) {
      _raw[c] = {
        if (raw is Map && raw[c] is Map)
          for (final e in (raw[c] as Map).entries) '${e.key}': Map<String, dynamic>.from(e.value as Map),
      };
    }
    syncState = j['Sync'] is Map<String, dynamic> ? j['Sync'] as Map<String, dynamic> : {};
    Money.currency = currencyFor(settings.currency);
  }

  /// Groups many changes (an import, paying a bill) into one save and one refresh.
  T batch<T>(T Function() changes) {
    _batch++;
    try {
      return changes();
    } finally {
      if (--_batch == 0 && _batchChanged) {
        _batchChanged = false;
        final local = _batchLocal;
        _batchLocal = false;
        _changed(local: local);
      }
    }
  }

  void _changed({bool local = true}) {
    if (_batch > 0) {
      _batchChanged = true;
      _batchLocal |= local;
      return;
    }
    Money.currency = currencyFor(settings.currency);
    notifyListeners();
    if (local) onLocalChange?.call();
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

  void saveTxns(Iterable<Txn> list) => batch(() {
        for (final t in list) {
          saveTxn(t);
        }
      });

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

  /// Saves a record (new or changed). It gets a new UpdatedAt so it syncs.
  void put(Rec r) {
    r.j['UpdatedAt'] = stamp();
    if (r.collection != 'settings') r.j['CreatedAt'] ??= stamp();
    _raw[r.collection]![r.id] = r.j;
    _changed();
  }

  void remove(Rec r) {
    if (_raw[r.collection]!.remove(r.id) != null) _changed();
  }

  void updateSettings(void Function(AppSettings s) change) {
    change(settings);
    _changed(local: false);
  }

  /// Changes the synced preferences (budget month, dismissed suggestions…).
  void updatePrefs(void Function(Prefs p) change) {
    final p = prefs.copy();
    change(p);
    put(p);
  }

  void updateDebtPlan(void Function(DebtPlanSettings p) change) {
    final p = debtPlan.copy();
    change(p);
    put(p);
  }

  /// Saves the sync engine's state (no sync is triggered by this).
  void saveSyncState() => _changed(local: false);

  /// Deletes all data (and, when syncing, on the other devices too). Settings like the theme stay.
  void eraseAll() {
    _txns.clear();
    _budgets.clear();
    for (final c in recordCollections) {
      if (c != 'settings') _raw[c]!.clear();
    }
    final p = prefs.copy()
      ..setupDone = false
      ..dismissedSubscriptions = []
      ..skippedCuts = [];
    p.j['WelcomeDismissed'] = false;
    _raw['settings']!['preferences'] = p.j..['UpdatedAt'] = stamp();
    final d = debtPlan.copy()..extraCents = 0;
    _raw['settings']!['debt-plan'] = d.j..['UpdatedAt'] = stamp();
    _deleteFolder('receipts');
    _deleteFolder('taxes');
    _changed();
  }

  void _deleteFolder(String name) {
    final dir = filesDir;
    if (dir == null) return;
    try {
      final d = Directory('${dir.path}${Platform.pathSeparator}$name');
      if (d.existsSync()) d.deleteSync(recursive: true);
    } catch (_) {}
  }

  // ----- Files (receipts and tax documents) -----

  String? receiptPath(String fileName) => filesDir == null || fileName.isEmpty ? null : '${filesDir!.path}/receipts/$fileName';
  String? taxPath(TaxDoc d) => filesDir == null || d.fileName.isEmpty ? null : '${filesDir!.path}/taxes/${d.year}/${d.fileName}';

  /// Every file a record uses, as (name on the server, path here).
  List<(String, String)> wantedFiles() => [
        for (final t in _txns)
          if (t.receipt.isNotEmpty && receiptPath(t.receipt) != null) ('receipt/${t.receipt}', receiptPath(t.receipt)!),
        for (final d in taxDocs)
          if (d.fileName.isNotEmpty && taxPath(d) != null) ('tax/${d.fileName}', taxPath(d)!),
      ];

  /// Copies a file into the app's folder under a new random name. Returns that name.
  Future<String> keepFile(List<int> bytes, String extension, {required String folder}) async {
    final ext = extension.startsWith('.') ? extension.toLowerCase() : '.${extension.toLowerCase()}';
    final name = '${newId()}${ext == '.' ? '' : ext}';
    final dir = filesDir;
    if (dir != null) {
      final f = File('${dir.path}/$folder/$name');
      await f.parent.create(recursive: true);
      await f.writeAsBytes(bytes, flush: true);
    }
    return name;
  }

  // ----- Sync -----

  /// Collections this app edits and sends to the server: all of them.
  static const editable = ['transactions', 'budgets', ...recordCollections];

  static int _ms(Object? updatedAt) => DateTime.tryParse('${updatedAt ?? ''}')?.millisecondsSinceEpoch ?? 0;

  /// (id, last change in ms) of every record in a collection.
  List<(String, int)> syncEntries(String collection) => switch (collection) {
        'transactions' => [for (final t in _txns) (t.id, t.updatedAt.millisecondsSinceEpoch)],
        'budgets' => [for (final b in _budgets) (b.id, b.updatedAt.millisecondsSinceEpoch)],
        // Settings that were never changed anywhere (no UpdatedAt) aren't sent.
        'settings' => [
            for (final e in _raw['settings']!.entries)
              if (_ms(e.value['UpdatedAt']) > 0) (e.key, _ms(e.value['UpdatedAt'])),
          ],
        _ => [for (final e in (_raw[collection] ?? const <String, Map<String, dynamic>>{}).entries) (e.key, _ms(e.value['UpdatedAt']))],
      };

  /// The record's JSON (with every field from the PC), or null.
  Map<String, dynamic>? syncRecord(String collection, String id) {
    switch (collection) {
      case 'transactions':
        for (final t in _txns) {
          if (t.id == id) return t.toJson();
        }
      case 'budgets':
        for (final b in _budgets) {
          if (b.id == id) return b.toJson();
        }
      default:
        return _raw[collection]?[id];
    }
    return null;
  }

  int? syncModified(String collection, String id) {
    final r = syncRecord(collection, id);
    if (r == null) return null;
    return DateTime.tryParse('${r['UpdatedAt'] ?? ''}')?.millisecondsSinceEpoch;
  }

  /// Whether the server's records of this kind are kept here.
  bool keeps(String collection) => editable.contains(collection);

  /// Records from the server: added or replaced without being sent back.
  void applyRemote(String collection, List<Map<String, dynamic>> records, {List<String>? ids}) {
    for (final (n, j) in records.indexed) {
      // Settings files from the PC have no Id field: it's the file's name.
      var id = j['Id'] as String? ?? '';
      if (id.isEmpty && ids != null && n < ids.length) {
        id = ids[n];
        j['Id'] = id;
      }
      if (id.isEmpty) continue;
      switch (collection) {
        case 'transactions':
          final t = Txn.fromJson(j);
          final i = _txns.indexWhere((x) => x.id == id);
          i >= 0 ? _txns[i] = t : _txns.add(t);
        case 'budgets':
          final b = Budget.fromJson(j);
          final i = _budgets.indexWhere((x) => x.id == id);
          i >= 0 ? _budgets[i] = b : _budgets.add(b);
        case 'settings':
          if (id == 'preferences' || id == 'debt-plan') _raw['settings']![id] = j;
        default:
          _raw[collection]?[id] = j;
      }
    }
    _changed(local: false);
  }

  void removeRemote(String collection, List<String> ids) {
    final set = ids.toSet();
    switch (collection) {
      case 'transactions':
        _txns.removeWhere((t) => set.contains(t.id));
      case 'budgets':
        _budgets.removeWhere((b) => set.contains(b.id));
      case 'settings':
        break; // settings are never deleted
      default:
        _raw[collection]?.removeWhere((k, _) => set.contains(k));
    }
    _changed(local: false);
  }

  /// Empties this phone's copy so it can be replaced with the server's ("use only the server's data").
  void clearForServer() {
    _txns.clear();
    _budgets.clear();
    for (final c in _raw.values) {
      c.clear();
    }
    _changed(local: false);
  }

  /// Replaces everything with data from a backup (records by collection, the way the Windows app saves them).
  void replaceAll(Map<String, List<Map<String, dynamic>>> data) {
    _txns
      ..clear()
      ..addAll([for (final j in data['transactions'] ?? const <Map<String, dynamic>>[]) Txn.fromJson(j)]);
    _budgets
      ..clear()
      ..addAll([for (final j in data['budgets'] ?? const <Map<String, dynamic>>[]) Budget.fromJson(j)]);
    for (final c in recordCollections) {
      _raw[c] = {
        for (final j in data[c] ?? const <Map<String, dynamic>>[])
          if ('${j['Id'] ?? ''}'.isNotEmpty) '${j['Id']}': j,
      };
    }
    _changed();
  }

  void loadSample({DateTime? now}) {
    // (sample data counts as a change made here)
    final data = sampleData(now ?? DateTime.now());
    batch(() {
      _txns.addAll(data.transactions);
      for (final b in data.budgets) {
        if (budgetFor(b.category) == null) _budgets.add(b);
      }
      for (final r in data.records) {
        if (r is Account && accountRecords.any((a) => a.name == r.name)) continue;
        _raw[r.collection]![r.id] = r.j;
      }
      _changed();
    });
  }

  // ----- Budget months -----

  /// Day of the month budget months start on (Settings → Budget month). 1 = calendar months.
  int get monthStartDay => prefs.monthStartDay;

  /// First day of the budget month that contains [d].
  DateTime budgetMonth(DateTime d) {
    final sd = monthStartDay;
    final start = DateTime(d.year, d.month, sd);
    return !dayOnly(d).isBefore(start) ? start : DateTime(d.year, d.month - 1, sd);
  }

  /// Last day of the budget month starting on [month].
  DateTime monthEnd(DateTime month) {
    final m = budgetMonth(month);
    return DateTime(m.year, m.month + 1, m.day - 1);
  }

  bool inBudgetMonth(DateTime date, DateTime month) {
    final start = budgetMonth(month);
    final d = dayOnly(date);
    return !d.isBefore(start) && d.isBefore(DateTime(start.year, start.month + 1, start.day));
  }

  /// "November 2026", or "Nov 15 – Dec 14" when budget months start mid-month.
  String monthTitle(DateTime month) {
    final m = budgetMonth(month);
    if (monthStartDay == 1) return monthLabel(m);
    final end = monthEnd(m);
    return '${shortMonths[m.month - 1]} ${m.day} – ${shortMonths[end.month - 1]} ${end.day}';
  }

  /// "November" or "Nov 15 – Dec 14".
  String monthName(DateTime month) => monthStartDay == 1 ? monthNames[budgetMonth(month).month - 1] : monthTitle(month);

  // ----- Questions -----

  /// The month's transactions, newest first.
  List<Txn> inMonth(DateTime month) {
    final list = _txns.where((t) => inBudgetMonth(t.date, month)).toList();
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

  /// Spending per category in a month (split pieces counted separately).
  Map<String, int> spentByCategory(DateTime month) {
    final totals = <String, int>{};
    for (final t in _txns) {
      if (!t.isSpending || !inBudgetMonth(t.date, month)) continue;
      for (final (c, cents) in t.parts()) {
        totals[c] = (totals[c] ?? 0) - cents;
      }
    }
    return totals;
  }

  /// What counts against a budget: its category, plus subcategories without a budget of their own.
  int _spentFor(String category, Map<String, int> spent) {
    var total = spent[category] ?? 0;
    for (final child in childrenOf(category)) {
      if (budgetFor(child) == null) total += spent[child] ?? 0;
    }
    return total;
  }

  /// Money spent in a month (positive), optionally for one category (its subcategories count too).
  int spentIn(DateTime month, {String? category}) {
    if (category == null) {
      var total = 0;
      for (final t in _txns) {
        if (t.isSpending && inBudgetMonth(t.date, month)) total -= t.cents;
      }
      return total;
    }
    return _spentFor(category, spentByCategory(month));
  }

  // ----- Categories -----

  /// The categories in use: saved ones (from either device), or the built-in list.
  List<CategoryDef> get _defs {
    final saved = categoryDefs;
    if (saved.isNotEmpty) return saved;
    return [
      for (final (i, n) in spendingCategories.indexed) CategoryDef({'Name': n, 'Kind': 'Spending', 'Parent': '', 'Order': i}),
      for (final (i, n) in incomeCategories.indexed) CategoryDef({'Name': n, 'Kind': 'Income', 'Parent': '', 'Order': 100 + i}),
    ];
  }

  CategoryDef? _def(String name) {
    final n = name.toLowerCase();
    for (final c in _defs) {
      if (c.name.toLowerCase() == n) return c;
    }
    return null;
  }

  /// A subcategory's parent ("Coffee" → "Dining out"); null for top-level ones.
  String? parentOf(String category) {
    final p = _def(category)?.parent ?? '';
    return p.isEmpty ? null : p;
  }

  /// The top-level category ("Coffee" → "Dining out").
  String rootOf(String category) => parentOf(category) ?? category;

  /// "Dining out › Coffee" for a subcategory.
  String displayCategory(String category) {
    final p = parentOf(category);
    return p == null ? category : '$p › $category';
  }

  Iterable<String> childrenOf(String category) {
    final n = category.toLowerCase();
    return _defs.where((c) => c.parent.toLowerCase() == n).map((c) => c.name);
  }

  bool isIncomeCategory(String name) => _def(name)?.isIncome ?? incomeCategories.contains(name);

  /// Every category of a kind in order (subcategories right after their parent), hidden ones included.
  List<CategoryDef> allCategories({required bool income}) {
    final defs = _defs.where((c) => c.isIncome == income).toList();
    final result = <CategoryDef>[];
    for (final top in defs.where((c) => c.parent.isEmpty)) {
      result.add(top);
      result.addAll(defs.where((c) => c.parent.toLowerCase() == top.name.toLowerCase()));
    }
    result.addAll(defs.where((c) => !result.contains(c)));
    return result;
  }

  /// Categories to pick from (hidden ones left out).
  List<String> categoriesFor({required bool income}) =>
      [for (final c in allCategories(income: income)) if (!c.isHidden) c.name];

  /// Saves the built-in categories as records before the first change, the same as the PC.
  void _ensureCategoriesSaved() {
    if (_raw['categories']!.isNotEmpty) return;
    for (final c in _defs) {
      final r = CategoryDef(Rec.fresh(c.j));
      _raw['categories']![r.id] = r.j;
    }
  }

  void addCategory(String name, {required bool income, String parent = ''}) => batch(() {
        _ensureCategoriesSaved();
        final order = categoryDefs.fold<int>(0, (m, c) => max(m, c.order)) + 1;
        put(CategoryDef.create(name: name.trim(), income: income, parent: parent, order: order));
      });

  void setCategoryHidden(String name, bool hidden) => batch(() {
        _ensureCategoriesSaved();
        for (final c in categoryDefs) {
          if (c.name == name) put(c.copy()..isHidden = hidden);
        }
      });

  bool isCategoryUsed(String name) =>
      _txns.any((t) => t.category == name || t.splits.any((s) => s.$1 == name)) ||
      _budgets.any((b) => b.category == name) ||
      bills.any((b) => b.category == name);

  void deleteCategory(String name) => batch(() {
        _ensureCategoriesSaved();
        for (final c in categoryDefs) {
          if (c.name == name) remove(c);
        }
      });

  /// Renames a category everywhere it's used (transactions, splits, budgets, bills and rules).
  void renameCategory(String oldName, String newName) => batch(() {
        newName = newName.trim();
        if (newName.isEmpty || newName == oldName) return;
        _ensureCategoriesSaved();
        for (final c in categoryDefs) {
          if (c.name == oldName || c.parent == oldName) {
            final copy = c.copy();
            if (copy.name == oldName) copy.name = newName;
            if (copy.parent == oldName) copy.parent = newName;
            put(copy);
          }
        }
        for (final t in [..._txns]) {
          final splits = t.extra['Splits'] as List? ?? const [];
          final usesSplit = splits.any((s) => s is Map && s['Category'] == oldName);
          if (t.category != oldName && !usesSplit) continue;
          saveTxn(t.copyWith(
            category: t.category == oldName ? newName : null,
            extra: usesSplit
                ? {
                    'Splits': [
                      for (final s in splits.whereType<Map>()) {...s, 'Category': s['Category'] == oldName ? newName : s['Category']},
                    ],
                  }
                : null,
          ));
        }
        for (final b in [..._budgets]) {
          if (b.category == oldName) saveBudget(b.copyWith(category: newName));
        }
        for (final b in bills) {
          if (b.category == oldName) put(b.copy()..category = newName);
        }
        for (final r in rules) {
          if (r.category == oldName) put(r.copy()..category = newName);
        }
      });

  // ----- Totals -----

  int incomeIn(DateTime month) =>
      _txns.where((t) => t.isIncome && inBudgetMonth(t.date, month)).fold<int>(0, (s, t) => s + t.cents);

  /// A budget's amount for one month: its single-month amount if it has one, otherwise the usual amount.
  int limitFor(Budget b, DateTime month) {
    final key = isoDay(budgetMonth(month)).substring(0, 10);
    return b.overrides[key] ?? b.limitCents;
  }

  /// Leftover (or overspending) carried into [month] from earlier months, for rollover budgets.
  int carryFor(Budget b, DateTime month) {
    if (!b.rollover) return 0;
    final created = b.startMonth ?? (b.createdAt.year < 2000 ? month : b.createdAt);
    var p = budgetMonth(created);
    final target = budgetMonth(month);
    var carry = 0;
    for (var guard = 0; p.isBefore(target) && guard < 240; guard++) {
      carry += limitFor(b, p) - _spentFor(b.category, spentByCategory(p));
      p = DateTime(p.year, p.month + 1, p.day);
    }
    return carry;
  }

  /// This month's amount plus anything rolled over.
  int availableFor(Budget b, DateTime month) => limitFor(b, month) + carryFor(b, month);

  int budgetedIn(DateTime month) => _budgets.fold<int>(0, (s, b) => s + limitFor(b, month));
  int availableIn(DateTime month) => _budgets.fold<int>(0, (s, b) => s + availableFor(b, month));

  int get budgetedTotal => budgetedIn(budgetMonth(DateTime.now()));

  Budget? budgetFor(String category) {
    for (final b in _budgets) {
      if (b.category == category) return b;
    }
    return null;
  }

  /// What's left in the budgets this month (available minus spent in budgeted categories).
  int leftToSpend(DateTime month) {
    final spent = spentByCategory(month);
    return _budgets.fold<int>(0, (s, b) => s + availableFor(b, month) - _spentFor(b.category, spent));
  }

  /// Spent this month in categories that have a budget.
  int spentInBudgets(DateTime month) {
    final spent = spentByCategory(month);
    return _budgets.fold<int>(0, (s, b) => s + _spentFor(b.category, spent));
  }

  /// Spending this month in categories without a budget, biggest first.
  List<MapEntry<String, int>> unbudgeted(DateTime month) {
    final totals = <String, int>{};
    for (final t in _txns) {
      if (!t.isSpending || !inBudgetMonth(t.date, month)) continue;
      for (final (c, cents) in t.parts()) {
        if (budgetFor(c) != null) continue;
        final top = parentOf(c) ?? c;
        if (budgetFor(top) != null) continue;
        totals[top] = (totals[top] ?? 0) - cents;
      }
    }
    return totals.entries.toList()..sort((a, b) => b.value.compareTo(a.value));
  }

  /// Spending in each week of the month: days 1–7, 8–14, 15–21, 22–28 and 29 to the end.
  List<int> weeklySpending(DateTime month) {
    final start = budgetMonth(month);
    final weeks = List<int>.filled(5, 0);
    for (final t in _txns) {
      if (t.isSpending && inBudgetMonth(t.date, month)) weeks[min(4, dayOnly(t.date).difference(start).inDays ~/ 7)] -= t.cents;
    }
    return weeks;
  }

  /// The category for a payee: a rule that matches, or the category last used for it.
  String? categoryForPayee(String payee, {required bool income}) {
    final p = payee.trim().toLowerCase();
    if (p.isEmpty) return null;
    Rule? best;
    for (final r in rules) {
      final c = r.contains.trim().toLowerCase();
      if (c.isEmpty || !p.contains(c) || isIncomeCategory(r.category) != income) continue;
      if (best == null || c.length > best.contains.length) best = r;
    }
    if (best != null) return best.category;
    for (final t in recent(_txns.length)) {
      if (t.isTransfer || t.isSplit) continue;
      if (t.payee.trim().toLowerCase() == p && t.isIncome == income) return t.category;
    }
    return null;
  }

  /// Payees used most recently (for quick picks), without repeats.
  List<String> recentPayees({required bool income, int count = 6}) {
    final seen = <String>{};
    final result = <String>[];
    for (final t in recent(_txns.length)) {
      if (t.isTransfer || t.isIncome != income || t.payee.isEmpty || !seen.add(t.payee.toLowerCase())) continue;
      result.add(t.payee);
      if (result.length == count) break;
    }
    return result;
  }

  /// Open accounts when there are any (else the defaults), plus names used in transactions. [current] is always included.
  List<String> accountNames({String? current}) {
    final open = [for (final a in accountRecords) if (!a.isClosed && a.name.isNotEmpty) a.name];
    final base = open.isNotEmpty ? open : [...defaultAccounts];
    final closed = {for (final a in accountRecords) if (a.isClosed) a.name};
    final used = {for (final t in _txns) t.account}..removeWhere((a) => a.isEmpty || base.contains(a) || closed.contains(a));
    final all = [...base, ...used];
    if (current != null && current.isNotEmpty && !all.contains(current)) all.add(current);
    return all;
  }

  /// A starting amount for a new budget: the average of the last three months, rounded up to $10.
  int suggestedBudget(String category, {DateTime? now}) {
    final month = budgetMonth(now ?? DateTime.now());
    var total = 0;
    for (var i = 1; i <= 3; i++) {
      total += spentIn(DateTime(month.year, month.month - i, month.day), category: category);
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
