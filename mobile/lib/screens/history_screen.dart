import 'package:flutter/material.dart';

import '../dates.dart';
import '../form.dart';
import '../kit.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

/// What changed recently, on this device or synced from another one, with Undo.
class HistoryScreen extends StatelessWidget {
  const HistoryScreen({super.key});

  static const kinds = {
    'transactions': 'Transactions', 'budgets': 'Budgets', 'accounts': 'Accounts', 'debts': 'Debts', 'bills': 'Bills', 'goals': 'Goals',
    'categories': 'Categories', 'rules': 'Rules', 'income': 'Income', 'tax-documents': 'Taxes', 'ious': 'IOUs', 'mileage': 'Mileage',
    'documents': 'Documents', 'wishlist': 'Wishlist', 'gift-plans': 'Gifts',
  };

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final list = store.history.reversed.take(200).toList();
    String? lastDay;
    final children = <Widget>[];
    for (final h in list) {
      final at = DateTime.tryParse('${h['At']}')?.toLocal() ?? DateTime.now();
      final day = dayLabel(at);
      if (day != lastDay) {
        children.add(SectionTitle(day));
        lastDay = day;
      }
      final remote = h['Remote'] == true;
      final action = '${h['Action']}';
      children.add(Panel(
        padding: const EdgeInsets.fromLTRB(14, 10, 6, 10),
        child: Row(children: [
          Icon(action.startsWith('Added') ? Icons.add_circle_outline_rounded : action.startsWith('Deleted') ? Icons.remove_circle_outline_rounded : Icons.edit_outlined,
              size: 20, color: action.startsWith('Deleted') ? p.danger : p.accent),
          const SizedBox(width: 10),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text('${kinds[h['Collection']] ?? h['Collection']}: ${action.toLowerCase()} ${h['Label'] ?? ''}'.trim(), style: const TextStyle(fontWeight: FontWeight.w600)),
              Text('${TimeOfDay.fromDateTime(at).format(context)} · ${remote ? 'synced from another device' : 'on this $deviceWord'}', style: TextStyle(fontSize: 12.5, color: p.muted)),
            ]),
          ),
          if (!action.startsWith('Undid'))
            TextButton(
              onPressed: () {
                store.undo(h);
                toast(context, 'Undone.');
              },
              child: const Text('Undo'),
            ),
        ]),
      ));
      children.add(const SizedBox(height: 8));
    }
    return SubPage(
      title: 'Change history',
      subtitle: 'What changed recently, here or on your other devices. Undo puts it back the way it was.',
      children: list.isEmpty ? [const EmptyState(icon: Icons.history_rounded, title: 'No changes yet', text: 'Changes you make, and ones synced from your other devices, show up here.')] : children,
    );
  }
}
