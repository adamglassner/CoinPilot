import 'finance.dart';
import 'models.dart';
import 'store.dart';

// Reads bank and card statements the same way WillowWallet for Windows does (Data/Importer.cs):
// CSV in any layout (with payment-app layouts recognized), OFX and QFX.

class ImportedRow {
  ImportedRow({required this.date, required this.payee, required this.cents, this.category = '', this.note = '', this.importId = ''});
  final DateTime date;
  final String payee;
  /// Negative = money out.
  final int cents;
  final String category, note, importId;
  bool duplicate = false;
}

class CsvMapping {
  int date = -1, payee = -1, amount = -1, debit = -1, credit = -1, category = -1, note = -1, payeeIncoming = -1, status = -1;
  bool flipSign = false;
  String source = '';
  bool get isComplete => date >= 0 && payee >= 0 && (amount >= 0 || debit >= 0 || credit >= 0);
}

class Statement {
  Statement({required this.format, this.rows = const [], this.headers = const [], this.cells = const [], this.ledgerCents, this.ledgerDate});
  final String format;
  List<ImportedRow> rows;
  final List<String> headers;
  final List<List<String>> cells;
  final int? ledgerCents;
  final DateTime? ledgerDate;
}

class Importer {
  static Statement read(String text, String fileName) {
    final ext = fileName.toLowerCase();
    final upper = text.toUpperCase();
    if (ext.endsWith('.ofx') || ext.endsWith('.qfx') || upper.contains('<OFX') || upper.contains('<STMTTRN>')) {
      return readOfx(text, ext.endsWith('.qfx') ? 'QFX' : 'OFX');
    }
    return readCsv(text);
  }

  // ----- OFX / QFX -----

  static Statement readOfx(String text, [String format = 'OFX']) {
    final rows = <ImportedRow>[];
    final blocks = RegExp(r'<STMTTRN>(.*?)(</STMTTRN>|(?=<STMTTRN>)|</BANKTRANLIST>)', dotAll: true, caseSensitive: false).allMatches(text);
    for (final m in blocks) {
      final block = m.group(1)!;
      final date = _ofxDate(_tag(block, 'DTPOSTED'));
      final amount = double.tryParse(_tag(block, 'TRNAMT').replaceAll(',', '.'));
      if (date == null || amount == null) continue;
      final name = _tag(block, 'NAME'), memo = _tag(block, 'MEMO');
      rows.add(ImportedRow(
        date: date,
        cents: (amount * 100).round(),
        payee: name.isNotEmpty ? name : memo,
        note: name.isNotEmpty && memo.isNotEmpty && memo.toLowerCase() != name.toLowerCase() ? memo : '',
        importId: _tag(block, 'FITID'),
      ));
    }
    int? balance;
    DateTime? balanceDate;
    final ledger = RegExp(r'<LEDGERBAL>(.*?)(</LEDGERBAL>|<AVAILBAL>|</STMTRS>|</CCSTMTRS>)', dotAll: true, caseSensitive: false).firstMatch(text);
    if (ledger != null) {
      final b = double.tryParse(_tag(ledger.group(1)!, 'BALAMT').replaceAll(',', '.'));
      final d = _ofxDate(_tag(ledger.group(1)!, 'DTASOF'));
      if (b != null && d != null) {
        balance = (b * 100).round();
        balanceDate = d;
      }
    }
    return Statement(format: format, rows: rows, ledgerCents: balance, ledgerDate: balanceDate);
  }

  static String _tag(String block, String name) {
    final m = RegExp('<$name>\\s*([^<\\r\\n]*)', caseSensitive: false).firstMatch(block);
    if (m == null) return '';
    return m.group(1)!.trim().replaceAll('&amp;', '&').replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&quot;', '"').replaceAll('&#39;', "'");
  }

  static DateTime? _ofxDate(String s) {
    if (s.length < 8) return null;
    final y = int.tryParse(s.substring(0, 4)), m = int.tryParse(s.substring(4, 6)), d = int.tryParse(s.substring(6, 8));
    if (y == null || m == null || d == null || m < 1 || m > 12 || d < 1 || d > 31) return null;
    return DateTime(y, m, d);
  }

