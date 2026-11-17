import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'theme.dart';
import 'widgets.dart';

/// A simple calculator: + − × ÷, percent, sign, and chained sums ("12 + 30 + 8 ="). Works like the Windows one.
class CalcEngine {
  String display = '0';

  /// "120 + 35 =" above the display.
  String history = '';
  double? _acc;
  String? _op;
  bool _fresh = true;

  static String fmt(double v) {
    if (v.isNaN || v.isInfinite) return 'Can\'t divide by zero';
    final r = (v * 1e10).roundToDouble() / 1e10;
    if (r == r.truncateToDouble() && r.abs() < 1e15) return r.toInt().toString();
    var s = r.toStringAsFixed(10);
    s = s.replaceFirst(RegExp(r'0+$'), '');
    return s.endsWith('.') ? s.substring(0, s.length - 1) : s;
  }

  double get value => double.tryParse(display) ?? 0;
  bool get isError => double.tryParse(display) == null;

  void digit(String d) {
    if (isError) clear();
    if (_fresh) {
      display = d == '.' ? '0.' : d;
      _fresh = false;
      return;
    }
    if (d == '.' && display.contains('.')) return;
    if (display.replaceAll(RegExp(r'[-.]'), '').length >= 15) return;
    display = display == '0' && d != '.' ? d : display + d;
  }

  double _apply(double a, String op, double b) => switch (op) {
        '+' => a + b,
        '−' => a - b,
        '×' => a * b,
        _ => b == 0 ? double.nan : a / b,
      };

  void op(String o) {
    if (isError) return;
    if (_op != null && !_fresh) {
      _acc = _apply(_acc!, _op!, value);
      display = fmt(_acc!);
    } else {
      _acc ??= value;
      if (_op == null) _acc = value;
    }
    _op = o;
    history = '${fmt(_acc!)} $o';
    _fresh = true;
  }

  void equals() {
    if (_op == null || isError) return;
    final b = value;
    history = '${fmt(_acc!)} $_op ${fmt(b)} =';
    display = fmt(_apply(_acc!, _op!, b));
    _acc = null;
    _op = null;
    _fresh = true;
  }

  /// 50 + 10% = 55; on its own, 10% = 0.1.
  void percent() {
    if (isError) return;
    final v = _acc != null && (_op == '+' || _op == '−') ? _acc! * value / 100 : value / 100;
    display = fmt(v);
    _fresh = false;
  }

  void negate() {
    if (isError || display == '0') return;
    display = display.startsWith('-') ? display.substring(1) : '-$display';
  }

  void back() {
    if (_fresh || isError) return;
    display = display.length <= 1 || (display.length == 2 && display.startsWith('-')) ? '0' : display.substring(0, display.length - 1);
  }

  void clear() {
    display = '0';
    history = '';
    _acc = null;
    _op = null;
    _fresh = true;
  }
}

/// Opens the calculator as a small panel beside the sidebar. Esc or a click outside closes it.
Future<void> showCalculator(BuildContext context) => showGeneralDialog<void>(
      context: context,
      barrierDismissible: true,
      barrierLabel: 'Close calculator',
      barrierColor: Colors.black.withValues(alpha: 0.08),
      transitionDuration: const Duration(milliseconds: 140),
      transitionBuilder: (_, a, __, child) => FadeTransition(
        opacity: a,
        child: SlideTransition(position: Tween(begin: const Offset(-0.04, 0), end: Offset.zero).animate(a), child: child),
      ),
      pageBuilder: (_, __, ___) => const Align(
        alignment: Alignment.bottomLeft,
        child: Padding(padding: EdgeInsets.only(left: 262, bottom: 24), child: CalculatorPanel()),
      ),
    );

class CalculatorPanel extends StatefulWidget {
  const CalculatorPanel({super.key});

  @override
  State<CalculatorPanel> createState() => _CalculatorPanelState();
}

class _CalculatorPanelState extends State<CalculatorPanel> {
  final calc = CalcEngine();
  final _focus = FocusNode();

  void _do(void Function() f) => setState(f);

