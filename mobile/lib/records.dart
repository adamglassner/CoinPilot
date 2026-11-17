import 'dart:convert';

import 'models.dart' show newId;

// Records the Windows app has that this app now edits too: accounts, debts, bills, goals, categories, rules, income
// and tax documents, plus the two synced settings files (preferences and the debt plan).
//
// Each one wraps the record's JSON exactly as WillowWallet for Windows saves it (same field names, amounts as decimal
// numbers, dates as "2026-11-16T00:00:00"), so fields this app doesn't know about are never lost.

String _two(int n) => n.toString().padLeft(2, '0');

/// "2026-11-16T00:00:00", the way the Windows app saves a date.
String isoDay(DateTime d) => '${d.year.toString().padLeft(4, '0')}-${_two(d.month)}-${_two(d.day)}T00:00:00';

/// Now, for UpdatedAt / CreatedAt.
String stamp() => DateTime.now().toUtc().toIso8601String();

int toCents(Object? v) => v is num ? (v * 100).round() : (v is String ? ((double.tryParse(v) ?? 0) * 100).round() : 0);

DateTime? parseDate(Object? v) {
  if (v is! String || v.isEmpty) return null;
  final d = DateTime.tryParse(v);
  if (d == null) return null;
  // Dates are days: drop the time (and treat UTC dates as the same calendar day).
  return DateTime(d.year, d.month, d.day);
}

/// A dated amount: a debt payment, or money added to (positive) / taken from (negative) a goal.
class MoneyEntry {
  const MoneyEntry(this.date, this.cents);
  final DateTime date;
  final int cents;

  Map<String, dynamic> toJson() => {'Date': isoDay(date), 'Amount': cents / 100};
  static MoneyEntry fromJson(Object? j) {
    final m = j is Map ? j : const {};
    return MoneyEntry(parseDate(m['Date']) ?? DateTime(2000), toCents(m['Amount']));
  }
}

/// Base for every record: its JSON, with typed getters and setters on top.
abstract class Rec {
  Rec(this.j);

  /// The record as saved (and synced).
  final Map<String, dynamic> j;

  /// Which kind of record ("bills", "accounts"…), the same name the Windows app and the server use.
  String get collection;

  String get id => j['Id'] as String? ?? '';
  DateTime get updatedAt => DateTime.tryParse('${j['UpdatedAt'] ?? ''}') ?? DateTime.fromMillisecondsSinceEpoch(0);

  String str(String k) => j[k] is String ? j[k] as String : (j[k] == null ? '' : '${j[k]}');
  int money(String k) => toCents(j[k]);
  void setMoney(String k, int cents) => j[k] = cents / 100;
  bool flag(String k) => j[k] == true;
  int whole(String k, [int fallback = 0]) => j[k] is num ? (j[k] as num).toInt() : fallback;
  double number(String k) => j[k] is num ? (j[k] as num).toDouble() : 0;
  DateTime day(String k) => parseDate(j[k]) ?? _today();
  DateTime? dayOrNull(String k) => parseDate(j[k]);
  void setDay(String k, DateTime? d) => j[k] = d == null ? null : isoDay(d);
  List<MoneyEntry> entries(String k) => [for (final e in (j[k] as List? ?? const [])) MoneyEntry.fromJson(e)];
  void setEntries(String k, List<MoneyEntry> list) => j[k] = [for (final e in list) e.toJson()];
  List<String> strings(String k) => [for (final e in (j[k] as List? ?? const [])) '$e'];

  /// A deep copy of the JSON (edit a copy, then save it with Store.put).
  static Map<String, dynamic> copyOf(Map<String, dynamic> j) => jsonDecode(jsonEncode(j)) as Map<String, dynamic>;

  /// A new record's JSON with an id and timestamps.
  static Map<String, dynamic> fresh(Map<String, dynamic> fields) => {
        'Id': newId(),
        'CreatedAt': stamp(),
        'UpdatedAt': stamp(),
        ...fields,
      };
}

