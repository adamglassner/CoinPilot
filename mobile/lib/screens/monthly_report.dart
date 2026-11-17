import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../dates.dart';
import '../insights.dart';
import '../kit.dart';
import '../money.dart';
import '../platform.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

/// One month on a page: in and out, budgets, where it went, bills, savings and net worth. Save it as a web page to
/// keep, print or turn into a PDF.
class MonthlyReportScreen extends StatefulWidget {
  const MonthlyReportScreen({super.key, this.month});
  final DateTime? month;

  @override
  State<MonthlyReportScreen> createState() => _MonthlyReportScreenState();
}

class _MonthlyReportScreenState extends State<MonthlyReportScreen> {
  late DateTime _month = widget.month ?? DateTime.now();

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final r = store.monthlyReport(_month);
    final name = store.monthTitle(r.month);
    String vs(int now, int before) {
      if (before <= 0) return '';
      final c = ((now - before) * 100 / before).round();
      return '${c > 0 ? '+' : ''}$c% vs last month';
    }

    return SubPage(
      title: 'Monthly report',
      subtitle: 'Your month on one page. Save it to keep it, print it or make a PDF.',
      actions: [IconButton(tooltip: 'Save the report', onPressed: () => _save(context, r, name), icon: const Icon(Icons.download_rounded))],
      children: [
        MonthSwitcher(month: _month, onChanged: (m) => setState(() => _month = m)),
        const SizedBox(height: 12),
        Row(
          children: [
            Expanded(child: StatTile(label: 'Came in', value: Money.format(r.income, whole: true), note: vs(r.income, r.incomeBefore), valueColor: p.positive)),
            const SizedBox(width: 10),
            Expanded(child: StatTile(label: 'Spent', value: Money.format(r.spent, whole: true), note: vs(r.spent, r.spentBefore))),
          ],
        ),
        const SizedBox(height: 10),
        Row(
          children: [
            Expanded(
              child: StatTile(
                label: r.saved >= 0 ? 'Saved' : 'Overspent',
                value: Money.format(r.saved.abs(), whole: true),
                note: r.savingsRate == null ? '' : '${(r.savingsRate! * 100).round()}% of income',
                valueColor: r.saved < 0 ? p.danger : null,
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: StatTile(
                label: 'Net worth',
                value: Money.format(r.netWorthEnd, whole: true),
                note: '${Money.format(r.netWorthEnd - r.netWorthStart, signed: true, whole: true)} this month',
              ),
            ),
          ],
        ),
        if (r.budgets.isNotEmpty) ...[
          SectionTitle(r.budgetsOver == 0 ? 'Budgets · all on track' : 'Budgets · ${r.budgetsOver} over'),
          Panel(
            child: Column(
              children: [
                for (final (cat, spent, avail) in r.budgets)
                  Padding(
                    padding: const EdgeInsets.symmetric(vertical: 6),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Row(children: [
                          Expanded(child: Text(cat, style: const TextStyle(fontWeight: FontWeight.w600))),
                          Text('${Money.format(spent, whole: true)} of ${Money.format(avail, whole: true)}',
                              style: TextStyle(fontSize: 13, color: spent > avail ? p.danger : p.muted, fontWeight: spent > avail ? FontWeight.w700 : FontWeight.w500)),
                        ]),
                        const SizedBox(height: 6),
                        ProgressBar(value: avail <= 0 ? 1 : spent / avail, color: spent > avail ? p.danger : p.accent, height: 6),
                      ],
                    ),
                  ),
              ],
            ),
          ),
        ],
        if (r.topCategories.isNotEmpty) ...[
          const SectionTitle('Where it went'),
          ListPanel(children: [
            for (final c in r.topCategories)
              RowTile(leading: CategoryAvatar(c.key, size: 36), title: c.key, trailing: Money.format(c.value, whole: true), trailingSub: r.spent <= 0 ? null : '${(c.value * 100 / r.spent).round()}%'),
          ]),
        ],
        const SectionTitle('Also this month'),
        ListPanel(children: [
          if (r.biggest != null)
            RowTile(leading: const IconBox(Icons.shopping_bag_outlined, size: 36), title: 'Biggest purchase', subtitle: '${r.biggest!.payee} · ${shortDate(r.biggest!.date)}', trailing: Money.format(-r.biggest!.cents)),
          RowTile(leading: const IconBox(Icons.event_available_outlined, size: 36), title: 'Bills paid', trailing: '${r.billsPaid}'),
          RowTile(leading: const IconBox(Icons.savings_outlined, size: 36), title: 'Put toward goals', trailing: Money.format(r.savedToGoals, whole: true)),
          RowTile(leading: const IconBox(Icons.credit_card_rounded, size: 36), title: 'Paid on debts', trailing: Money.format(r.debtPaid, whole: true)),
          RowTile(leading: const IconBox(Icons.receipt_long_outlined, size: 36), title: 'Transactions', trailing: '${r.transactions}'),
        ]),
        const SizedBox(height: 14),
        OutlinedButton.icon(onPressed: () => _save(context, r, name), icon: const Icon(Icons.download_rounded, size: 19), label: const Text('Save as a web page (print or PDF from it)')),
      ],
    );
  }

  Future<void> _save(BuildContext context, MonthlyReport r, String name) async {
    final html = monthlyReportHtml(r, name);
    final ok = await Platform2.saveFile('WillowWallet ${name.replaceAll(RegExp(r'[^A-Za-z0-9 –-]'), '')}.html', Uint8List.fromList(utf8.encode(html)));
    if (ok && context.mounted) toast(context, 'Saved. Open it in a browser to print or save as PDF.');
  }
}

