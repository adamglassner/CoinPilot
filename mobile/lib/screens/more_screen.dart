import 'package:flutter/material.dart';

import '../dates.dart';
import '../finance.dart';
import '../kit.dart';
import '../money.dart';
import '../store.dart';
import '../sync/scope.dart';
import '../theme.dart';
import '../widgets.dart';
import 'accounts_screen.dart';
import 'bills_screen.dart';
import 'debts_screen.dart';
import 'goals_screen.dart';
import 'import_screen.dart';
import 'income_screen.dart';
import 'reports_screen.dart';
import 'settings_screen.dart';
import 'sync_screen.dart';
import 'taxes_screen.dart';

/// Everything that isn't a tab: accounts, bills, income, debts, goals, reports, taxes, sync and settings.
class MoreScreen extends StatelessWidget {
  const MoreScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final sync = SyncScope.of(context);
    final updater = UpdaterScope.of(context);
    final p = context.pal;
    final today = dayOnly(DateTime.now());
    final due = store.billsDueWithin(7, today);
    final asking = store.askingPaydays(today);
    final activeDebts = store.debts.where((d) => !d.isPaidOff).length;
    final goals = store.goals;

    final tiles = [
      _Feature('Accounts', Icons.account_balance_outlined, store.accountRecords.isEmpty ? 'Add yours' : Money.format(store.netWorth, whole: true), 'net worth',
          () => pushPage(context, const AccountsScreen())),
      _Feature('Bills', Icons.event_note_outlined, store.openBills.isEmpty ? 'None yet' : '${due.length} due', due.isEmpty ? 'this week' : 'in 7 days',
          () => pushPage(context, const BillsScreen()),
          badge: due.any((b) => Bills.status(b, today) == BillStatus.overdue)),
      _Feature('Income', Icons.payments_outlined, store.incomeSources.isEmpty ? 'Add paycheck' : Money.format(store.expectedMonthlyIncome(), whole: true), 'a month',
          () => pushPage(context, const IncomeScreen()),
          badge: asking.isNotEmpty),
      _Feature('Debts', Icons.credit_card_rounded, store.debts.isEmpty ? 'None' : Money.format(store.debtTotalCents, whole: true), activeDebts == 0 ? '' : '$activeDebts to pay off',
          () => pushPage(context, const DebtsScreen())),
      _Feature('Goals', Icons.flag_outlined, goals.isEmpty ? 'Set one' : '${goals.length} goal${goals.length == 1 ? '' : 's'}',
          goals.isEmpty ? '' : '${(goals.fold<double>(0, (s, g) => s + store.fraction(g)) / goals.length * 100).round()}% on average', () => pushPage(context, const GoalsScreen())),
      _Feature('Reports', Icons.insights_rounded, 'Trends', 'and year in review', () => pushPage(context, const ReportsScreen())),
      _Feature('Taxes', Icons.folder_copy_outlined, '${store.taxDocs.length} document${store.taxDocs.length == 1 ? '' : 's'}', 'by tax year', () => pushPage(context, const TaxesScreen())),
      _Feature('Import', Icons.file_download_outlined, 'Statements', 'CSV, OFX, PayPal…', () => pushPage(context, const ImportScreen())),
    ];

    return SafeArea(
      bottom: false,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 18, 20, 40),
        children: [
          ScreenHeader(
            title: 'More',
            subtitle: 'Everything else WillowWallet does.',
            trailing: IconButton.filled(
              tooltip: 'Settings',
              onPressed: () => pushPage(context, const SettingsScreen()),
              style: IconButton.styleFrom(backgroundColor: p.accentSoft, foregroundColor: p.accent, fixedSize: const Size(46, 46)),
              icon: const Icon(Icons.settings_outlined),
            ),
          ),
          if (updater.available != null && !updater.dismissed) ...[
            UpdateBanner(onOpen: () => pushPage(context, const SettingsScreen())),
            const SizedBox(height: 12),
          ],
          GridView.count(
            crossAxisCount: 2,
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            mainAxisSpacing: 10,
            crossAxisSpacing: 10,
            childAspectRatio: 1.32,
            padding: EdgeInsets.zero,
            children: tiles,
          ),
          const SizedBox(height: 14),
          ListPanel(
            children: [
              RowTile(
                leading: IconBox(syncLook(sync.status, p).$1, size: 40),
                title: sync.isSetUp ? 'Sync & devices' : 'Sync with your PC',
                subtitle: sync.isSetUp ? '${sync.statusText} · ${sync.serverName}' : 'Connect to your WillowWallet Server',
                onTap: () => SyncScreen.open(context),
              ),
              RowTile(
                leading: const IconBox(Icons.settings_outlined, size: 40),
                title: 'Settings',
                subtitle: 'Appearance, categories, budget month, lock, backup',
                onTap: () => pushPage(context, const SettingsScreen()),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _Feature extends StatelessWidget {
  const _Feature(this.title, this.icon, this.value, this.note, this.onTap, {this.badge = false});
  final String title, value, note;
  final IconData icon;
  final VoidCallback onTap;
  final bool badge;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Panel(
      onTap: onTap,
      padding: const EdgeInsets.fromLTRB(14, 14, 14, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              IconBox(icon, size: 38),
              const Spacer(),
              if (badge) Container(width: 10, height: 10, decoration: BoxDecoration(color: p.danger, shape: BoxShape.circle)),
            ],
          ),
          const Spacer(),
          Text(title, style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700)),
          const SizedBox(height: 2),
          Text(note.isEmpty ? value : '$value $note', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 13, color: p.muted)),
        ],
      ),
    );
  }
}

/// "Version 0.4.0 is available".
class UpdateBanner extends StatelessWidget {
  const UpdateBanner({super.key, required this.onOpen});
  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context) {
    final updater = UpdaterScope.of(context);
    final p = context.pal;
    final u = updater.available!;
    return Panel(
      color: p.accentSoft,
      onTap: onOpen,
      padding: const EdgeInsets.fromLTRB(14, 12, 6, 12),
      child: Row(
        children: [
          Icon(Icons.system_update_rounded, color: p.accent),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('WillowWallet ${u.version} is available', style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
                Text('Tap to download and install it.', style: TextStyle(fontSize: 13, color: p.muted)),
              ],
            ),
          ),
          IconButton(tooltip: 'Not now', onPressed: updater.dismiss, icon: Icon(Icons.close_rounded, color: p.muted)),
        ],
      ),
    );
  }
}