DateTime _today() {
  final n = DateTime.now();
  return DateTime(n.year, n.month, n.day);
}

// ----- Accounts -----

class AccountTypes {
  static const checking = 'Checking';
  static const savings = 'Savings';
  static const creditCard = 'Credit card';
  static const cash = 'Cash';
  static const paymentApp = 'Payment app';
  static const investment = 'Investment';
  static const other = 'Other';
  static const all = [checking, savings, creditCard, cash, paymentApp, investment, other];

  /// One-tap payment apps: the account name and the company behind it.
  static const paymentApps = [('PayPal', 'PayPal'), ('Venmo', 'PayPal'), ('Cash App', 'Block'), ('Apple Cash', 'Apple')];
}

/// A bank account, card or wallet. Its balance is [startingCents] on [balanceDate] plus every transaction after it.
/// Credit cards keep what's owed as a negative number.
class Account extends Rec {
  Account(super.j);
  factory Account.create({required String name, String type = AccountTypes.checking, String institution = '', int startingCents = 0, DateTime? balanceDate}) =>
      Account(Rec.fresh({
        'Name': name,
        'Type': type,
        'Institution': institution,
        'StartingBalance': startingCents / 100,
        'BalanceDate': isoDay(balanceDate ?? DateTime.now()),
        'IsClosed': false,
      }));
  Account copy() => Account(Rec.copyOf(j));
  @override
  String get collection => 'accounts';

  String get name => str('Name');
  set name(String v) => j['Name'] = v;
  String get type => str('Type').isEmpty ? AccountTypes.checking : str('Type');
  set type(String v) => j['Type'] = v;
  String get institution => str('Institution');
  set institution(String v) => j['Institution'] = v;
  int get startingCents => money('StartingBalance');
  set startingCents(int v) => setMoney('StartingBalance', v);
  DateTime get balanceDate => day('BalanceDate');
  set balanceDate(DateTime v) => setDay('BalanceDate', v);
  bool get isClosed => flag('IsClosed');
  set isClosed(bool v) => j['IsClosed'] = v;
  bool get isLiability => type == AccountTypes.creditCard;

  /// Credit cards: the limit, and the days the statement closes and the payment is due (0 = not set).
  int get creditLimitCents => money('CreditLimit');
  set creditLimitCents(int v) => setMoney('CreditLimit', v);
  int get statementDay => whole('StatementDay');
  set statementDay(int v) => j['StatementDay'] = v;
  int get paymentDueDay => whole('PaymentDueDay');
  set paymentDueDay(int v) => j['PaymentDueDay'] = v;
}

// ----- Debts -----

class DebtTypes {
  static const creditCard = 'Credit card';
  static const all = [creditCard, 'Student loan', 'Car loan', 'Mortgage', 'Personal loan', 'Medical', 'Other'];
}

class Debt extends Rec {
  Debt(super.j);
  factory Debt.create({required String name, String type = DebtTypes.creditCard, int balanceCents = 0, double rate = 0, int minimumCents = 0}) =>
      Debt(Rec.fresh({
        'Name': name,
        'Type': type,
        'Balance': balanceCents / 100,
        'OriginalBalance': balanceCents / 100,
        'InterestRate': rate,
        'MinimumPayment': minimumCents / 100,
        'Note': '',
        'Payments': <dynamic>[],
      }));
  Debt copy() => Debt(Rec.copyOf(j));
  @override
  String get collection => 'debts';

