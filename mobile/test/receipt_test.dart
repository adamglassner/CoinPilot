import 'package:coinpilot/models.dart';
import 'package:coinpilot/money.dart';
import 'package:coinpilot/receipt_reader.dart';
import 'package:coinpilot/reminders.dart';
import 'package:coinpilot/store.dart';
import 'package:coinpilot/widget_sync.dart';
import 'package:flutter_test/flutter_test.dart';

/// The receipt examples WillowWallet for Windows checks too (Services/SelfTest.cs, "Receipts:" checks).
const kroger = '''
KROGER #412
1234 Main St
Columbus OH 43215
Tel 614-555-0100
11/16/2026 5:42 PM
MILK 2% GAL      3.49
BREAD            2.99
SUBTOTAL         6.48
TAX              0.39
TOTAL            6.87
VISA ****1234    6.87
THANK YOU
''';

const starbucks = '''
Starbucks Coffee
Store 1234
Nov 15, 2026
Latte 5.25
Amount due \$5.68
''';

const hardware = '''
A&B Hardware
2026-11-10
Screws 4.50
Hammer 18.00
''';

void main() {
  final today = DateTime(2026, 11, 17);

  test('receipts: the store, the total (not the subtotal or tax) and the date', () {
    final k = ReceiptReader.parse(kroger, today: today);
    expect(k.payee, 'Kroger');
    expect(k.cents, 687);
    expect(k.date, DateTime(2026, 11, 16));

    final s = ReceiptReader.parse(starbucks, today: today);
    expect(s.payee, 'Starbucks Coffee');
    expect(s.cents, 568);
    expect(s.date, DateTime(2026, 11, 15));

    final h = ReceiptReader.parse(hardware, today: today);
    expect(h.payee, 'A&B Hardware');
    expect(h.cents, 1800); // no total line: the biggest amount
    expect(h.date, DateTime(2026, 11, 10));
  });

  test('receipts: nothing readable, and dates in the future are ignored', () {
    expect(ReceiptReader.parse('', today: today).isEmpty, isTrue);
    expect(ReceiptReader.parse('Shop\n12/25/2026\nTOTAL 9.99', today: today).date, isNull);
    expect(ReceiptReader.parse('Shop\nTOTAL 1,234.50', today: today).cents, 123450);
  });

  test('quick add: "12.50 coffee" and "Gas 40"', () {
    Money.currency = currencies.first;
    expect(Reminders.parseQuickAdd('12.50 coffee'), (1250, 'Coffee'));
    expect(Reminders.parseQuickAdd('Gas \$40'), (4000, 'Gas'));
    expect(Reminders.parseQuickAdd(''), (null, null));
  });

  test('home screen widget: safe to spend, hidden behind a PIN', () {
    Money.currency = currencies.first;
    final s = Store.memory();
    expect(HomeScreenWidget.content(s, today).$2, '—');
    s.loadSample(now: today);
    expect(HomeScreenWidget.content(s, today).$1, 'Safe to spend');
    s.updateSettings((x) => x
      ..lockKind = 'PIN'
      ..lockHash = 'x');
    expect(HomeScreenWidget.content(s, today), ('WillowWallet', 'Locked', 'Open to see your numbers'));
  });
}
