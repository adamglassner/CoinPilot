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
  /// File name of an attached receipt photo (kept in the app's receipts folder, synced like on the PC).
  String get receipt => extra['Receipt'] as String? ?? '';
  String get importId => extra['ImportId'] as String? ?? '';
  /// The split pieces as (category, cents), always positive.
  List<(String, int)> get splits => [
        for (final s in (extra['Splits'] as List? ?? const []).whereType<Map>()) ('${s['Category'] ?? ''}', _cents(s['Amount']).abs()),
      ];

  /// A copy with other fields changed (anything not given stays the same). Extra fields can be set or removed.
  Txn copyWith({String? payee, String? category, String? account, DateTime? date, int? cents, String? note, Map<String, dynamic>? extra}) => Txn(
        id: id,
        payee: payee ?? this.payee,
        category: category ?? this.category,
        account: account ?? this.account,
        date: date ?? this.date,
        cents: cents ?? this.cents,
        note: note ?? this.note,
        createdAt: createdAt,
        extra: {...this.extra, ...?extra},
      );

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

  /// Leftover money (or overspending) carries into the next month.
  bool get rollover => extra['Rollover'] == true;
  /// Amounts for single months, keyed by the month's start date ("yyyy-MM-dd").
  Map<String, int> get overrides => {
        for (final e in ((extra['Overrides'] as Map?) ?? const {}).entries) '${e.key}': _cents(e.value),
      };
  DateTime? get startMonth => extra['StartMonth'] is String ? DateTime.tryParse(extra['StartMonth'] as String) : null;

  Budget copyWith({int? limitCents, bool? rollover, Map<String, int>? overrides, String? category}) => Budget(
        id: id,
        category: category ?? this.category,
        limitCents: limitCents ?? this.limitCents,
        createdAt: createdAt,
        extra: {
          ...extra,
          if (rollover != null) 'Rollover': rollover,
          if (overrides != null) 'Overrides': {for (final e in overrides.entries) e.key: e.value / 100},
          if (rollover == true && extra['StartMonth'] == null) 'StartMonth': _isoDay(DateTime(DateTime.now().year, DateTime.now().month)),
        },
      );

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

/// Settings for this phone only (not synced).
class AppSettings {
  /// "system", "light" or "dark"
  String themeMode = 'system';
  String colorTheme = 'Pine';
  String currency = 'USD';

  /// App lock: "" (off), "PIN" or "Password", with a salted PBKDF2 hash of it.
  String lockKind = '';
  String lockSalt = '';
  String lockHash = '';
  /// Minutes away before the app asks again (0 = every time it opens).
  int autoLockMinutes = 1;

  /// Updates: when GitHub was last checked, and a version you chose to skip.
  String lastUpdateCheck = '';
  String skippedVersion = '';

  /// Unlock with a fingerprint, face or Touch ID instead of typing (the PIN or password still works).
  bool biometricUnlock = false;

  /// Reminders already shown on Mac and Linux ("billId|due|today"), and the last day Android showed overdue bills.
  List<String> remindersShown = [];
  String lastReminderDay = '';

  /// The version What's new was last shown for (it opens once after each update).
  String lastSeenVersion = '';

  /// Mac and Linux: start WillowWallet when you sign in (for bill reminders), like "Run in the background" on Windows.
  bool startAtLogin = false;

  /// The phone's bottom bar: the three pages beside the + button (More is always last). Kept on this device only.
  List<String> navTabs = [...defaultNavTabs];
  static const defaultNavTabs = ['home', 'transactions', 'budgets'];

  bool get isLocked => lockKind.isNotEmpty && lockHash.isNotEmpty;

  Map<String, dynamic> toJson() => {
        'ThemeMode': themeMode,
        'ColorTheme': colorTheme,
        'Currency': currency,
        'LockKind': lockKind,
        'LockSalt': lockSalt,
        'LockHash': lockHash,
        'AutoLockMinutes': autoLockMinutes,
        'LastUpdateCheck': lastUpdateCheck,
        'SkippedVersion': skippedVersion,
        'BiometricUnlock': biometricUnlock,
        'RemindersShown': remindersShown,
        'LastReminderDay': lastReminderDay,
        'LastSeenVersion': lastSeenVersion,
        'StartAtLogin': startAtLogin,
        'NavTabs': navTabs,
      };

  static AppSettings fromJson(Map<String, dynamic> j) => AppSettings()
    ..themeMode = j['ThemeMode'] as String? ?? 'system'
    ..colorTheme = j['ColorTheme'] as String? ?? 'Pine'
    ..currency = j['Currency'] as String? ?? 'USD'
    ..lockKind = j['LockKind'] as String? ?? ''
    ..lockSalt = j['LockSalt'] as String? ?? ''
    ..lockHash = j['LockHash'] as String? ?? ''
    ..autoLockMinutes = (j['AutoLockMinutes'] as num?)?.toInt() ?? 1
    ..lastUpdateCheck = j['LastUpdateCheck'] as String? ?? ''
    ..skippedVersion = j['SkippedVersion'] as String? ?? ''
    ..biometricUnlock = j['BiometricUnlock'] == true
    ..remindersShown = [for (final x in (j['RemindersShown'] as List? ?? const [])) '$x']
    ..lastReminderDay = j['LastReminderDay'] as String? ?? ''
    ..lastSeenVersion = j['LastSeenVersion'] as String? ?? ''
    ..startAtLogin = j['StartAtLogin'] == true
    ..navTabs = [for (final x in (j['NavTabs'] as List? ?? AppSettings.defaultNavTabs)) '$x'];
}