  String get name => str('Name');
  set name(String v) => j['Name'] = v;
  String get type => str('Type').isEmpty ? DebtTypes.creditCard : str('Type');
  set type(String v) => j['Type'] = v;
  int get balanceCents => money('Balance');
  set balanceCents(int v) => setMoney('Balance', v);
  int get originalCents => money('OriginalBalance');
  set originalCents(int v) => setMoney('OriginalBalance', v);
  double get rate => number('InterestRate');
  set rate(double v) => j['InterestRate'] = v;
  int get minimumCents => money('MinimumPayment');
  set minimumCents(int v) => setMoney('MinimumPayment', v);
  String get note => str('Note');
  set note(String v) => j['Note'] = v;
  List<MoneyEntry> get payments => entries('Payments');
  set payments(List<MoneyEntry> v) => setEntries('Payments', v);

  int get paidOffCents => originalCents - balanceCents > 0 ? originalCents - balanceCents : 0;
  bool get isPaidOff => balanceCents <= 0;
}

// ----- Bills -----

class BillSchedule {
  static const weekly = 'Weekly';
  static const everyTwoWeeks = 'Every 2 weeks';
  static const monthly = 'Monthly';
  static const quarterly = 'Every 3 months';
  static const yearly = 'Yearly';
  static const once = 'One time';
  static const all = [monthly, weekly, everyTwoWeeks, quarterly, yearly, once];
}

class Bill extends Rec {
  Bill(super.j);
  factory Bill.create({
    required String name,
    int amountCents = 0,
    DateTime? nextDue,
    String repeats = BillSchedule.monthly,
    String category = 'Utilities',
    String account = '',
    bool autoPay = false,
    String debtId = '',
  }) {
    final due = nextDue ?? DateTime.now();
    return Bill(Rec.fresh({
      'Name': name,
      'Amount': amountCents / 100,
      'NextDue': isoDay(due),
      'DueDay': due.day,
      'Repeats': repeats,
      'Category': category,
      'Account': account,
      'AutoPay': autoPay,
      'LastPaid': null,
      'Note': '',
      'DebtId': debtId,
    }));
  }
  Bill copy() => Bill(Rec.copyOf(j));
  @override
  String get collection => 'bills';

  String get name => str('Name');
  set name(String v) => j['Name'] = v;
  int get amountCents => money('Amount');
  set amountCents(int v) => setMoney('Amount', v);
  DateTime get nextDue => day('NextDue');
  set nextDue(DateTime v) => setDay('NextDue', v);
  int get dueDay => whole('DueDay');
  set dueDay(int v) => j['DueDay'] = v;
  String get repeats => str('Repeats').isEmpty ? BillSchedule.monthly : str('Repeats');
  set repeats(String v) => j['Repeats'] = v;
  String get category => str('Category');
  set category(String v) => j['Category'] = v;
  String get account => str('Account');
  set account(String v) => j['Account'] = v;
  bool get autoPay => flag('AutoPay');
  set autoPay(bool v) => j['AutoPay'] = v;
  DateTime? get lastPaid => dayOrNull('LastPaid');
  set lastPaid(DateTime? v) => setDay('LastPaid', v);
  String get note => str('Note');
  set note(String v) => j['Note'] = v;
  String get debtId => str('DebtId');
  set debtId(String v) => j['DebtId'] = v;

  bool get paysDebt => debtId.isNotEmpty;
  bool get isDone => repeats == BillSchedule.once && lastPaid != null;
}

// ----- Goals -----

class Goal extends Rec {
  Goal(super.j);
  factory Goal.create({required String name, int targetCents = 0, DateTime? targetDate, String linkedAccount = ''}) =>
      Goal(Rec.fresh({
        'Name': name,
        'Target': targetCents / 100,
        'Saved': 0,
        'TargetDate': targetDate == null ? null : isoDay(targetDate),
        'Note': '',
        'LinkedAccount': linkedAccount,
        'History': <dynamic>[],
      }));
  Goal copy() => Goal(Rec.copyOf(j));
  @override
  String get collection => 'goals';