  KeyEventResult _key(FocusNode _, KeyEvent e) {
    if (e is! KeyDownEvent && e is! KeyRepeatEvent) return KeyEventResult.ignored;
    final k = e.logicalKey;
    final ch = e.character;
    if (ch != null && RegExp(r'^[0-9.]$').hasMatch(ch)) {
      _do(() => calc.digit(ch));
    } else if (ch == '+') {
      _do(() => calc.op('+'));
    } else if (ch == '-') {
      _do(() => calc.op('−'));
    } else if (ch == '*' || ch == 'x') {
      _do(() => calc.op('×'));
    } else if (ch == '/') {
      _do(() => calc.op('÷'));
    } else if (ch == '%') {
      _do(calc.percent);
    } else if (ch == '=' || k == LogicalKeyboardKey.enter || k == LogicalKeyboardKey.numpadEnter) {
      _do(calc.equals);
    } else if (k == LogicalKeyboardKey.backspace) {
      _do(calc.back);
    } else if (k == LogicalKeyboardKey.delete || ch == 'c' || ch == 'C') {
      _do(calc.clear);
    } else if (k == LogicalKeyboardKey.escape) {
      Navigator.of(context).maybePop();
    } else {
      return KeyEventResult.ignored;
    }
    return KeyEventResult.handled;
  }

  @override
  void dispose() {
    _focus.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    Widget key(String label, VoidCallback onTap, {bool accent = false, bool op = false}) => Expanded(
          child: Padding(
            padding: const EdgeInsets.all(3),
            child: Material(
              color: accent ? p.accent : (op ? p.accentSoft : p.surfaceAlt),
              borderRadius: BorderRadius.circular(10),
              child: InkWell(
                borderRadius: BorderRadius.circular(10),
                onTap: () => _do(onTap),
                child: SizedBox(
                  height: 46,
                  child: Center(
                    child: Text(label,
                        style: TextStyle(fontSize: 18, fontWeight: op || accent ? FontWeight.w700 : FontWeight.w500, color: accent ? p.onAccent : (op ? p.accent : p.text))),
                  ),
                ),
              ),
            ),
          ),
        );
    Widget row(List<Widget> keys) => Row(children: keys);
    return Focus(
      focusNode: _focus,
      autofocus: true,
      onKeyEvent: _key,
      child: Material(
        color: p.surface,
        elevation: 12,
        shadowColor: Colors.black54,
        borderRadius: BorderRadius.circular(18),
        child: Container(
          width: 292,
          padding: const EdgeInsets.fromLTRB(12, 10, 12, 12),
          decoration: BoxDecoration(borderRadius: BorderRadius.circular(18), border: Border.all(color: p.border)),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Icon(Icons.calculate_outlined, size: 18, color: p.muted),
                  const SizedBox(width: 6),
                  Expanded(child: Text('Calculator', style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w700, color: p.muted))),
                  IconButton(
                    tooltip: 'Copy the result',
                    visualDensity: VisualDensity.compact,
                    onPressed: calc.isError
                        ? null
                        : () {
                            Clipboard.setData(ClipboardData(text: calc.display));
                            toast(context, 'Copied ${calc.display}');
                          },
                    icon: Icon(Icons.copy_rounded, size: 18, color: p.muted),
                  ),
                  IconButton(
                      tooltip: 'Close (Esc)',
                      visualDensity: VisualDensity.compact,
                      onPressed: () => Navigator.of(context).maybePop(),
                      icon: Icon(Icons.close_rounded, size: 18, color: p.muted)),
                ],
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(6, 2, 6, 8),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Text(calc.history, style: TextStyle(fontSize: 13, color: p.muted)),
                    FittedBox(
                      fit: BoxFit.scaleDown,
                      child: Text(calc.display, key: const Key('calc-display'), maxLines: 1, style: display(34, p.text, weight: FontWeight.w700)),
                    ),
                  ],
                ),
              ),
              row([key('C', calc.clear, op: true), key('±', calc.negate, op: true), key('%', calc.percent, op: true), key('÷', () => calc.op('÷'), op: true)]),
              row([key('7', () => calc.digit('7')), key('8', () => calc.digit('8')), key('9', () => calc.digit('9')), key('×', () => calc.op('×'), op: true)]),
              row([key('4', () => calc.digit('4')), key('5', () => calc.digit('5')), key('6', () => calc.digit('6')), key('−', () => calc.op('−'), op: true)]),
              row([key('1', () => calc.digit('1')), key('2', () => calc.digit('2')), key('3', () => calc.digit('3')), key('+', () => calc.op('+'), op: true)]),
              row([key('⌫', calc.back), key('0', () => calc.digit('0')), key('.', () => calc.digit('.')), key('=', calc.equals, accent: true)]),
            ],
          ),
        ),
      ),
    );
  }
}
