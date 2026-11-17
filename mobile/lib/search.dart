import 'models.dart';
import 'money.dart';

/// A transaction search in plain words: "dining over $50 in october", "amazon under 20", "groceries 2025".
/// Amounts ("over $50", "under 20", "> 100"), months, years, categories and accounts are understood; anything else is
/// matched against the payee, category, account, note and tags. WillowWallet for Windows reads searches the same way.
class TxnQuery {
  TxnQuery._(this.text, this.minCents, this.maxCents, this.month, this.year, this.words);

  final String text;
  final int? minCents, maxCents;
  final int? month, year;
  final List<String> words;

  static const _months = ['jan', 'feb', 'mar', 'apr', 'may', 'jun', 'jul', 'aug', 'sep', 'oct', 'nov', 'dec'];
  static const _filler = {'in', 'on', 'at', 'for', 'from', 'the', 'and', 'of', 'spent', 'spending', 'than'};

  /// Whether a search reaches beyond one month (a month name, a year, or an amount).
  bool get acrossMonths => month != null || year != null || minCents != null || maxCents != null;

  static TxnQuery parse(String input) {
    var s = ' ${input.toLowerCase().trim()} ';
    int? minC, maxC, month, year;
    int? amount(String v) => Money.parse(v.replaceAll(r'$', ''));
    for (final m in RegExp(r'(?:over|above|more than|>)\s*\$?(\d+(?:[.,]\d{1,2})?)').allMatches(s).toList().reversed) {
      minC = amount(m.group(1)!);
      s = s.replaceRange(m.start, m.end, ' ');
    }
    for (final m in RegExp(r'(?:under|below|less than|<)\s*\$?(\d+(?:[.,]\d{1,2})?)').allMatches(s).toList().reversed) {
      maxC = amount(m.group(1)!);
      s = s.replaceRange(m.start, m.end, ' ');
    }
    final y = RegExp(r'\b(20\d\d)\b').firstMatch(s);
    if (y != null) {
      year = int.parse(y.group(1)!);
      s = s.replaceRange(y.start, y.end, ' ');
    }
    final words = <String>[];
    for (final w in s.split(RegExp(r'\s+')).where((w) => w.isNotEmpty)) {
      final mi = w.length >= 3 ? _months.indexOf(w.substring(0, 3)) : -1;
      if (mi >= 0 && (w.length == 3 || 'january february march april may june july august september october november december'.contains(w))) {
        month = mi + 1;
      } else if (!_filler.contains(w)) {
        words.add(w);
      }
    }
    return TxnQuery._(input.trim(), minC, maxC, month, year, words);
  }

  bool matches(Txn t) {
    final size = t.cents.abs();
    if (minCents != null && size <= minCents!) return false;
    if (maxCents != null && size >= maxCents!) return false;
    if (month != null && t.date.month != month) return false;
    if (year != null && t.date.year != year) return false;
    if (words.isEmpty) return true;
    final hay = [t.payee, t.category, t.account, t.note, ...t.tags, Money.editable(t.cents)].join(' ').toLowerCase();
    return words.every(hay.contains);
  }
}
