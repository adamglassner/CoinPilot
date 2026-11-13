import 'dart:math';

final _rng = Random.secure();

/// A random id like the desktop app's (32 hex characters).
String newId() => List.generate(16, (_) => _rng.nextInt(256).toRadixString(16).padLeft(2, '0')).join();

String _two(int n) => n.toString().padLeft(2, '0');
String _isoDay(DateTime d) => '${d.year.toString().padLeft(4, '0')}-${_two(d.month)}-${_two(d.day)}T00:00:00';
DateTime _date(Object? v, DateTime fallback) => v is String ? (DateTime.tryParse(v) ?? fallback) : fallback;
int _cents(Object? v) => v is num ? (v * 100).round() : 0;

/// One purchase or paycheck. [cents] is negative for money out and positive for money in.
/// Saved with the same field names as WillowWallet for Windows, ready for syncing.
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
  })  : id = id ?? newId(),
        date = DateTime(date.year, date.month, date.day),
        createdAt = createdAt ?? DateTime.now(),
        updatedAt = updatedAt ?? DateTime.now();

  final String id;
  final String payee;
  final String category;
  final String account;
  final DateTime date;
  final int cents;
  final String note;
  final DateTime createdAt;
  final DateTime updatedAt;

  bool get isIncome => cents > 0;
  bool get isSpending => cents < 0;

  Map<String, dynamic> toJson() => {
        'Id': id,
        'Payee': payee,
        'Category': category,
        'Account': account,
        'Date': _isoDay(date),
        'Amount': cents / 100,
        'Note': note,
        'CreatedAt': createdAt.toIso8601String(),
        'UpdatedAt': updatedAt.toIso8601String(),
      };

  factory Txn.fromJson(Map<String, dynamic> j) {
    final now = DateTime.now();
    return Txn(
      id: j['Id'] as String? ?? newId(),
      payee: j['Payee'] as String? ?? '',
      category: j['Category'] as String? ?? '',
      account: j['Account'] as String? ?? '',
      date: _date(j['Date'], now),
      cents: _cents(j['Amount']),
      note: j['Note'] as String? ?? '',
      createdAt: _date(j['CreatedAt'], now),
      updatedAt: _date(j['UpdatedAt'], now),
    );
  }
}

/// A monthly spending limit for one category.
class Budget {
  Budget({String? id, required this.category, required this.limitCents, DateTime? createdAt, DateTime? updatedAt})
      : id = id ?? newId(),
        createdAt = createdAt ?? DateTime.now(),
        updatedAt = updatedAt ?? DateTime.now();

  final String id;
  final String category;
  final int limitCents;
  final DateTime createdAt;
  final DateTime updatedAt;

  Map<String, dynamic> toJson() => {
        'Id': id,
        'Category': category,
        'MonthlyLimit': limitCents / 100,
        'Rollover': false,
        'CreatedAt': createdAt.toIso8601String(),
        'UpdatedAt': updatedAt.toIso8601String(),
      };

  factory Budget.fromJson(Map<String, dynamic> j) {
    final now = DateTime.now();
    return Budget(
      id: j['Id'] as String? ?? newId(),
      category: j['Category'] as String? ?? '',
      limitCents: _cents(j['MonthlyLimit']),
      createdAt: _date(j['CreatedAt'], now),
      updatedAt: _date(j['UpdatedAt'], now),
    );
  }
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