  // ----- CSV -----

  static Statement readCsv(String text) {
    final lines = parseCsv(text);
    var header = lines.indexWhere((l) => l.length >= 3 && l.any((c) => c.toLowerCase().contains('date')));
    if (header < 0) header = 0;
    final headers = lines.length > header ? [for (final h in lines[header]) h.trim()] : <String>[];
    final cells = [for (final l in lines.skip(header + 1)) if (l.any((c) => c.trim().isNotEmpty)) l];
    return Statement(format: 'CSV', headers: headers, cells: cells);
  }

  /// Splits CSV text into rows of cells. Handles quotes, and comma, semicolon or tab separators.
  static List<List<String>> parseCsv(String text) {
    if (text.startsWith('﻿')) text = text.substring(1);
    final firstLine = text.split('\n').firstWhere((l) => l.trim().isNotEmpty, orElse: () => '');
    final sep = [',', ';', '\t'].reduce((a, b) => firstLine.split(a).length >= firstLine.split(b).length ? a : b);
    final rows = <List<String>>[];
    var row = <String>[];
    final cell = StringBuffer();
    var quoted = false;
    for (var i = 0; i < text.length; i++) {
      final c = text[i];
      if (quoted) {
        if (c == '"') {
          if (i + 1 < text.length && text[i + 1] == '"') {
            cell.write('"');
            i++;
          } else {
            quoted = false;
          }
        } else {
          cell.write(c);
        }
      } else if (c == '"') {
        quoted = true;
      } else if (c == sep) {
        row.add(cell.toString());
        cell.clear();
      } else if (c == '\n' || c == '\r') {
        if (c == '\r' && i + 1 < text.length && text[i + 1] == '\n') i++;
        row.add(cell.toString());
        cell.clear();
        if (row.length > 1 || row[0].isNotEmpty) rows.add(row);
        row = [];
      } else {
        cell.write(c);
      }
    }
    if (cell.isNotEmpty || row.isNotEmpty) {
      row.add(cell.toString());
      rows.add(row);
    }
    return rows;
  }

  /// Guesses the columns from common header names (Date, Description, Amount, Debit/Credit, Category…).
  static CsvMapping guessMapping(List<String> headers) {
    int find(List<String> names) {
      for (var i = 0; i < headers.length; i++) {
        if (names.contains(headers[i].trim().toLowerCase())) return i;
      }
      for (var i = 0; i < headers.length; i++) {
        final h = headers[i].trim().toLowerCase();
        if (names.any(h.contains)) return i;
      }
      return -1;
    }

    final map = CsvMapping()
      ..date = find(['date', 'transaction date', 'posted date', 'posting date', 'trans date'])
      ..payee = find(['payee', 'description', 'merchant', 'name', 'details', 'memo'])
      ..amount = find(['amount', 'transaction amount', 'amt'])
      ..debit = find(['debit', 'withdrawal', 'withdrawals', 'money out', 'paid out'])
      ..credit = find(['credit', 'deposit', 'deposits', 'money in', 'paid in'])
      ..category = find(['category']);
    if (map.amount >= 0) {
      map.debit = -1;
      map.credit = -1;
    }
    final memo = find(['memo', 'note', 'notes', 'reference']);
    if (memo >= 0 && memo != map.payee) map.note = memo;
    return paymentAppMapping(headers) ?? map;
  }

  /// PayPal, Venmo and Cash App activity downloads have their own layouts.
  static CsvMapping? paymentAppMapping(List<String> headers) {
    int exact(String name) => headers.indexWhere((h) => h.trim().toLowerCase() == name.toLowerCase());
    bool has(List<String> names) => names.every((n) => exact(n) >= 0);
    if (has(['Datetime', 'From', 'To']) && exact('Amount (total)') >= 0) {
      return CsvMapping()
        ..source = 'Venmo'
        ..date = exact('Datetime')
        ..amount = exact('Amount (total)')
        ..payee = exact('To')
        ..payeeIncoming = exact('From')
        ..note = exact('Note')
        ..status = exact('Status');
    }
    if (has(['Date', 'Name of sender/receiver']) && (exact('Net Amount') >= 0 || exact('Amount') >= 0)) {
      return CsvMapping()
        ..source = 'Cash App'
        ..date = exact('Date')
        ..amount = exact('Net Amount') >= 0 ? exact('Net Amount') : exact('Amount')
        ..payee = exact('Name of sender/receiver')
        ..note = exact('Notes')
        ..status = exact('Status');
    }
    if (has(['Date', 'Name', 'Gross', 'Net'])) {
      return CsvMapping()
        ..source = 'PayPal'
        ..date = exact('Date')
        ..amount = exact('Net')
        ..payee = exact('Name')
        ..note = exact('Type')
        ..status = exact('Status');
    }
    return null;
  }