  String get name => str('Name');
  set name(String v) => j['Name'] = v;
  int get targetCents => money('Target');
  set targetCents(int v) => setMoney('Target', v);
  int get savedCents => money('Saved');
  set savedCents(int v) => setMoney('Saved', v);
  DateTime? get targetDate => dayOrNull('TargetDate');
  set targetDate(DateTime? v) => setDay('TargetDate', v);
  String get note => str('Note');
  set note(String v) => j['Note'] = v;
  String get linkedAccount => str('LinkedAccount');
  set linkedAccount(String v) => j['LinkedAccount'] = v;
  List<MoneyEntry> get history => entries('History');
  set history(List<MoneyEntry> v) => setEntries('History', v);

  bool get isLinked => linkedAccount.isNotEmpty;

  /// A sinking fund: money set aside every month for a cost that comes around again (car registration, holidays).
  bool get isFund => str('Kind') == 'Fund';
  set isFund(bool v) => j['Kind'] = v ? 'Fund' : 'Goal';

  /// How often a sinking fund's cost comes around: 'Yearly', 'Every 6 months' or 'Every 3 months'.
  String get repeats => str('Repeats').isEmpty ? FundSchedule.yearly : str('Repeats');
  set repeats(String v) => j['Repeats'] = v;
}

class FundSchedule {
  static const yearly = 'Yearly';
  static const twiceAYear = 'Every 6 months';
  static const quarterly = 'Every 3 months';
  static const all = [yearly, twiceAYear, quarterly];
  static int months(String repeats) => switch (repeats) { twiceAYear => 6, quarterly => 3, _ => 12 };
}

// ----- Categories and rules -----

class CategoryDef extends Rec {
  CategoryDef(super.j);
  factory CategoryDef.create({required String name, required bool income, String parent = '', int order = 0}) =>
      CategoryDef(Rec.fresh({'Name': name, 'Kind': income ? 'Income' : 'Spending', 'Parent': parent, 'IsHidden': false, 'Order': order}));
  CategoryDef copy() => CategoryDef(Rec.copyOf(j));
  @override
  String get collection => 'categories';

  String get name => str('Name');
  set name(String v) => j['Name'] = v;
  bool get isIncome => str('Kind') == 'Income';
  String get parent => str('Parent');
  set parent(String v) => j['Parent'] = v;
  bool get isHidden => flag('IsHidden');
  set isHidden(bool v) => j['IsHidden'] = v;
  int get order => whole('Order');
}

/// "When the payee contains X, use category Y."
class Rule extends Rec {
  Rule(super.j);
  factory Rule.create({required String contains, required String category}) =>
      Rule(Rec.fresh({'Contains': contains, 'Category': category}));
  Rule copy() => Rule(Rec.copyOf(j));
  @override
  String get collection => 'rules';

  String get contains => str('Contains');
  set contains(String v) => j['Contains'] = v;
  String get category => str('Category');
  set category(String v) => j['Category'] = v;
}

// ----- Income -----

class IncomeSchedule {
  static const weekly = 'Weekly';
  static const everyTwoWeeks = 'Every 2 weeks';
  static const twiceAMonth = 'Twice a month';
  static const monthly = 'Monthly';
  static const once = 'One time';
  static const all = [everyTwoWeeks, twiceAMonth, weekly, monthly, once];
}

class IncomeSource extends Rec {
  IncomeSource(super.j);
  factory IncomeSource.create({
    required String name,
    int amountCents = 0,
    bool varies = false,
    String repeats = IncomeSchedule.everyTwoWeeks,
    DateTime? nextDate,
    String category = 'Paycheck',
    String account = '',
  }) {
    final next = nextDate ?? DateTime.now();
    return IncomeSource(Rec.fresh({
      'Name': name,
      'Amount': amountCents / 100,
      'Varies': varies,
      'AutoAdd': !varies,
      'Repeats': repeats,
      'NextDate': isoDay(next),
      'Day1': next.day,
      'Day2': 15,
      'Category': category,
      'Account': account,
      'LastReceived': null,
      'Note': '',
    }));
  }
  IncomeSource copy() => IncomeSource(Rec.copyOf(j));
  @override
  String get collection => 'income';

