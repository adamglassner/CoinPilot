import 'dart:convert';
import 'dart:io';

import 'package:archive/archive.dart';
import 'package:coinpilot/backup.dart';
import 'package:coinpilot/dates.dart';
import 'package:coinpilot/finance.dart';
import 'package:coinpilot/form.dart';
import 'package:coinpilot/reminders.dart';
import 'package:coinpilot/screens/whats_new.dart';
import 'package:coinpilot/importer.dart';
import 'package:coinpilot/models.dart';
import 'package:coinpilot/money.dart';
import 'package:coinpilot/records.dart';
import 'package:coinpilot/store.dart';
import 'package:coinpilot/updater.dart';
import 'package:coinpilot/version.dart';
import 'package:cryptography/cryptography.dart';
import 'package:flutter_test/flutter_test.dart';

/// The money rules shared with WillowWallet for Windows: bills, debts, goals, income, subscriptions, budgets,
/// reports, importing, backups and updates.
void main() {
  setUp(() => Money.currency = currencies.first);
  final today = dayOnly(DateTime.now());

  test('one version number: pubspec.yaml, the app, WillowWallet for Windows and the server', () {
    final pub = File('pubspec.yaml').readAsStringSync();
    expect(RegExp(r'^version: ([0-9.]+)', multiLine: true).firstMatch(pub)!.group(1), appVersion);
    String csproj(String path) => RegExp(r'<Version>([0-9.]+)</Version>').firstMatch(File(path).readAsStringSync())!.group(1)!;
    expect(csproj('../src/CoinPilot.Desktop/CoinPilot.Desktop.csproj'), appVersion);
    expect(csproj('../server/WillowWallet.Server/WillowWallet.Server.csproj'), appVersion);
  });

  test('What\'s new: each app\'s changelog starts with this version', () {
    for (final file in ['CHANGELOG.md', 'CHANGELOG-desktop.md']) {
      final releases = parseChangelog(File(file).readAsStringSync());
      expect(releases.first.title, startsWith(appVersion), reason: file);
      expect(releases.first.lines, isNotEmpty, reason: file);
    }
  });

  test('bill reminders: due soon, not autopay', () {
    final s = Store.memory();
    s.put(Bill.create(name: 'Phone', amountCents: 6500, nextDue: today.add(const Duration(days: 2))));
    s.put(Bill.create(name: 'Netflix', amountCents: 1549, nextDue: today.add(const Duration(days: 1)), autoPay: true));
    s.put(Bill.create(name: 'Rent', amountCents: 145000, nextDue: today.add(const Duration(days: 20))));
    s.updatePrefs((p) => p.reminderDays = 3);
    final due = Reminders.due(s, today);
    expect(due.map((b) => b.name), ['Phone']);
    expect(Reminders.titleFor(due, today), 'Phone is due in 2 days');
    s.updatePrefs((p) => p.remindersOn = false);
    expect(Reminders.due(s, today), isEmpty);
  });

  group('Bills', () {
    test('monthly bills keep their day', () {
      expect(Bills.next(DateTime(2027, 1, 31), BillSchedule.monthly, 31), DateTime(2027, 2, 28));
      expect(Bills.next(DateTime(2027, 2, 28), BillSchedule.monthly, 31), DateTime(2027, 3, 31));
      expect(Bills.next(DateTime(2026, 11, 16), BillSchedule.everyTwoWeeks, 16), DateTime(2026, 11, 30));
      expect(Bills.next(DateTime(2026, 11, 16), BillSchedule.yearly, 16), DateTime(2027, 11, 16));
      expect(Bills.monthlyCost(1000, BillSchedule.weekly).round(), 4333);
    });

    test('paying records a transaction and moves to the next due date', () {
      final s = Store.memory();
      final b = Bill.create(name: 'Phone', amountCents: 6500, nextDue: today, account: 'Checking', category: 'Utilities');
      s.put(b);
      expect(Bills.status(s.bills.single, today), BillStatus.dueToday);
      final t = s.markPaid(s.bills.single, today);
      expect(t!.cents, -6500);
      expect(t.category, 'Utilities');
      expect(s.bills.single.nextDue, Bills.next(today, BillSchedule.monthly, today.day));
      expect(s.bills.single.lastPaid, today);
      s.skipBill(s.bills.single);
      expect(s.bills.single.nextDue, Bills.next(Bills.next(today, BillSchedule.monthly, today.day), BillSchedule.monthly, today.day));
    });

    test('a bill that pays a debt lowers it and ends when it\'s paid off', () {
      final s = Store.memory();
      final loan = Debt.create(name: 'Student loan', type: 'Student loan', balanceCents: 55000, rate: 5, minimumCents: 30000);
      s.put(loan);
      final bill = s.createDebtBill(loan, today, 'Checking');
      s.markPaid(s.bills.single, today);
      expect(s.debtById(loan.id)!.balanceCents, 25000);
      expect(s.bills.single.isDone, isFalse);
      s.markPaid(s.bills.single, today);
      expect(s.debtById(loan.id)!.isPaidOff, isTrue);
      expect(s.bills.single.isDone, isTrue);
      expect(s.transactions.where((t) => t.category == 'Debt payments').map((t) => t.cents), [-30000, -25000]);
      expect(bill.debtId, loan.id);
      s.deleteDebt(s.debtById(loan.id)!);
      expect(s.bills.single.paysDebt, isFalse);
    });
  });

  group('Debts', () {
    test('avalanche pays the highest rate first, snowball the smallest balance', () {
      final card = Debt.create(name: 'Card', balanceCents: 300000, rate: 24, minimumCents: 9000);
      final car = Debt.create(name: 'Car', balanceCents: 120000, rate: 6, minimumCents: 15000);
      final a = DebtPlan.make([card, car], DebtPlan.avalanche, 10000);
      final b = DebtPlan.make([card, car], DebtPlan.snowball, 10000);
      expect(a.order[card.id], 1);
      expect(b.order[car.id], 1);
      expect(a.neverPaysOff, isFalse);
      expect(a.totalInterest, lessThanOrEqualTo(b.totalInterest));
      expect(a.monthlyPayment, 34000);
      final growing = DebtPlan.make([Debt.create(name: 'X', balanceCents: 1000000, rate: 30, minimumCents: 1000)], DebtPlan.avalanche, 0);
      expect(growing.neverPaysOff, isTrue);
    });

    test('a payment on the debt counts as its bill when the bill is due soon', () {
      final s = Store.memory();
      final d = Debt.create(name: 'Visa', balanceCents: 100000, rate: 22, minimumCents: 5000);
      s.put(d);
      s.createDebtBill(d, today.add(const Duration(days: 3)), 'Checking');
      s.recordDebtPayment(s.debtById(d.id)!, 5000, today, 'Checking');
      expect(s.debtById(d.id)!.balanceCents, 95000);
      expect(s.bills.single.nextDue.isAfter(today.add(const Duration(days: 20))), isTrue);
    });
  });

  group('Accounts and goals', () {
    test('balances, transfers and net worth', () {
      final s = Store.memory();
      s.put(Account.create(name: 'Checking', startingCents: 100000, balanceDate: today.subtract(const Duration(days: 10))));
      s.put(Account.create(name: 'Savings', type: AccountTypes.savings, startingCents: 50000, balanceDate: today.subtract(const Duration(days: 10))));
      s.put(Debt.create(name: 'Loan', balanceCents: 20000));
      s.saveTxn(Txn(payee: 'Kroger', category: 'Groceries', account: 'Checking', date: today, cents: -2500));
      s.saveTxn(Txn(payee: 'Transfer to Savings', category: 'Transfer', account: 'Checking', date: today, cents: -10000, extra: {'TransferAccount': 'Savings'}));
      final checking = s.accountRecords.firstWhere((a) => a.name == 'Checking');
      final savings = s.accountRecords.firstWhere((a) => a.name == 'Savings');
      expect(s.balance(checking), 87500);
      expect(s.balance(savings), 60000);
      expect(s.balanceAt(checking, today.subtract(const Duration(days: 1))), 100000);
      expect(s.netWorth, 87500 + 60000 - 20000);
      expect(s.spentIn(s.budgetMonth(today)), 2500); // the transfer isn't spending
      s.renameAccount('Savings', 'Ally Savings');
      expect(s.transactions.any((t) => t.transferAccount == 'Ally Savings'), isTrue);
    });

    test('goals: linked to an account, milestones and monthly amount', () {
      final s = Store.memory();
      s.put(Account.create(name: 'HYSA', type: AccountTypes.savings, startingCents: 300000));
      s.put(Goal.create(name: 'Emergency', targetCents: 600000, linkedAccount: 'HYSA'));
      s.put(Goal.create(name: 'Trip', targetCents: 100000, targetDate: DateTime(today.year, today.month + 10, 1)));
      final e = s.goals.firstWhere((g) => g.name == 'Emergency');
      final trip = s.goals.firstWhere((g) => g.name == 'Trip');
      expect(s.savedFor(e), 300000);
      expect(s.fraction(e), 0.5);
      expect(s.addToGoal(trip, 30000, today), 25);
      expect(s.addToGoal(s.goals.firstWhere((g) => g.name == 'Trip'), 20000, today), 50);
      expect(s.monthlyNeeded(s.goals.firstWhere((g) => g.name == 'Trip'), today), 5000);
    });
  });

  group('Income', () {
    test('twice a month and every two weeks', () {
      expect(Paydays.next(DateTime(2026, 11, 1), IncomeSchedule.twiceAMonth, 1, 15), DateTime(2026, 11, 15));
      expect(Paydays.next(DateTime(2026, 11, 15), IncomeSchedule.twiceAMonth, 1, 15), DateTime(2026, 12, 1));
      final s = Store.memory();
      s.put(IncomeSource.create(name: 'Acme', amountCents: 185000, repeats: IncomeSchedule.everyTwoWeeks, nextDate: today.subtract(const Duration(days: 28)), account: 'Checking'));
      s.put(IncomeSource.create(name: 'DoorDash', varies: true, repeats: IncomeSchedule.weekly, nextDate: today.subtract(const Duration(days: 1)), category: 'Side income'));
      final added = s.addDuePaydays(today);
      expect(added.length, 3);
      expect(s.addDuePaydays(today), isEmpty);
      expect(s.askingPaydays(today).single.name, 'DoorDash');
      s.receiveIncome(s.askingPaydays(today).single, 13275, today.subtract(const Duration(days: 1)));
      expect(s.askingPaydays(today), isEmpty);
      expect(s.typicalIncome(s.incomeSources.firstWhere((x) => x.name == 'DoorDash')), 13275);
      expect(s.expectedMonthlyIncome(), (185000 * 26 / 12).round() + (13275 * 52 / 12).round());
    });
  });

  group('Budgets', () {
    test('rollover, one-month amounts, copy last month and budget months', () {
      final s = Store.memory();
      final month = s.budgetMonth(today);
      final last = DateTime(month.year, month.month - 1, month.day);
      s.saveBudget(Budget(category: 'Dining out', limitCents: 20000).copyWith(rollover: true));
      s.saveBudget(s.budgetFor('Dining out')!.copyWith(rollover: true));
      final b = Budget.fromJson({...s.budgetFor('Dining out')!.toJson(), 'StartMonth': isoDay(last)});
      s.saveBudget(b);
      s.saveTxn(Txn(payee: 'Chipotle', category: 'Dining out', account: 'Card', date: last, cents: -5000));
      expect(s.carryFor(s.budgetFor('Dining out')!, month), 15000);
      expect(s.availableFor(s.budgetFor('Dining out')!, month), 35000);
      s.setMonthAmount(s.budgetFor('Dining out')!, month, 30000);
      expect(s.limitFor(s.budgetFor('Dining out')!, month), 30000);
      expect(s.limitFor(s.budgetFor('Dining out')!, last), 20000);
      expect(s.copyLastMonth(month), 1);
      expect(s.limitFor(s.budgetFor('Dining out')!, month), 20000);
      s.updatePrefs((p) => p.monthStartDay = 15);
      expect(s.budgetMonth(DateTime(2026, 11, 14)), DateTime(2026, 10, 15));
      expect(s.budgetMonth(DateTime(2026, 11, 15)), DateTime(2026, 11, 15));
      expect(s.monthTitle(DateTime(2026, 11, 20)), 'Nov 15 – Dec 14');
    });

    test('templates', () {
      final plan = BudgetTemplates.plan(BudgetTemplates.fiftyThirtyTwenty, 400000, {}, [...BudgetTemplates.needs, ...BudgetTemplates.wants, 'Savings']);
      expect(plan['Savings'], 80000);
      final total = plan.values.fold<int>(0, (s, v) => s + v);
      expect((total - 400000).abs(), lessThanOrEqualTo(6000));
      final zero = BudgetTemplates.plan(BudgetTemplates.zeroBased, 100000, {'Groceries': 41230}, ['Groceries', 'Savings']);
      expect(zero['Groceries'], 41500);
      expect(zero['Savings'], 58500);
    });
  });

  group('Subscriptions', () {
    Store withSubs() {
      final s = Store.memory();
      s.put(Bill.create(name: 'Netflix', amountCents: 1549, nextDue: today.add(const Duration(days: 5)), category: 'Subscriptions'));
      s.put(Bill.create(name: 'Hulu', amountCents: 1799, nextDue: today.add(const Duration(days: 9)), category: 'Entertainment'));
      for (final m in [4, 3, 2, 1, 0]) {
        s.saveTxn(Txn(payee: 'DISNEY PLUS 8842', category: 'Subscriptions', account: 'Visa', date: today.subtract(Duration(days: 2 + 30 * m)), cents: m >= 3 ? -1099 : -1399));
      }
      s.put(Bill.create(name: 'Amazon Prime', amountCents: 13900, repeats: BillSchedule.yearly, nextDue: today.add(const Duration(days: 20)), category: 'Subscriptions'));
      s.put(Bill.create(name: 'Dropbox', amountCents: 1199, nextDue: today.add(const Duration(days: 12)), category: 'Subscriptions'));
      for (final m in [5, 3, 1]) {
        s.saveTxn(Txn(payee: 'Dropbox', category: 'Subscriptions', account: 'Visa', date: today.subtract(Duration(days: 30 * m)), cents: m == 5 ? -999 : -1199));
      }
      s.put(Bill.create(name: 'Car insurance', amountCents: 12000, nextDue: today.add(const Duration(days: 3)), category: 'Transportation'));
      return s;
    }

    test('found in transactions even after a price change', () {
      final s = withSubs();
      final g = s.findSubscriptions(today).firstWhere((g) => g.payee.startsWith('DISNEY'));
      expect(g.cents, 1399);
      expect(g.repeats, BillSchedule.monthly);
      expect(Subscriptions.pretty(g.payee), 'Disney Plus');
      expect(Subscriptions.normalize('NETFLIX.COM #1234'), 'netflix.com');
    });

    test('suggestions match the PC', () {
      final s = withSubs();
      final month = s.budgetMonth(today);
      final subs = s.allSubscriptions(today);
      expect(subs.map((x) => x.name), unorderedEquals(['Netflix', 'Hulu', 'Amazon Prime', 'Dropbox', 'Disney Plus']));
      final cuts = s.suggestCuts(month, today);
      int? priority(String key) => cuts.where((c) => c.key.startsWith(key)).map((c) => c.priority).firstOrNull;
      expect(priority('netflix'), 40);
      expect(priority('disney'), 40);
      expect(priority('dropbox'), 35);
      expect(priority('amazon prime'), 30);
      expect(priority('hulu'), 10);
      expect(cuts.first.priority, 40);
      expect(cuts.firstWhere((c) => c.key.startsWith('disney')).reason, contains('price also went up'));
      expect(cuts.firstWhere((c) => c.key == 'amazon prime').yearlySaving, 13900);
    });

    test('skip for a month, for good, and I canceled it', () {
      final s = withSubs();
      final month = s.budgetMonth(today);
      final next = DateTime(month.year, month.month + 1, month.day);
      s.skipCut('netflix', month);
      expect(s.suggestCuts(month, today).any((c) => c.key == 'netflix'), isFalse);
      expect(s.suggestCuts(next, today).any((c) => c.key == 'netflix'), isTrue);
      s.skipCut('amazon prime', null);
      expect(s.suggestCuts(next, today).any((c) => c.key == 'amazon prime'), isFalse);
      s.markCanceled(s.allSubscriptions(today).firstWhere((x) => x.name == 'Hulu'));
      expect(s.bills.any((b) => b.name == 'Hulu'), isFalse);
      s.markCanceled(s.allSubscriptions(today).firstWhere((x) => x.key.startsWith('disney')));
      expect(s.findSubscriptions(today).any((g) => g.payee.startsWith('DISNEY')), isFalse);
      expect(s.suggestCuts(next, today).any((c) => c.priority == 40), isFalse);
      s.resetCutSkips();
      expect(s.suggestCuts(month, today).any((c) => c.key == 'amazon prime'), isTrue);
      s.eraseAll();
      expect(s.prefs.skippedCuts, isEmpty);
    });
  });

  group('Reports', () {
    test('year in review and categories', () {
      final s = Store.memory()..loadSample(now: today);
      final r = s.yearReview(today.year);
      expect(r.spent, s.transactions.where((t) => t.date.year == today.year && t.isSpending).fold<int>(0, (x, t) => x - t.cents));
      expect(r.topCategories, isNotEmpty);
      expect(r.months.length, 12);
      final totals = s.monthlyTotals(today, 6);
      expect(totals.length, 6);
      expect(totals.last.spent, s.spentIn(s.budgetMonth(today)));
      expect(s.netWorthHistory(6).last.$2, s.netWorth);
    });
  });

  group('Importing', () {
    test('bank CSV with debit and credit columns', () {
      const csv = 'Account: 1234\nDate,Description,Debit,Credit\n11/02/2026,KROGER #445,52.10,\n11/05/2026,ACME PAYROLL,,1850.00\n';
      final st = Importer.read(csv, 'chase.csv');
      final map = Importer.guessMapping(st.headers);
      final (rows, skipped) = Importer.apply(st, map);
      expect(skipped, 0);
      expect(rows.map((r) => r.cents), [-5210, 185000]);
      expect(rows.first.date, DateTime(2026, 11, 2));
    });

    test('Venmo, amounts and dates', () {
      const csv = 'Datetime,Type,Status,Note,From,To,Amount (total)\n2026-11-07T10:00:00,Payment,Complete,Tacos,Adam,Chris,- \$12.40\n2026-11-08T10:00:00,Payment,Complete,Rent share,Morgan,Adam,+ \$40.00\n2026-11-09T10:00:00,Payment,Pending,x,Adam,Sam,- \$5.00\n';
      final st = Importer.read(csv, 'venmo.csv');
      final map = Importer.guessMapping(st.headers);
      expect(map.source, 'Venmo');
      final (rows, skipped) = Importer.apply(st, map);
      expect(skipped, 1);
      expect(rows.map((r) => (r.payee, r.cents)), [('Chris', -1240), ('Morgan', 4000)]);
      expect(Importer.parseAmount('(12.34)'), -1234);
      expect(Importer.parseAmount('1.234,56'), 123456);
      expect(Importer.parseAmount('12.34 DR'), -1234);
      expect(Importer.parseDate('Nov 16, 2026'), DateTime(2026, 11, 16));
      expect(Importer.parseDate('16.11.2026'), DateTime(2026, 11, 16));
      expect(Importer.parseDate('25/12/2026'), DateTime(2026, 12, 25));
    });

    test('OFX, duplicates and categories from rules', () {
      const ofx = '<OFX><BANKTRANLIST><STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20261103<TRNAMT>-12.99<FITID>A1<NAME>NETFLIX.COM<MEMO>Streaming</STMTTRN>'
          '<STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20261104<TRNAMT>-5.25<FITID>A2<NAME>STARBUCKS 1182</STMTTRN></BANKTRANLIST>'
          '<LEDGERBAL><BALAMT>1234.56<DTASOF>20261105</LEDGERBAL></OFX>';
      final st = Importer.read(ofx, 'bank.ofx');
      expect(st.rows.length, 2);
      expect(st.ledgerCents, 123456);
      final s = Store.memory();
      s.put(Rule.create(contains: 'starbucks', category: 'Dining out'));
      expect(s.categoryForRow(st.rows[1]), 'Dining out');
      expect(s.commitImport(st.rows, 'Checking'), 2);
      s.markDuplicates(st.rows, 'Checking');
      expect(st.rows.every((r) => r.duplicate), isTrue);
    });
  });

  group('Backups (the same .zip as WillowWallet for Windows)', () {
    test('a backup made here restores here', () async {
      final a = Store.memory()..loadSample(now: today);
      a.updatePrefs((p) => p.monthStartDay = 5);
      final zip = await Backup.create(a);
      final names = [for (final f in ZipDecoder().decodeBytes(zip).files) f.name];
      expect(names, containsAll(['transactions.json', 'bills.json', 'preferences.json', 'debt-plan.json']));
      final prefs = jsonDecode(utf8.decode(ZipDecoder().decodeBytes(zip).files.firstWhere((f) => f.name == 'preferences.json').content as List<int>));
      expect(prefs['MonthStartDay'], 5);
      expect(prefs.containsKey('Id'), isFalse);
      final b = Store.memory();
      await Backup.restore(b, await Backup.read(zip));
      expect(b.transactions.length, a.transactions.length);
      expect(b.bills.length, a.bills.length);
      expect(b.debts.length, a.debts.length);
      expect(b.monthStartDay, 5);
      expect(b.netWorth, a.netWorth);
    });

    test('a backup from the PC (protected with a password) opens here', () async {
      final aes = AesGcm.with256bits(nonceLength: 12);
      final dataKey = List<int>.generate(32, (i) => i + 1);
      final salt = List<int>.generate(16, (i) => 200 - i);
      const iterations = 1000;
      Future<List<int>> enc(List<int> plain, List<int> key) async {
        final box = await aes.encrypt(plain, secretKey: SecretKey(key), nonce: List<int>.generate(12, (i) => i * 3));
        return [...ascii.encode('CPENC1'), ...box.nonce, ...box.mac.bytes, ...box.cipherText];
      }

      final wrapping = await (await Pbkdf2(macAlgorithm: Hmac.sha256(), iterations: iterations, bits: 256)
              .deriveKey(secretKey: SecretKey(utf8.encode('hunter22')), nonce: salt))
          .extractBytes();
      // Exactly how the PC writes its files: PascalCase names, decimals as numbers, local dates without a zone
      final txns = [
        {
          'Id': 'pc1', 'CreatedAt': '2026-11-01T09:00:00', 'UpdatedAt': '2026-11-01T09:00:00', 'Payee': 'Costco', 'Category': 'Groceries',
          'Account': 'Visa', 'Date': '2026-11-01T00:00:00', 'Amount': -212.45, 'Note': '', 'Splits': [], 'Tags': ['bulk'], 'Receipt': 'r1.jpg',
          'TransferAccount': '', 'ImportId': '',
        },
      ];
      final bills = [
        {
          'Id': 'b1', 'CreatedAt': '2026-11-01T09:00:00', 'UpdatedAt': '2026-11-01T09:00:00', 'Name': 'Rent', 'Amount': 1450.0,
          'NextDue': '2026-12-01T00:00:00', 'DueDay': 1, 'Repeats': 'Monthly', 'Category': 'Housing', 'Account': 'Checking', 'AutoPay': false,
          'LastPaid': null, 'Note': '', 'DebtId': '',
        },
      ];
      final lock = {
        'Version': 1, 'Kind': 'Password', 'Salt': base64.encode(salt), 'Iterations': iterations,
        'WrappedKey': base64.encode(await enc(dataKey, wrapping)), 'RecoverySalt': base64.encode(salt), 'RecoveryWrappedKey': base64.encode(await enc(dataKey, wrapping)),
        'AutoLockMinutes': 15,
      };
      final archive = Archive();
      void add(String name, List<int> b) => archive.addFile(ArchiveFile(name, b.length, b));
      add('lock.json', utf8.encode(jsonEncode(lock)));
      add('transactions.json', await enc(utf8.encode(jsonEncode(txns)), dataKey));
      add('bills.json', await enc(utf8.encode(jsonEncode(bills)), dataKey));
      add('preferences.json', await enc(utf8.encode(jsonEncode({'Currency': 'en-US', 'MonthStartDay': 1, 'UpdatedAt': '2026-11-01T09:00:00'})), dataKey));
      add('receipts/r1.jpg', await enc([1, 2, 3], dataKey));
      add('sync.json', utf8.encode('{}'));
      final zip = ZipEncoder().encode(archive)!;

      await expectLater(Backup.read(zip as dynamic), throwsA(isA<BackupLocked>().having((e) => e.wrong, 'wrong', false)));
      await expectLater(Backup.read(zip as dynamic, secret: 'nope'), throwsA(isA<BackupLocked>().having((e) => e.wrong, 'wrong', true)));
      final backup = await Backup.read(zip as dynamic, secret: 'hunter22');
      expect(backup.protected, isTrue);
      expect(backup.files['receipts/r1.jpg'], [1, 2, 3]);
      final s = Store.memory();
      await Backup.restore(s, backup);
      expect(s.transactions.single.cents, -21245);
      expect(s.transactions.single.tags, ['bulk']);
      expect(s.transactions.single.receipt, 'r1.jpg');
      expect(s.bills.single.amountCents, 145000);
      expect(s.bills.single.nextDue, DateTime(2026, 12, 1));
      expect(s.prefs.j['Id'], 'preferences');
    });

    test('not a backup', () async {
      await expectLater(Backup.read(utf8.encode('hello') as dynamic), throwsA(isA<FormatException>()));
    });
  });

  group('Updates from GitHub', () {
    test('picks the newest Android release with an APK', () {
      final releases = [
        {'tag_name': 'v1.5.0', 'draft': false, 'prerelease': false, 'assets': [{'name': 'WillowWallet-1.5.0-Setup.exe'}]},
        {
          'tag_name': 'android-v0.4.1', 'draft': false, 'prerelease': false, 'html_url': 'https://github.com/x/y/releases/tag/android-v0.4.1',
          'assets': [{'name': 'WillowWallet-Android-0.4.1.apk', 'browser_download_url': 'https://example.com/a.apk', 'size': 1000}],
        },
        {'tag_name': 'android-v0.10.0', 'draft': true, 'prerelease': false, 'assets': []},
        {'tag_name': 'android-v0.2.0', 'draft': false, 'prerelease': false, 'assets': [{'name': 'x.apk', 'browser_download_url': 'u'}]},
      ];
      final u = Updater.pick(releases, '0.3.0')!;
      expect(u.version, '0.4.1');
      expect(u.apkUrl, 'https://example.com/a.apk');
      expect(Updater.pick(releases, '0.4.1'), isNull);
      expect(Updater.isNewer('0.10.0', '0.9.9'), isTrue);
      expect(Updater.isNewer('0.3.0', '0.3.0'), isFalse);
    });

    test('Mac and Linux take the Windows release (v…) and their own file', () {
      final releases = [
        {'tag_name': 'android-v1.7.0', 'draft': false, 'prerelease': false, 'assets': [{'name': 'WillowWallet-Android-1.7.0.apk', 'browser_download_url': 'a'}]},
        {
          'tag_name': 'v1.7.0', 'draft': false, 'prerelease': false,
          'assets': [
            {'name': 'WillowWallet-1.7.0-Setup.exe', 'browser_download_url': 'w'},
            {'name': 'WillowWallet-1.7.0-macOS.dmg', 'browser_download_url': 'm'},
            {'name': 'WillowWallet-1.7.0-x86_64.AppImage', 'browser_download_url': 'l'},
          ],
        },
      ];
      try {
        hostOs = HostOs.macos;
        expect(Updater.pick(releases, '1.6.0')!.apkUrl, 'm');
        hostOs = HostOs.linux;
        expect(Updater.pick(releases, '1.6.0')!.apkUrl, 'l');
        hostOs = HostOs.android;
        expect(Updater.pick(releases, '1.6.0')!.apkUrl, 'a');
      } finally {
        hostOs = HostOs.android;
      }
    });
  });
}
