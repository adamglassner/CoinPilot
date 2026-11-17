import 'package:flutter/material.dart';

import '../insights.dart';
import '../money.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

/// "Heads up": spending alerts on the dashboard. Each can be dismissed; a bill price change can update the bill.
class AlertsCard extends StatelessWidget {
  const AlertsCard({super.key, required this.onForecast, required this.onBills});
  final VoidCallback onForecast, onBills;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final alerts = store.alerts(DateTime.now());
    if (alerts.isEmpty) return const SizedBox.shrink();
    IconData icon(String kind) => switch (kind) {
          'over' => Icons.error_outline_rounded,
          'budget' => Icons.pie_chart_outline_rounded,
          'bill-price' => Icons.trending_up_rounded,
          'dip' => Icons.south_east_rounded,
          _ => Icons.bolt_rounded,
        };
    Color color(String kind) => kind == 'over' || kind == 'dip' ? p.danger : p.warn;
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Panel(
        key: const Key('alerts-card'),
        padding: const EdgeInsets.fromLTRB(16, 12, 6, 6),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('HEADS UP', style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700, letterSpacing: 0.6, color: p.muted)),
            for (final a in alerts.take(4))
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Padding(padding: const EdgeInsets.only(top: 2), child: Icon(icon(a.kind), size: 20, color: color(a.kind))),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(a.title, style: const TextStyle(fontSize: 14.5, fontWeight: FontWeight.w700)),
                          Text(a.body, style: TextStyle(fontSize: 13, color: p.muted, height: 1.3)),
                          if (a.kind == 'bill-price' || a.kind == 'dip')
                            Align(
                              alignment: Alignment.centerLeft,
                              child: TextButton(
                                style: TextButton.styleFrom(padding: EdgeInsets.zero, minimumSize: const Size(0, 34), tapTargetSize: MaterialTapTargetSize.shrinkWrap),
                                onPressed: a.kind == 'dip'
                                    ? onForecast
                                    : () {
                                        store.applyBillPrice(a);
                                        toast(context, 'Bill updated to ${Money.format(a.newCents ?? 0)}.');
                                      },
                                child: Text(a.kind == 'dip' ? 'Open forecast' : 'Update the bill to ${Money.format(a.newCents ?? 0)}'),
                              ),
                            ),
                        ],
                      ),
                    ),
                    IconButton(
                      tooltip: 'Dismiss',
                      visualDensity: VisualDensity.compact,
                      onPressed: () => store.dismissAlert(a),
                      icon: Icon(Icons.close_rounded, size: 18, color: p.muted),
                    ),
                  ],
                ),
              ),
            if (alerts.length > 4)
              Padding(padding: const EdgeInsets.fromLTRB(30, 6, 0, 6), child: Text('+ ${alerts.length - 4} more', style: TextStyle(color: p.muted, fontSize: 13))),
          ],
        ),
      ),
    );
  }
}