  String get name => str('Name');
  set name(String v) => j['Name'] = v;
  int get amountCents => money('Amount');
  set amountCents(int v) => setMoney('Amount', v);
  bool get varies => flag('Varies');
  set varies(bool v) => j['Varies'] = v;
  bool get autoAdd => j['AutoAdd'] != false;
  set autoAdd(bool v) => j['AutoAdd'] = v;
  String get repeats => str('Repeats').isEmpty ? IncomeSchedule.everyTwoWeeks : str('Repeats');
  set repeats(String v) => j['Repeats'] = v;
  DateTime get nextDate => day('NextDate');
  set nextDate(DateTime v) => setDay('NextDate', v);
  int get day1 => whole('Day1');
  set day1(int v) => j['Day1'] = v;
  int get day2 => whole('Day2', 15);
  set day2(int v) => j['Day2'] = v;
  String get category => str('Category').isEmpty ? 'Paycheck' : str('Category');
  set category(String v) => j['Category'] = v;
  String get account => str('Account');
  set account(String v) => j['Account'] = v;
  DateTime? get lastReceived => dayOrNull('LastReceived');
  set lastReceived(DateTime? v) => setDay('LastReceived', v);
  String get note => str('Note');
  set note(String v) => j['Note'] = v;

  bool get isDone => repeats == IncomeSchedule.once && lastReceived != null;

  /// Paycheck breakdown: pay before deductions, and each deduction (taxes, 401k, insurance…).
  int get grossCents => money('Gross');
  set grossCents(int v) => setMoney('Gross', v);
  List<(String, int)> get deductions => [
        for (final d in (j['Deductions'] as List? ?? const []))
          if (d is Map) ('${d['Name'] ?? ''}', toCents(d['Amount'])),
      ];
  set deductions(List<(String, int)> v) => j['Deductions'] = [for (final (n, c) in v) {'Name': n, 'Amount': c / 100}];
  bool get hasBreakdown => grossCents > 0;

  /// Payday has come and WillowWallet needs an answer (an amount, or a confirmation).
  bool get needsAsking => !isDone && (varies || !autoAdd);
}

// ----- Tax documents -----

class TaxKinds {
  static const other = 'Other';
  static const all = ['W-2', '1099', '1098', '1095', 'Tax return', 'Receipt', 'Charitable donation', 'Property tax', 'Estimated payment', 'Letter from IRS/state', other];
}

class TaxDoc extends Rec {
  TaxDoc(super.j);
  factory TaxDoc.create({required int year, required String title, String kind = TaxKinds.other, required String fileName, String originalName = '', int size = 0}) =>
      TaxDoc(Rec.fresh({'Year': year, 'Title': title, 'Kind': kind, 'FileName': fileName, 'OriginalName': originalName, 'Size': size, 'Note': ''}));
  TaxDoc copy() => TaxDoc(Rec.copyOf(j));
  @override
  String get collection => 'tax-documents';

  int get year => whole('Year', DateTime.now().year - 1);
  set year(int v) => j['Year'] = v;
  String get title => str('Title');
  set title(String v) => j['Title'] = v;
  String get kind => str('Kind').isEmpty ? TaxKinds.other : str('Kind');
  set kind(String v) => j['Kind'] = v;
  String get fileName => str('FileName');
  String get originalName => str('OriginalName');
  int get size => whole('Size');
  String get note => str('Note');
  set note(String v) => j['Note'] = v;
}

// ----- Owed to you: IOUs -----

/// Money lent to or borrowed from someone. Positive: they owe you; negative: you owe them.
class Iou extends Rec {
  Iou(super.j);
  factory Iou.create({required String person, required int cents, DateTime? date, String note = ''}) =>
      Iou(Rec.fresh({'Person': person, 'Amount': cents / 100, 'Date': isoDay(date ?? DateTime.now()), 'Note': note, 'Settled': false, 'Payments': <dynamic>[]}));
  Iou copy() => Iou(Rec.copyOf(j));
  @override
  String get collection => 'ious';

