import 'package:flutter/material.dart';

import '../dates.dart';
import '../finance.dart';
import '../kit.dart';
import '../main.dart' show AppShellState;
import '../nav_bar.dart';
import '../money.dart';
import '../store.dart';
import '../sync/scope.dart';
import '../theme.dart';
import '../widgets.dart';
import 'import_screen.dart';
import 'settings_screen.dart';
import 'sync_screen.dart';

/// Every page that isn't in the bottom bar, plus import, sync and settings.
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

    final shell = AppShellState.of(context);
    void open(String key) => shell != null ? shell.openPage(key) : pushPage(context, barPage(key)!.build((_) {}));
    final inBar = {for (final t in barTabs(store.settings)) t.key};

    final all = <String, _Feature Function()>{
      'home': () => _Feature('Home', Icons.home_outlined, 'Overview', 'this month', () => open('home')),
      'transactions': () => _Feature('Transactions', Icons.receipt_long_outlined, 'Search and edit', 'all accounts', () => open('transactions')),
      'budgets': () => _Feature('Budgets', Icons.pie_chart_outline_rounded, store.budgets.isEmpty ? 'Set one up' : '${store.budgets.length} budgets', 'this month', () => open('budgets')),
      'forecast': () => _Feature('Forecast', Icons.timeline_rounded, 'Where you\'re headed', 'safe to spend', () => open('forecast')),
      'accounts': () => _Feature('Accounts', Icons.account_balance_outlined, store.accountRecords.isEmpty ? 'Add yours' : Money.format(store.netWorth, whole: true),
          store.accountRecords.isEmpty ? '' : 'net worth', () => open('accounts')),
      'bills': () => _Feature('Bills', Icons.event_note_outlined, store.openBills.isEmpty ? 'None yet' : (due.isEmpty ? 'Nothing due' : '${due.length} due'),
          store.openBills.isEmpty ? '' : 'next 7 days', () => open('bills'),
          badge: due.any((b) => Bills.status(b, today) == BillStatus.overdue)),
      'income': () => _Feature('Income', Icons.payments_outlined, store.incomeSources.isEmpty ? 'Add your paycheck' : '${Money.format(store.expectedMonthlyIncome(), whole: true)} a month', '',
          () => open('income'),
          badge: asking.isNotEmpty),
      'debts': () => _Feature('Debts', Icons.credit_card_rounded, store.debts.isEmpty ? 'None' : Money.format(store.debtTotalCents, whole: true), activeDebts == 0 ? '' : '$activeDebts to pay off',
          () => open('debts')),
      'goals': () => _Feature('Goals', Icons.flag_outlined, goals.isEmpty ? 'Set one' : '${goals.length} goal${goals.length == 1 ? '' : 's'}',
          goals.isEmpty ? '' : '${(goals.fold<double>(0, (s, g) => s + store.fraction(g)) / goals.length * 100).round()}% on average', () => open('goals')),
      'receipts': () => _Feature('Receipts', Icons.receipt_rounded, '${store.transactions.where((t) => t.receipt.isNotEmpty).length} kept', 'scan to add', () => open('receipts')),
      'reports': () => _Feature('Reports', Icons.insights_rounded, 'Trends', 'year in review', () => open('reports')),
      'taxes': () => _Feature('Taxes', Icons.folder_copy_outlined, '${store.taxDocs.length} document${store.taxDocs.length == 1 ? '' : 's'}', 'by tax year', () => open('taxes')),
    };
    final tiles = [
      for (final e in all.entries)
        if (!inBar.contains(e.key)) e.value(),
      _Feature('Import', Icons.file_download_outlined, 'Bank statements', 'CSV, OFX, PayPal', () => pushPage(context, const ImportScreen())),
    ];
    // Big phone text gets taller tiles, so nothing is cut off
    final scale = MediaQuery.textScalerOf(context).scale(1).clamp(1.0, 2.0);

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
            childAspectRatio: 1.25 / scale,
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
                leading: const IconBox(Icons.space_bar_rounded, size: 40),
                title: 'Bottom bar',
                subtitle: 'Choose the pages beside the + button',
                onTap: () => showBottomBarSheet(context),
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
          Text(title, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700)),
          const SizedBox(height: 2),
          Text(note.isEmpty ? value : '$value · $note', maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 13, color: p.muted, height: 1.25)),
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
