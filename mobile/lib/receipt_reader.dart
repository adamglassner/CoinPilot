import 'dart:io';
import 'dart:math';

import 'package:google_mlkit_text_recognition/google_mlkit_text_recognition.dart';

import 'form.dart';

/// What a receipt says: the store, the total and the date (any of them can be missing).
class ReceiptGuess {
  const ReceiptGuess({this.payee, this.cents, this.date});
  final String? payee;
  final int? cents;
  final DateTime? date;

  bool get isEmpty => payee == null && cents == null && date == null;
}

/// Reads a receipt photo on the device (nothing is sent anywhere): Android uses Google's on-device text recognition,
/// Linux uses Tesseract when it's installed. Mac shows the photo for you to type from.
/// WillowWallet for Windows uses Windows' own text recognition and the same rules (Data/ReceiptReader.cs).
class ReceiptReader {
  /// Whether this device can read receipts by itself.
  static Future<bool> available() async {
    if (hostOs == HostOs.android) return true;
    if (hostOs == HostOs.linux) {
      try {
        return (await Process.run('tesseract', ['--version'])).exitCode == 0;
      } catch (_) {
        return false;
      }
    }
    return false;
  }

  /// The text on a receipt photo, or null when it can't be read here.
  static Future<String?> text(String imagePath) async {
    try {
      if (hostOs == HostOs.android) {
        final recognizer = TextRecognizer(script: TextRecognitionScript.latin);
        try {
          return (await recognizer.processImage(InputImage.fromFilePath(imagePath))).text;
        } finally {
          await recognizer.close();
        }
      }
      if (hostOs == HostOs.linux) {
        final r = await Process.run('tesseract', [imagePath, 'stdout']).timeout(const Duration(seconds: 40));
        return r.exitCode == 0 ? '${r.stdout}' : null;
      }
    } catch (_) {}
    return null;
  }

  static Future<ReceiptGuess> read(String imagePath, {DateTime? today}) async {
    final t = await text(imagePath);
    return t == null ? const ReceiptGuess() : parse(t, today: today);
  }

  static final _amount = RegExp(r'(?<![\d.,])\$?\s?(\d{1,3}(?:,\d{3})*|\d+)[.,](\d{2})(?!\d)');
  static const _skip = ['receipt', 'welcome', 'thank', 'store #', 'store#', 'tel', 'phone', 'www', 'http', 'order', 'invoice', 'cashier', 'register', 'date', 'time'];
  static const _months = ['jan', 'feb', 'mar', 'apr', 'may', 'jun', 'jul', 'aug', 'sep', 'oct', 'nov', 'dec'];

  static List<int> _amounts(String line) => [
        for (final m in _amount.allMatches(line)) int.parse(m.group(1)!.replaceAll(',', '')) * 100 + int.parse(m.group(2)!),
      ];

  /// Finds the store, total and date in a receipt's text.
  static ReceiptGuess parse(String text, {DateTime? today}) {
    final now = today ?? DateTime.now();
    final lines = [for (final l in text.split(RegExp(r'\r?\n'))) if (l.trim().isNotEmpty) l.trim()];

    // Store: one of the first lines with real words in it
    String? payee;
    for (final l in lines.take(6)) {
      final lower = l.toLowerCase();
      final letters = l.replaceAll(RegExp(r'[^A-Za-z]'), '').length;
      if (letters < 3 || RegExp(r'^\d').hasMatch(l) || _skip.any(lower.contains) || _amounts(l).isNotEmpty) continue;
      payee = _tidy(l);
      break;
    }

    // Total: the line that says so (not subtotal, tax or savings), else the biggest amount
    int? total;
    int score(String lower) => lower.contains('grand total') || lower.contains('amount due') || lower.contains('balance due') || lower.contains('total due')
        ? 3
        : lower.contains('total')
            ? 2
            : 0;
    var best = 0;
    for (var i = 0; i < lines.length; i++) {
      final lower = lines[i].toLowerCase();
      if (lower.contains('subtotal') || lower.contains('sub total') || lower.contains('sub-total') || lower.contains('savings') || lower.contains('tax')) continue;
      final s = score(lower);
      if (s == 0 || s < best) continue;
      var found = _amounts(lines[i]);
      if (found.isEmpty && i + 1 < lines.length) found = _amounts(lines[i + 1]);
      if (found.isEmpty) continue;
      total = found.last;
      best = s;
    }
    if (total == null) {
      final all = [for (final l in lines) ..._amounts(l)];
      if (all.isNotEmpty) total = all.reduce(max);
    }

    // Date: 11/16/2026, 11-16-26, 2026-11-16 or Nov 16, 2026
    DateTime? date;
    for (final l in lines) {
      date = _date(l, now);
      if (date != null) break;
    }
    return ReceiptGuess(payee: payee, cents: total, date: date);
  }

  static DateTime? _date(String line, DateTime now) {
    DateTime? ok(int y, int m, int d) {
      if (y < 100) y += 2000;
      if (m < 1 || m > 12 || d < 1 || d > 31) return null;
      final dt = DateTime(y, m, d);
      if (dt.month != m || dt.isAfter(now.add(const Duration(days: 1))) || dt.isBefore(DateTime(now.year - 2, now.month, now.day))) return null;
      return dt;
    }

    for (final m in RegExp(r'(?<!\d)(\d{4})-(\d{1,2})-(\d{1,2})(?!\d)').allMatches(line)) {
      final d = ok(int.parse(m.group(1)!), int.parse(m.group(2)!), int.parse(m.group(3)!));
      if (d != null) return d;
    }
    for (final m in RegExp(r'(?<!\d)(\d{1,2})[/-](\d{1,2})[/-](\d{2,4})(?!\d)').allMatches(line)) {
      final d = ok(int.parse(m.group(3)!), int.parse(m.group(1)!), int.parse(m.group(2)!));
      if (d != null) return d;
    }
    for (final m in RegExp(r'\b([A-Za-z]{3})[a-z]*\.?\s+(\d{1,2}),?\s+(\d{4})\b').allMatches(line)) {
      final mi = _months.indexOf(m.group(1)!.toLowerCase());
      if (mi < 0) continue;
      final d = ok(int.parse(m.group(3)!), mi + 1, int.parse(m.group(2)!));
      if (d != null) return d;
    }
    return null;
  }

  /// "KROGER #412" → "Kroger".
  static String _tidy(String s) {
    var t = s.replaceAll(RegExp(r'\s*#\s*\d+.*$'), '').replaceAll(RegExp(r'\s{2,}'), ' ').trim();
    if (t.isEmpty) t = s.trim();
    if (t == t.toUpperCase()) {
      t = t.toLowerCase().split(' ').map((w) => w.isEmpty ? w : w[0].toUpperCase() + w.substring(1)).join(' ');
    }
    return t;
  }
}