  String get person => str('Person');
  set person(String v) => j['Person'] = v;
  int get cents => money('Amount');
  set cents(int v) => setMoney('Amount', v);
  DateTime get date => day('Date');
  set date(DateTime v) => setDay('Date', v);
  String get note => str('Note');
  set note(String v) => j['Note'] = v;
  bool get settled => flag('Settled');
  set settled(bool v) => j['Settled'] = v;

  /// Paid back so far (always positive).
  List<MoneyEntry> get payments => entries('Payments');
  set payments(List<MoneyEntry> v) => setEntries('Payments', v);
  int get paidBack => payments.fold(0, (s, p) => s + p.cents);

  /// What's still owed (positive: to you; negative: by you).
  int get left => settled ? 0 : (cents.abs() - paidBack).clamp(0, 1 << 40) * cents.sign;
  bool get theyOwe => cents > 0;
}

// ----- Taxes: mileage -----

class MileageKinds {
  static const all = ['Business', 'Medical', 'Charity', 'Moving'];

  /// Cents per mile (the IRS standard rates; change them in Taxes → Mileage rates).
  static const defaultRates = {'Business': 70.0, 'Medical': 21.0, 'Charity': 14.0, 'Moving': 21.0};
}

/// A trip that counts for taxes: business, medical, charity or moving.
class MileageTrip extends Rec {
  MileageTrip(super.j);
  factory MileageTrip.create({required DateTime date, required double miles, String purpose = '', String kind = 'Business'}) =>
      MileageTrip(Rec.fresh({'Date': isoDay(date), 'Miles': miles, 'Purpose': purpose, 'Kind': kind}));
  MileageTrip copy() => MileageTrip(Rec.copyOf(j));
  @override
  String get collection => 'mileage';

  DateTime get date => day('Date');
  set date(DateTime v) => setDay('Date', v);
  double get miles => number('Miles');
  set miles(double v) => j['Miles'] = v;
  String get purpose => str('Purpose');
  set purpose(String v) => j['Purpose'] = v;
  String get kind => MileageKinds.all.contains(str('Kind')) ? str('Kind') : 'Business';
  set kind(String v) => j['Kind'] = v;
}

// ----- Documents vault -----

class DocumentKinds {
  static const other = 'Other';
  static const all = ['Insurance', 'Vehicle', 'Home', 'Warranty', 'Manual', 'Medical', 'Legal', 'Pet', other];
}

/// A document kept with your money: an insurance policy, a car title, a manual. With an end date, you get a reminder.
class VaultDoc extends Rec {
  VaultDoc(super.j);
  factory VaultDoc.create({required String title, String kind = DocumentKinds.other, required String fileName, String originalName = '', int size = 0, DateTime? expires}) =>
      VaultDoc(Rec.fresh({'Title': title, 'Kind': kind, 'FileName': fileName, 'OriginalName': originalName, 'Size': size, 'Expires': expires == null ? null : isoDay(expires), 'Note': ''}));
  VaultDoc copy() => VaultDoc(Rec.copyOf(j));
  @override
  String get collection => 'documents';

  String get title => str('Title');
  set title(String v) => j['Title'] = v;
  String get kind => str('Kind').isEmpty ? DocumentKinds.other : str('Kind');
  set kind(String v) => j['Kind'] = v;
  String get fileName => str('FileName');
  String get originalName => str('OriginalName');
  int get size => whole('Size');
  DateTime? get expires => dayOrNull('Expires');
  set expires(DateTime? v) => setDay('Expires', v);
  String get note => str('Note');
  set note(String v) => j['Note'] = v;
}

// ----- Wishlist -----

