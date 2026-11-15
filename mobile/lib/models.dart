import 'dart:math';

final _rng = Random.secure();

/// A random id like the desktop app's (32 hex characters).
String newId() => List.generate(16, (_) => _rng.nextInt(256).toRadixString(16).padLeft(2, '0')).join();

String _two(int n) => n.toString().padLeft(2, '0');
String _isoDay(DateTime d) => '${d.year.toString().padLeft(4, '0')}-${_two(d.month)}-${_two(d.day)}T00:00:00';
DateTime _date(Object? v, DateTime fallback) => v is String ? (DateTime.tryParse(v) ?? fallback) : fallback;
int _cents(Object? v) => v is num ? (v * 100).round() : 0;
final _epoch = DateTime.fromMillisecondsSinceEpoch(0, isUtc: true);

/// Keeps fields this app doesn't use (a record edited on the PC may have splits, tags, a receipt or a transfer),
/// so editing here never loses them.
Map<String, dynamic> _extra(Map<String, dynamic> j, Set<String> known) =>
    {for (final e in j.entries) if (!known.contains(e.key)) e.key: e.value};

/// One purchase or paycheck. [cents] is negative for money out and positive for money in.
/// Saved with the same field names as WillowWallet for Windows, so the two sync.
class Txn {
  Txn({
    String? id,
    required this.payee,
    required this.category,
    required this.account,
    required DateTime date,
    required this.cents,
    this.note = '',
    DateTime? createdAt,
    DateTime? updatedAt,
    Map<String, dynamic>? extra,
  })  : id = id ?? newId(),
        date = DateTime(date.year, date.month, date.day),
        createdAt = createdAt ?? DateTime.now(),
        updatedAt = updatedAt ?? DateTime.now(),
        extra = extra ?? const {};

  final String id;
  final String payee;
  final String category;
  final String account;
  final DateTime date;
  final int cents;
  final String note;
  final DateTime createdAt;
  final DateTime updatedAt;
  /// Fields from the Windows app this app shows but doesn't edit (Splits, Tags, Receipt, TransferAccount, ImportId…).
  final Map<String, dynamic> extra;

  /// The same transaction, changed just now (an undo counts as a new edit so it wins over a synced delete).
  Txn touched() => Txn.fromJson({...toJson(), 'UpdatedAt': DateTime.now().toUtc().toIso8601String()});

  /// Moves money between two accounts (made on the PC): neither spending nor income.
  bool get isTransfer => (extra['TransferAccount'] as String? ?? '').isNotEmpty;
  bool get isIncome => cents > 0 && !isTransfer;
  bool get isSpending => cents < 0 && !isTransfer;
  bool get isSplit => (extra['Splits'] as List? ?? const []).isNotEmpty;
  String get transferAccount => extra['TransferAccount'] as String? ?? '';
  List<String> get tags => [for (final t in (extra['Tags'] as List? ?? const [])) '$t'];

  /// Category pieces with signed cents: one normally, one per split, none for transfers (same as the PC).
  Iterable<(String, int)> parts() sync* {
    if (isTransfer) return;
    final splits = extra['Splits'] as List? ?? const [];
    if (splits.isEmpty) {
      yield (category, cents);
      return;
    }
    final sign = cents < 0 ? -1 : 1;
    for (final s in splits.cast<Map>()) {
      yield ('${s['Category'] ?? ''}', sign * _cents(s['Amount']).abs());
    }
  }

  /// "Groceries", "Split (3)" or "Transfer".
  String get categoryLabel => isTransfer ? 'Transfer' : isSplit ? 'Split (${(extra['Splits'] as List).length})' : category;

  static const _known = {'Id', 'Payee', 'Category', 'Account', 'Date', 'Amount', 'Note', 'CreatedAt', 'UpdatedAt'};

  Map<String, dynamic> toJson() => {
        ...extra,
        'Id': id,
        'Payee': payee,
        'Category': category,
        'Account': account,
        'Date': _isoDay(date),
        'Amount': cents / 100,
        'Note': note,
        'CreatedAt': createdAt.toUtc().toIso8601String(),
        'UpdatedAt': updatedAt.toUtc().toIso8601String(),
      };

  factory Txn.fromJson(Map<String, dynamic> j) => Txn(
        id: j['Id'] as String? ?? newId(),
        payee: j['Payee'] as String? ?? '',
        category: j['Category'] as String? ?? '',
        account: j['Account'] as String? ?? '',
        date: _date(j['Date'], DateTime.now()),
        cents: _cents(j['Amount']),
        note: j['Note'] as String? ?? '',
        createdAt: _date(j['CreatedAt'], _epoch),
        updatedAt: _date(j['UpdatedAt'], _epoch),
        extra: _extra(j, _known),
      );
}

/// A monthly spending limit for one category.
class Budget {
  Budget({String? id, required this.category, required this.limitCents, DateTime? createdAt, DateTime? updatedAt, Map<String, dynamic>? extra})
      : id = id ?? newId(),
        createdAt = createdAt ?? DateTime.now(),
        updatedAt = updatedAt ?? DateTime.now(),
        extra = extra ?? const {};

  final String id;
  final String category;
  final int limitCents;
  final DateTime createdAt;
  final DateTime updatedAt;
  /// Rollover, one-month amounts and start month from the PC.
  final Map<String, dynamic> extra;

  Budget touched() => Budget.fromJson({...toJson(), 'UpdatedAt': DateTime.now().toUtc().toIso8601String()});

  static const _known = {'Id', 'Category', 'MonthlyLimit', 'CreatedAt', 'UpdatedAt'};

  Map<String, dynamic> toJson() => {
        'Rollover': false,
        ...extra,
        'Id': id,
        'Category': category,
        'MonthlyLimit': limitCents / 100,
        'CreatedAt': createdAt.toUtc().toIso8601String(),
        'UpdatedAt': updatedAt.toUtc().toIso8601String(),
      };

  factory Budget.fromJson(Map<String, dynamic> j) => Budget(
        id: j['Id'] as String? ?? newId(),
        category: j['Category'] as String? ?? '',
        limitCents: _cents(j['MonthlyLimit']),
        createdAt: _date(j['CreatedAt'], _epoch),
        updatedAt: _date(j['UpdatedAt'], _epoch),
        extra: _extra(j, _known),
      );
}

class AppSettings {
  /// "system", "light" or "dark"
  String themeMode = 'system';
  String colorTheme = 'Pine';
  String currency = 'USD';

  Map<String, dynamic> toJson() => {'ThemeMode': themeMode, 'ColorTheme': colorTheme, 'Currency': currency};

  static AppSettings fromJson(Map<String, dynamic> j) => AppSettings()
    ..themeMode = j['ThemeMode'] as String? ?? 'system'
    ..colorTheme = j['ColorTheme'] as String? ?? 'Pine'
    ..currency = j['Currency'] as String? ?? 'USD';
}
