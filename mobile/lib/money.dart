/// A currency CoinPilot can show amounts in.
class Currency {
  const Currency(this.code, this.symbol, this.name, {this.decimals = 2});
  final String code;
  final String symbol;
  final String name;
  final int decimals;
}

const currencies = <Currency>[
  Currency('USD', r'$', 'US dollar'),
  Currency('CAD', r'$', 'Canadian dollar'),
  Currency('AUD', r'$', 'Australian dollar'),
  Currency('MXN', r'$', 'Mexican peso'),
  Currency('EUR', '€', 'Euro'),
  Currency('GBP', '£', 'British pound'),
  Currency('INR', '₹', 'Indian rupee'),
  Currency('JPY', '¥', 'Japanese yen', decimals: 0),
];

Currency currencyFor(String code) => currencies.firstWhere((c) => c.code == code, orElse: () => currencies.first);

/// Amounts are kept as whole cents (an int) so they always add up exactly.
class Money {
  static Currency currency = currencies.first;

  /// "$1,234.56", "−$12.00" (a real minus sign), or "+$50.00" with [signed].
  static String format(int cents, {bool signed = false, bool whole = false}) {
    final neg = cents < 0;
    final abs = cents.abs();
    final noCents = whole || currency.decimals == 0;
    final units = noCents ? (abs + 50) ~/ 100 : abs ~/ 100;
    var s = currency.symbol + _group(units);
    if (!noCents) s += '.${(abs % 100).toString().padLeft(2, '0')}';
    if (neg) return '−$s';
    if (signed && cents > 0) return '+$s';
    return s;
  }

  /// "12.50" for editing (no symbol, no grouping, always positive).
  static String editable(int cents) {
    final abs = cents.abs();
    if (currency.decimals == 0) return ((abs + 50) ~/ 100).toString();
    return '${abs ~/ 100}.${(abs % 100).toString().padLeft(2, '0')}';
  }

  /// Reads what someone typed ("1,234.5", "$12") as cents; null when it isn't a number.
  static int? parse(String text) {
    final t = text.replaceAll(RegExp(r'[^0-9.]'), '');
    if (t.isEmpty || t == '.') return null;
    final v = double.tryParse(t);
    if (v == null || v.isNaN || v.isInfinite) return null;
    return (v * 100).round();
  }

  static String _group(int n) {
    final s = n.toString();
    final b = StringBuffer();
    for (var i = 0; i < s.length; i++) {
      if (i > 0 && (s.length - i) % 3 == 0) b.write(',');
      b.write(s[i]);
    }
    return b.toString();
  }
}