/// The report as a small, printable web page.
String monthlyReportHtml(MonthlyReport r, String name) {
  String e(String s) => const HtmlEscape().convert(s);
  String m(int c) => e(Money.format(c, whole: true));
  final rows = [
    for (final (cat, spent, avail) in r.budgets) '<tr><td>${e(cat)}</td><td>${m(spent)}</td><td>${m(avail)}</td><td class="${spent > avail ? 'bad' : 'ok'}">${spent > avail ? 'Over' : 'On track'}</td></tr>',
  ].join();
  final cats = [for (final c in r.topCategories) '<tr><td>${e(c.key)}</td><td>${m(c.value)}</td></tr>'].join();
  return '''<!doctype html><html><head><meta charset="utf-8"><title>WillowWallet · ${e(name)}</title>
<style>body{font-family:system-ui,sans-serif;max-width:720px;margin:32px auto;color:#14211B;padding:0 16px}h1{margin:0}h2{margin-top:28px;font-size:18px}
.tiles{display:flex;gap:12px;flex-wrap:wrap}.tile{flex:1;min-width:140px;border:1px solid #dde5e0;border-radius:12px;padding:12px}.tile b{display:block;font-size:22px}
table{width:100%;border-collapse:collapse}td{padding:6px 4px;border-bottom:1px solid #eef2ef}.bad{color:#c2410c;font-weight:600}.ok{color:#1f7a55}.muted{color:#6b7a73}</style></head><body>
<p class="muted">WillowWallet monthly report</p><h1>${e(name)}</h1>
<div class="tiles"><div class="tile">Came in<b>${m(r.income)}</b></div><div class="tile">Spent<b>${m(r.spent)}</b></div>
<div class="tile">${r.saved >= 0 ? 'Saved' : 'Overspent'}<b>${m(r.saved.abs())}</b>${r.savingsRate == null ? '' : '${(r.savingsRate! * 100).round()}% of income'}</div>
<div class="tile">Net worth<b>${m(r.netWorthEnd)}</b>${e(Money.format(r.netWorthEnd - r.netWorthStart, signed: true, whole: true))} this month</div></div>
${rows.isEmpty ? '' : '<h2>Budgets</h2><table><tr class="muted"><td>Category</td><td>Spent</td><td>Budget</td><td></td></tr>$rows</table>'}
${cats.isEmpty ? '' : '<h2>Where it went</h2><table>$cats</table>'}
<h2>Also this month</h2><table>
${r.biggest == null ? '' : '<tr><td>Biggest purchase</td><td>${e(r.biggest!.payee)}, ${e(Money.format(-r.biggest!.cents))}</td></tr>'}
<tr><td>Bills paid</td><td>${r.billsPaid}</td></tr><tr><td>Put toward goals</td><td>${m(r.savedToGoals)}</td></tr>
<tr><td>Paid on debts</td><td>${m(r.debtPaid)}</td></tr><tr><td>Transactions</td><td>${r.transactions}</td></tr></table>
</body></html>''';
}