/// Something you want. WillowWallet asks again after a cooling-off period (30 days to start).
class WishItem extends Rec {
  WishItem(super.j);
  factory WishItem.create({required String name, int cents = 0, String link = '', int days = 30, DateTime? added}) {
    final a = added ?? DateTime.now();
    return WishItem(Rec.fresh({'Name': name, 'Price': cents / 100, 'Link': link, 'Added': isoDay(a), 'DecideOn': isoDay(DateTime(a.year, a.month, a.day + days)), 'Status': 'Waiting', 'Note': ''}));
  }
  WishItem copy() => WishItem(Rec.copyOf(j));
  @override
  String get collection => 'wishlist';

  String get name => str('Name');
  set name(String v) => j['Name'] = v;
  int get cents => money('Price');
  set cents(int v) => setMoney('Price', v);
  String get link => str('Link');
  set link(String v) => j['Link'] = v;
  DateTime get added => day('Added');
  DateTime get decideOn => day('DecideOn');
  set decideOn(DateTime v) => setDay('DecideOn', v);

  /// "Waiting", "Bought" or "Skipped".
  String get status => str('Status').isEmpty ? 'Waiting' : str('Status');
  set status(String v) => j['Status'] = v;
  bool get waiting => status == 'Waiting';
  String get note => str('Note');
  set note(String v) => j['Note'] = v;
}

// ----- Gift planner -----

class GiftItem {
  const GiftItem(this.name, this.cents, this.bought);
  final String name;
  final int cents;
  final bool bought;
  Map<String, dynamic> toJson() => {'Name': name, 'Amount': cents / 100, 'Bought': bought};
  static GiftItem fromJson(Object? j) {
    final m = j is Map ? j : const {};
    return GiftItem('${m['Name'] ?? ''}', toCents(m['Amount']), m['Bought'] == true);
  }
}

/// Gifts for one person for an occasion ("Holidays 2026"): a budget and the gift ideas or what you bought.
class GiftPlan extends Rec {
  GiftPlan(super.j);
  factory GiftPlan.create({required String occasion, required String person, int budgetCents = 0}) =>
      GiftPlan(Rec.fresh({'Occasion': occasion, 'Person': person, 'Budget': budgetCents / 100, 'Items': <dynamic>[]}));
  GiftPlan copy() => GiftPlan(Rec.copyOf(j));
  @override
  String get collection => 'gift-plans';

  String get occasion => str('Occasion');
  set occasion(String v) => j['Occasion'] = v;
  String get person => str('Person');
  set person(String v) => j['Person'] = v;
  int get budgetCents => money('Budget');
  set budgetCents(int v) => setMoney('Budget', v);
  List<GiftItem> get items => [for (final i in (j['Items'] as List? ?? const [])) GiftItem.fromJson(i)];
  set items(List<GiftItem> v) => j['Items'] = [for (final i in v) i.toJson()];
  int get boughtCents => items.where((i) => i.bought).fold(0, (s, i) => s + i.cents);
  int get plannedCents => items.fold(0, (s, i) => s + i.cents);
}

// ----- Synced settings -----

/// Budget preferences that travel with your data (the Windows app's preferences.json).
class Prefs extends Rec {
  Prefs(super.j);
  factory Prefs.defaults() => Prefs({
        'Id': 'preferences',
        'UpdatedAt': '0001-01-01T00:00:00',
        'Currency': 'en-US',
        'MonthStartDay': 1,
        'RemindersOn': true,
        'ReminderDays': 3,
        'DismissedSubscriptions': <dynamic>[],
        'SkippedCuts': <dynamic>[],
        'WelcomeDismissed': false,
        'SetupDone': false,
      });
  Prefs copy() => Prefs(Rec.copyOf(j));
  @override
  String get collection => 'settings';

