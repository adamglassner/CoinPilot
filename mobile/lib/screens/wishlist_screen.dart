import 'package:flutter/material.dart';

import '../dates.dart';
import '../forecast.dart';
import '../kit.dart';
import '../money.dart';
import '../platform.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import 'transaction_sheet.dart';

/// Things you want, with a cooling-off period: WillowWallet asks again later whether you still want each one.
class WishlistScreen extends StatelessWidget {
  const WishlistScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final today = dayOnly(DateTime.now());
    final all = store.wishlist;
    final ready = [for (final w in all) if (w.waiting && !w.decideOn.isAfter(today)) w];
    final waiting = [for (final w in all) if (w.waiting && w.decideOn.isAfter(today)) w]..sort((a, b) => a.decideOn.compareTo(b.decideOn));
    final done = [for (final w in all) if (!w.waiting) w]..sort((a, b) => b.added.compareTo(a.added));
    final safe = store.forecast(today).safeToSpend;
    Widget tile(WishItem w) => RowTile(
          leading: IconBox(w.waiting ? Icons.favorite_border_rounded : (w.status == 'Bought' ? Icons.shopping_bag_outlined : Icons.do_not_disturb_alt_outlined), size: 38),
          title: w.name,
          subtitle: w.waiting
              ? (w.decideOn.isAfter(today) ? 'Decide in ${w.decideOn.difference(today).inDays} days' : 'Ready to decide')
              : '${w.status} · added ${shortDate(w.added)}',
          trailing: w.cents > 0 ? Money.format(w.cents, whole: true) : null,
          trailingSub: w.waiting && w.cents > 0 ? (w.cents <= safe ? 'fits' : 'over safe to spend') : null,
          trailingSubColor: w.cents <= safe ? p.positive : p.warn,
          onTap: () => _actions(context, w),
        );
    return SubPage(
      title: 'Wishlist',
      subtitle: 'Want something? Add it and wait. If you still want it in 30 days, it\'s probably worth it.',
      fab: () => showSheet<void>(context, const _WishSheet()),
      fabLabel: 'Add a wish',
      children: [
        if (all.isEmpty)
          const EmptyState(
            icon: Icons.favorite_border_rounded,
            title: 'Nothing on your wishlist',
            text: 'Add things you\'re tempted to buy. WillowWallet asks again after the cooling-off period, and shows whether it fits what\'s safe to spend.',
          ),
        if (ready.isNotEmpty) ...[const SectionTitle('Ready to decide'), ListPanel(children: [for (final w in ready) tile(w)])],
        if (waiting.isNotEmpty) ...[const SectionTitle('Cooling off'), ListPanel(children: [for (final w in waiting) tile(w)])],
        if (done.isNotEmpty) ...[const SectionTitle('Decided'), ListPanel(children: [for (final w in done.take(20)) tile(w)])],
        if (all.isNotEmpty) ...[
          const SizedBox(height: 12),
          Text('Safe to spend right now: ${Money.format(safe, whole: true)}.', textAlign: TextAlign.center, style: TextStyle(color: p.muted)),
        ],
      ],
    );
  }

  Future<void> _actions(BuildContext context, WishItem w) => showSheet<void>(
        context,
        Builder(builder: (c) {
          final store = StoreScope.read(c);
          void set(String status) {
            store.put(w.copy()..status = status);
            Navigator.pop(c);
          }

          return SheetFrame(
            title: w.name,
            subtitle: '${w.cents > 0 ? '${Money.format(w.cents)} · ' : ''}added ${shortDate(w.added)}',
            onDelete: () {
              store.remove(w);
              Navigator.pop(c);
            },
            children: [
              const SizedBox(height: 12),
              if (w.waiting) ...[
                RowTile(
                    leading: const IconBox(Icons.shopping_bag_outlined, size: 40),
                    title: 'I bought it',
                    subtitle: 'And add it to Transactions',
                    onTap: () {
                      set('Bought');
                      showTransactionSheet(context, prefill: TxnPrefill(payee: w.name, cents: w.cents > 0 ? -w.cents : null));
                    }),
                RowTile(leading: const IconBox(Icons.do_not_disturb_alt_outlined, size: 40), title: 'I don\'t want it anymore', subtitle: 'Money saved', onTap: () => set('Skipped')),
                RowTile(
                    leading: const IconBox(Icons.snooze_rounded, size: 40),
                    title: 'Wait another 30 days',
                    onTap: () {
                      store.put(w.copy()..decideOn = dayOnly(DateTime.now()).add(const Duration(days: 30)));
                      Navigator.pop(c);
                    }),
              ] else
                RowTile(leading: const IconBox(Icons.undo_rounded, size: 40), title: 'Put it back on the list', onTap: () => set('Waiting')),
              if (w.link.isNotEmpty) RowTile(leading: const IconBox(Icons.link_rounded, size: 40), title: 'Open the link', subtitle: w.link, onTap: () => Platform2.openUrl(w.link)),
            ],
          );
        }),
      );
}

class _WishSheet extends StatefulWidget {
  const _WishSheet();

  @override
  State<_WishSheet> createState() => _WishSheetState();
}

class _WishSheetState extends State<_WishSheet> {
  final _name = TextEditingController();
  final _price = TextEditingController();
  final _link = TextEditingController();
  int _days = 30;
  String? _error;

  @override
  Widget build(BuildContext context) => SheetFrame(
        title: 'Add a wish',
        error: _error,
        primary: 'Add',
        onPrimary: () {
          if (_name.text.trim().isEmpty) return setState(() => _error = 'What is it?');
          StoreScope.read(context).put(WishItem.create(name: _name.text.trim(), cents: Money.parse(_price.text) ?? 0, link: _link.text.trim(), days: _days));
          Navigator.pop(context);
          toast(context, 'Added. Ask yourself again in $_days days.');
        },
        children: [
          LabeledField(label: 'What', controller: _name, hint: 'New headphones', caps: TextCapitalization.sentences, autofocus: true),
          const FieldLabel('Price (optional)'),
          MoneyField(controller: _price, size: 22),
          LabeledField(label: 'Link (optional)', controller: _link, hint: 'https://…', caps: TextCapitalization.none),
          const FieldLabel('Cooling off'),
          Choices(options: const ['7 days', '30 days', '90 days'], selected: '$_days days', onChanged: (v) => setState(() => _days = int.parse(v.split(' ').first))),
        ],
      );
}