  static const _skippedStatuses = ['pending', 'denied', 'declined', 'failed', 'canceled', 'cancelled', 'removed', 'reversed', 'refused', 'expired'];

  /// Turns CSV cells into rows using the mapping. Returns the rows and how many were skipped.
  static (List<ImportedRow>, int) apply(Statement s, CsvMapping map) {
    var skipped = 0;
    final rows = <ImportedRow>[];
    if (!map.isComplete) return (rows, 0);
    String cell(List<String> r, int i) => i >= 0 && i < r.length ? r[i].trim() : '';
    for (final r in s.cells) {
      final date = parseDate(cell(r, map.date));
      if (date == null) {
        skipped++;
        continue;
      }
      int? amount;
      if (map.amount >= 0) {
        amount = parseAmount(cell(r, map.amount));
      } else {
        final debit = parseAmount(cell(r, map.debit)), credit = parseAmount(cell(r, map.credit));
        if (debit != null || credit != null) amount = (credit?.abs() ?? 0) - (debit?.abs() ?? 0);
      }
      if (amount == null || amount == 0) {
        skipped++;
        continue;
      }
      final status = cell(r, map.status).toLowerCase();
      if (status.isNotEmpty && _skippedStatuses.any(status.startsWith)) {
        skipped++;
        continue;
      }
      if (map.flipSign) amount = -amount;
      var payee = amount > 0 && map.payeeIncoming >= 0 ? cell(r, map.payeeIncoming) : cell(r, map.payee);
      var note = cell(r, map.note);
      if (payee.isEmpty && note.isNotEmpty) {
        payee = note;
        note = '';
      }
      rows.add(ImportedRow(date: date, cents: amount, payee: payee, category: cell(r, map.category), note: note));
    }
    return (rows, skipped);
  }

  static const _months = {'jan': 1, 'feb': 2, 'mar': 3, 'apr': 4, 'may': 5, 'jun': 6, 'jul': 7, 'aug': 8, 'sep': 9, 'oct': 10, 'nov': 11, 'dec': 12};

  /// Reads common statement date formats. US month/day first, then day/month when the day is over 12.
  static DateTime? parseDate(String s) {
    s = s.trim().replaceAll('"', '').replaceAll(RegExp(r'\s+[A-Z]{2,5}$'), '');
    if (s.isEmpty) return null;
    DateTime? ok(int y, int m, int d) {
      if (y < 100) y += 2000;
      if (m < 1 || m > 12 || d < 1 || d > 31) return null;
      final r = DateTime(y, m, d);
      return r.month == m ? r : null;
    }

    // 2026-11-16, 2026/11/16, 2026-11-16T10:00:00, 2026-11-16 10:00:00
    var m = RegExp(r'^(\d{4})[-/](\d{1,2})[-/](\d{1,2})').firstMatch(s);
    if (m != null) return ok(int.parse(m[1]!), int.parse(m[2]!), int.parse(m[3]!));
    // 20261116
    m = RegExp(r'^(\d{4})(\d{2})(\d{2})$').firstMatch(s);
    if (m != null) return ok(int.parse(m[1]!), int.parse(m[2]!), int.parse(m[3]!));
    // 11/16/2026, 11-16-26, 16.11.2026 (dots are day first)
    m = RegExp(r'^(\d{1,2})([/.\-])(\d{1,2})[/.\-](\d{2,4})').firstMatch(s);
    if (m != null) {
      final a = int.parse(m[1]!), b = int.parse(m[3]!), y = int.parse(m[4]!);
      if (m[2] == '.') return ok(y, b, a);
      return a > 12 ? ok(y, b, a) : ok(y, a, b);
    }
    // Nov 16, 2026 / November 16, 2026
    m = RegExp(r'^([A-Za-z]{3})[A-Za-z]*\.?\s+(\d{1,2}),?\s+(\d{4})').firstMatch(s);
    if (m != null && _months[m[1]!.toLowerCase()] != null) return ok(int.parse(m[3]!), _months[m[1]!.toLowerCase()]!, int.parse(m[2]!));
    // 16 Nov 2026
    m = RegExp(r'^(\d{1,2})\s+([A-Za-z]{3})[A-Za-z]*\.?\s+(\d{4})').firstMatch(s);
    if (m != null && _months[m[2]!.toLowerCase()] != null) return ok(int.parse(m[3]!), _months[m[2]!.toLowerCase()]!, int.parse(m[1]!));
    return null;
  }