  int get monthStartDay => whole('MonthStartDay', 1).clamp(1, 28);
  set monthStartDay(int v) => j['MonthStartDay'] = v.clamp(1, 28);
  bool get remindersOn => j['RemindersOn'] != false;
  set remindersOn(bool v) => j['RemindersOn'] = v;
  int get reminderDays => whole('ReminderDays', 3);
  set reminderDays(int v) => j['ReminderDays'] = v;
  List<String> get dismissedSubscriptions => strings('DismissedSubscriptions');
  set dismissedSubscriptions(List<String> v) => j['DismissedSubscriptions'] = v;
  List<String> get skippedCuts => strings('SkippedCuts');
  set skippedCuts(List<String> v) => j['SkippedCuts'] = v;
  bool get setupDone => flag('SetupDone');
  set setupDone(bool v) => j['SetupDone'] = v;

  /// Forecast: how far ahead ('Month', '30', '60' or '90' days), the balance to aim for, and what it counts.
  String get forecastHorizon => const ['30', '60', '90'].contains(str('ForecastHorizon')) ? str('ForecastHorizon') : 'Month';
  set forecastHorizon(String v) => j['ForecastHorizon'] = v;
  int get forecastTargetCents => money('ForecastTarget');
  set forecastTargetCents(int v) => setMoney('ForecastTarget', v);
  bool get forecastIncludeSavings => flag('ForecastIncludeSavings');
  set forecastIncludeSavings(bool v) => j['ForecastIncludeSavings'] = v;
  /// Shared household: the people (two or more turns it on) and the first person's share of shared spending (50 = half).
  List<String> get householdPeople => strings('HouseholdPeople');
  set householdPeople(List<String> v) => j['HouseholdPeople'] = v;
  int get householdShare => whole('HouseholdShare', 50).clamp(1, 99);
  set householdShare(int v) => j['HouseholdShare'] = v;
  bool get household => householdPeople.length >= 2;

  /// Spending alerts: on or off, the budget warning level (80 = at 80% used), and alerts dismissed on any device.
  bool get alertsOn => j['AlertsOn'] != false;
  set alertsOn(bool v) => j['AlertsOn'] = v;
  int get alertPercent => whole('AlertPercent', 80).clamp(50, 99);
  set alertPercent(int v) => j['AlertPercent'] = v;
  List<String> get dismissedAlerts => strings('DismissedAlerts');
  set dismissedAlerts(List<String> v) => j['DismissedAlerts'] = v;
  bool get forecastCountSavings => j['ForecastCountSavings'] != false;

  /// Cents per mile for each kind of trip (unset kinds use the standard rates).
  Map<String, double> get mileageRates => {
        for (final e in ((j['MileageRates'] as Map?) ?? const {}).entries)
          if (e.value is num) '${e.key}': (e.value as num).toDouble(),
      };
  set mileageRates(Map<String, double> v) => j['MileageRates'] = v;

  /// No-spend challenge: how many no-spend days to aim for each month (0 = off).
  int get noSpendGoal => whole('NoSpendGoal');
  set noSpendGoal(int v) => j['NoSpendGoal'] = v;

  /// Searches pinned on Transactions.
  List<String> get savedSearches => strings('SavedSearches');
  set savedSearches(List<String> v) => j['SavedSearches'] = v;
  set forecastCountSavings(bool v) => j['ForecastCountSavings'] = v;
}

/// Snowball or avalanche, and the extra monthly payment (the Windows app's debt-plan.json).
class DebtPlanSettings extends Rec {
  DebtPlanSettings(super.j);
  factory DebtPlanSettings.defaults() =>
      DebtPlanSettings({'Id': 'debt-plan', 'UpdatedAt': '0001-01-01T00:00:00', 'Strategy': 'Avalanche', 'ExtraPayment': 0});
  DebtPlanSettings copy() => DebtPlanSettings(Rec.copyOf(j));
  @override
  String get collection => 'settings';

  String get strategy => str('Strategy') == 'Snowball' ? 'Snowball' : 'Avalanche';
  set strategy(String v) => j['Strategy'] = v;
  int get extraCents => money('ExtraPayment');
  set extraCents(int v) => setMoney('ExtraPayment', v);
}