  /// Reads "-12.34", "$1,234.56", "(12.34)", "12.34-", "12.34 DR", "1.234,56" as cents. Null when it isn't a number.
  static int? parseAmount(String s) {
    s = s.trim().replaceAll('"', '');
    if (s.isEmpty) return null;
    var negative = false;
    if (s.startsWith('(') && s.endsWith(')')) {
      negative = true;
      s = s.substring(1, s.length - 1);
    }
    final upper = s.toUpperCase();
    if (upper.endsWith('DR')) {
      negative = true;
      s = s.substring(0, s.length - 2);
    } else if (upper.endsWith('CR')) {
      s = s.substring(0, s.length - 2);
    }
    s = s.trim();
    if (s.endsWith('-')) {
      negative = true;
      s = s.substring(0, s.length - 1);
    }
    s = s.replaceAll('−', '-').replaceAll(RegExp(r'[^0-9.,\-]'), '');
    if (s.startsWith('-')) {
      negative = !negative;
      s = s.substring(1);
    }
    if (s.isEmpty) return null;
    final lastDot = s.lastIndexOf('.'), lastComma = s.lastIndexOf(',');
    if (lastComma > lastDot && (s.length - lastComma - 1 == 1 || s.length - lastComma - 1 == 2)) {
      s = s.replaceAll('.', '').replaceAll(',', '.');
    } else {
      s = s.replaceAll(',', '');
    }
    final v = double.tryParse(s);
    if (v == null) return null;
    final cents = (v * 100).round();
    return negative ? -cents : cents;
  }
}

extension ImportMath on Store {
  /// Marks rows already in WillowWallet (same bank id, or same date, amount and payee in that account).
  void markDuplicates(List<ImportedRow> rows, String account) {
    final existing = transactions.where((t) => t.account.toLowerCase() == account.toLowerCase()).toList();
    final ids = {for (final t in existing) if (t.importId.isNotEmpty) t.importId};
    String key(DateTime d, int c, String p) => '${d.year}-${d.month}-${d.day}|$c|${Subscriptions.normalize(p)}';
    final keys = {for (final t in existing) key(t.date, t.cents, t.payee)};
    for (final r in rows) {
      r.duplicate = (r.importId.isNotEmpty && ids.contains(r.importId)) || keys.contains(key(r.date, r.cents, r.payee));
    }
  }

  /// The category a row will get: its own (if it matches one of yours), your rules and history, or Other.
  String categoryForRow(ImportedRow r) {
    final income = r.cents > 0;
    for (final c in categoriesFor(income: income)) {
      if (c.toLowerCase() == r.category.toLowerCase()) return c;
    }
    return categoryForPayee(r.payee, income: income) ?? (income ? 'Other income' : 'Other');
  }

  /// Saves the new (non-duplicate) rows to an account. Returns how many were added.
  int commitImport(List<ImportedRow> rows, String account) {
    final add = [
      for (final r in rows)
        if (!r.duplicate)
          Txn(
            payee: r.payee.isEmpty ? '(no description)' : r.payee,
            category: categoryForRow(r),
            account: account,
            date: r.date,
            cents: r.cents,
            note: r.note,
            extra: {'ImportId': r.importId},
          ),
    ];
    saveTxns(add);
    return add.length;
  }
}
