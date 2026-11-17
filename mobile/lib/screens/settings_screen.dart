import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../backup.dart';
import '../dates.dart';
import '../kit.dart';
import '../lock.dart';
import '../money.dart';
import '../platform.dart';
import '../store.dart';
import '../sync/scope.dart';
import '../theme.dart';
import '../updater.dart';
import '../version.dart';
import '../widgets.dart';
import 'categories_screen.dart';
import 'get_started.dart';
import 'sync_screen.dart';

export '../version.dart';

class SettingsScreen extends StatelessWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final sync = SyncScope.of(context);
    final lock = LockScope.of(context);
    final p = context.pal;
    final s = store.settings;
    const modes = ['system', 'light', 'dark'];
    final startDay = store.monthStartDay;

    return SubPage(
      title: 'Settings',
      subtitle: 'Make WillowWallet yours.',
      children: [
        const _Heading('Appearance'),
        PillToggle(
          options: const ['Auto', 'Light', 'Dark'],
          selected: modes.indexOf(s.themeMode).clamp(0, 2),
          onChanged: (i) => store.updateSettings((x) => x.themeMode = modes[i]),
        ),
        const SizedBox(height: 14),
        Panel(
          padding: const EdgeInsets.fromLTRB(12, 14, 12, 8),
          child: GridView.count(
            crossAxisCount: 4,
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            childAspectRatio: 0.82,
            padding: EdgeInsets.zero,
            children: [
              for (final t in colorThemes)
                _Swatch(name: t.name, color: Palette.swatch(t), selected: t.name == s.colorTheme, onTap: () => store.updateSettings((x) => x.colorTheme = t.name)),
            ],
          ),
        ),
        const _Heading('Money'),
        Panel(
          padding: EdgeInsets.zero,
          child: Column(
            children: [
              _Tile(icon: Icons.payments_outlined, title: 'Currency', subtitle: '${Money.currency.name} (${Money.currency.symbol})', onTap: () => _pickCurrency(context, store)),
              const Divider(indent: 64),
              _Tile(
                icon: Icons.calendar_month_outlined,
                title: 'Budget month',
                subtitle: startDay == 1 ? 'Calendar months (1st to the end of the month)' : 'Starts on the ${_ordinal(startDay)} (like your payday)',
                onTap: () => _pickStartDay(context, store),
              ),
              const Divider(indent: 64),
              _Tile(
                icon: Icons.category_outlined,
                title: 'Categories and rules',
                subtitle: 'Add subcategories, rename or hide categories, and set rules that pick a category from the payee',
                onTap: () => pushPage(context, const CategoriesScreen()),
              ),
            ],
          ),
        ),
        const _Heading('Privacy'),
        Panel(
          padding: EdgeInsets.zero,
          child: Column(
            children: [
              _Tile(
                icon: s.isLocked ? Icons.lock_rounded : Icons.lock_open_rounded,
                title: s.isLocked ? 'Locked with a ${s.lockKind == 'PIN' ? 'PIN' : 'password'}' : 'Add a PIN or password',
                subtitle: s.isLocked
                    ? 'Asked when WillowWallet opens${s.autoLockMinutes == 0 ? '' : ' and after ${s.autoLockMinutes} minute${s.autoLockMinutes == 1 ? '' : 's'} away'}'
                    : 'Keep WillowWallet private on this phone',
                onTap: () => showSheet<void>(context, _LockSheet(lock)),
              ),
            ],
          ),
        ),
        const _Heading('Your data'),
        Panel(
          padding: EdgeInsets.zero,
          child: Column(
            children: [
              _Tile(
                icon: syncLook(sync.status, p).$1,
                title: sync.isSetUp ? 'Sync · ${sync.statusText}' : 'Sync with your PC',
                subtitle: sync.isSetUp
                    ? '${sync.username} on ${sync.serverName}. Everything is kept on this phone too.'
                    : 'Saved on this phone. Connect to a WillowWallet Server to sync with WillowWallet for Windows.',
                onTap: () => SyncScreen.open(context),
              ),
              const Divider(indent: 64),
              _Tile(
                icon: Icons.backup_outlined,
                title: 'Back up',
                subtitle: 'Save everything to a .zip file. It restores here or in WillowWallet for Windows.',
                onTap: () => backUp(context),
              ),
              const Divider(indent: 64),
              _Tile(
                icon: Icons.settings_backup_restore_rounded,
                title: 'Restore a backup',
                subtitle: 'From this phone or from WillowWallet for Windows (Settings → Back up on the PC)',
                onTap: () => restoreBackup(context),
              ),
              const Divider(indent: 64),
              _Tile(icon: Icons.table_view_outlined, title: 'Export transactions', subtitle: 'Every transaction as a CSV spreadsheet', onTap: () => _exportCsv(context, store)),
              const Divider(indent: 64),
              _Tile(
                icon: Icons.flag_circle_outlined,
                title: 'Run Get started again',
                subtitle: 'The step-by-step setup: accounts, income, bills and debts',
                onTap: () => pushPage(context, const GetStartedScreen(again: true)),
              ),
              const Divider(indent: 64),
              _Tile(
                icon: Icons.auto_awesome_outlined,
                title: 'Add sample data',
                subtitle: 'Four months of example money to try things out',
                onTap: () {
                  store.loadSample();
                  toast(context, 'Sample data added');
                },
              ),
              const Divider(indent: 64),
              _Tile(
                icon: Icons.delete_outline_rounded,
                title: 'Delete all data',
                subtitle: '${store.transactions.length} transactions, ${store.budgets.length} budgets, ${store.accountRecords.length} accounts, ${store.bills.length} bills',
                danger: true,
                onTap: () async {
                  final ok = await confirm(context,
                      title: 'Delete all data?',
                      message: sync.isSetUp
                          ? 'This deletes every transaction, budget, account, bill, debt, goal and document on this phone and, because sync is on, on your other devices too. Back up first if you might want it. It can\'t be undone.'
                          : 'This deletes every transaction, budget, account, bill, debt, goal and document on this phone. Back up first if you might want it. It can\'t be undone.',
                      confirmText: 'Delete everything',
                      danger: true);
                  if (ok) {
                    store.eraseAll();
                    if (context.mounted) toast(context, 'All data deleted.');
                  }
                },
              ),
            ],
          ),
        ),
        const _Heading('Updates'),
        const UpdatePanel(),
        const SizedBox(height: 28),
        Column(
          children: [
            const CoinLogo(size: 44),
            const SizedBox(height: 8),
            Text('WillowWallet for Android', style: display(17, p.text)),
            const SizedBox(height: 2),
            Text('Version $appVersion · Bytehawk Studio', style: TextStyle(fontSize: 13, color: p.muted)),
          ],
        ),
      ],
    );
  }

  static String _ordinal(int n) => '$n${(n % 100 >= 11 && n % 100 <= 13) ? 'th' : switch (n % 10) { 1 => 'st', 2 => 'nd', 3 => 'rd', _ => 'th' }}';

  void _pickCurrency(BuildContext context, Store store) {
    showSheet<void>(
      context,
      Builder(builder: (context) {
        final p = context.pal;
        return SheetFrame(
          title: 'Currency',
          children: [
            const SizedBox(height: 12),
            for (final c in currencies)
              ListTile(
                contentPadding: const EdgeInsets.symmetric(horizontal: 4),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                leading: Container(
                  width: 40,
                  height: 40,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(color: p.surfaceAlt, borderRadius: BorderRadius.circular(12)),
                  child: Text(c.symbol, style: display(18, p.accent)),
                ),
                title: Text(c.name, style: const TextStyle(fontWeight: FontWeight.w600)),
                subtitle: Text(c.code, style: TextStyle(color: p.muted)),
                trailing: c.code == store.settings.currency ? Icon(Icons.check_circle_rounded, color: p.accent) : null,
                onTap: () {
                  store.updateSettings((x) => x.currency = c.code);
                  Navigator.pop(context);
                },
              ),
          ],
        );
      }),
    );
  }

  void _pickStartDay(BuildContext context, Store store) {
    showSheet<void>(
      context,
      Builder(builder: (context) {
        final p = context.pal;
        final current = store.monthStartDay;
        return SheetFrame(
          title: 'Budget month',
          subtitle: 'The day your budget month starts. If you budget paycheck to paycheck, use your payday. Synced with your PC.',
          children: [
            const SizedBox(height: 16),
            GridView.count(
              crossAxisCount: 7,
              shrinkWrap: true,
              physics: const NeverScrollableScrollPhysics(),
              mainAxisSpacing: 6,
              crossAxisSpacing: 6,
              children: [
                for (var d = 1; d <= 28; d++)
                  Material(
                    color: d == current ? p.accent : p.surfaceAlt,
                    borderRadius: BorderRadius.circular(12),
                    child: InkWell(
                      borderRadius: BorderRadius.circular(12),
                      onTap: () {
                        store.updatePrefs((x) => x.monthStartDay = d);
                        Navigator.pop(context);
                      },
                      child: Center(
                        child: Text('$d', style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, color: d == current ? p.onAccent : p.text)),
                      ),
                    ),
                  ),
              ],
            ),
          ],
        );
      }),
    );
  }

  Future<void> _exportCsv(BuildContext context, Store store) async {
    String q(String v) => v.contains(RegExp(r'[",\n]')) ? '"${v.replaceAll('"', '""')}"' : v;
    final rows = [
      'Date,Payee,Category,Account,Transfer to,Amount,Tags,Note',
      for (final t in [...store.transactions]..sort((a, b) => a.date.compareTo(b.date)))
        [
          '${t.date.year}-${t.date.month.toString().padLeft(2, '0')}-${t.date.day.toString().padLeft(2, '0')}',
          q(t.payee),
          q(t.isTransfer ? 'Transfer' : t.isSplit ? [for (final (c, cents) in t.splits) '$c ${(cents / 100).toStringAsFixed(2)}'].join(' + ') : t.category),
          q(t.account),
          q(t.transferAccount),
          (t.cents / 100).toStringAsFixed(2),
          q(t.tags.join(', ')),
          q(t.note),
        ].join(','),
    ];
    final now = DateTime.now();
    try {
      final saved = await Platform2.saveFile('WillowWallet transactions ${now.year}-${now.month.toString().padLeft(2, '0')}-${now.day.toString().padLeft(2, '0')}.csv',
          Uint8List.fromList(utf8.encode(rows.join('\n'))));
      if (saved && context.mounted) toast(context, 'Exported ${store.transactions.length} transactions.');
    } catch (e) {
      if (context.mounted) toast(context, 'Couldn\'t save the file: $e');
    }
  }
}

/// Saves a backup .zip where the person chooses (Downloads, Google Drive…).
Future<void> backUp(BuildContext context) async {
  final store = StoreScope.read(context);
  try {
    final zip = await Backup.create(store);
    final saved = await Platform2.saveFile(Backup.suggestedName(DateTime.now()), zip);
    if (saved && context.mounted) toast(context, 'Backup saved. Restore it here or in WillowWallet for Windows.');
  } catch (e) {
    if (context.mounted) toast(context, 'Couldn\'t save the backup: $e');
  }
}

/// Picks a backup (made here or on the PC) and replaces this phone's data with it.
Future<void> restoreBackup(BuildContext context) async {
  final store = StoreScope.read(context);
  final f = await Platform2.pickFile(extensions: const ['zip']);
  if (f == null || !context.mounted) return;
  RestoredBackup? backup;
  String? secret;
  while (backup == null) {
    try {
      backup = await Backup.read(f.bytes, secret: secret);
    } on BackupLocked catch (e) {
      if (!context.mounted) return;
      secret = await _askSecret(context, e);
      if (secret == null) return;
    } on FormatException catch (e) {
      if (context.mounted) toast(context, e.message);
      return;
    } catch (e) {
      if (context.mounted) toast(context, 'Couldn\'t read that backup: $e');
      return;
    }
  }
  if (!context.mounted) return;
  final sync = SyncScope.read(context);
  final ok = await confirm(context,
      title: 'Restore this backup?',
      message: '${backup.count('transactions')} transactions, ${backup.count('accounts')} accounts, ${backup.count('bills')} bills and ${backup.count('budgets')} budgets. '
          'It replaces everything on this phone${sync.isSetUp ? ' and, because sync is on, on your other devices' : ''}. Back up first if you might want what\'s here now.',
      confirmText: 'Restore');
  if (!ok || !context.mounted) return;
  await Backup.restore(store, backup);
  if (context.mounted) toast(context, 'Backup restored.');
}

Future<String?> _askSecret(BuildContext context, BackupLocked e) {
  final c = TextEditingController();
  return showDialog<String>(
    context: context,
    builder: (context) => AlertDialog(
      title: const Text('This backup is protected'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(e.wrong
              ? 'That didn\'t open it. Check the ${e.kind == 'PIN' ? 'PIN' : 'password'} and try again (or use the recovery key).'
              : 'It was made with a ${e.kind == 'PIN' ? 'PIN' : 'password'} on WillowWallet for Windows. Enter it (or the recovery key) to open it.'),
          const SizedBox(height: 14),
          TextField(controller: c, obscureText: true, autofocus: true, keyboardType: e.kind == 'PIN' ? TextInputType.number : TextInputType.visiblePassword, decoration: const InputDecoration(hintText: 'PIN, password or recovery key')),
        ],
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        FilledButton(onPressed: () => Navigator.pop(context, c.text), child: const Text('Open')),
      ],
    ),
  );
}

/// Updates from GitHub: the version here, and a newer one when there is.
class UpdatePanel extends StatelessWidget {
  const UpdatePanel({super.key});

  @override
  Widget build(BuildContext context) {
    final updater = UpdaterScope.of(context);
    final p = context.pal;
    final u = updater.available;
    return Panel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              IconBox(u != null ? Icons.system_update_rounded : Icons.verified_outlined),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(u != null ? 'Version ${u.version} is available' : 'WillowWallet $appVersion', style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700)),
                    Text(
                      updater.checking
                          ? 'Checking GitHub…'
                          : updater.problem ??
                              (u != null
                                  ? 'You have $appVersion.'
                                  : updater.lastChecked != null
                                      ? 'Up to date. Checked ${dayLabel(updater.lastChecked!).toLowerCase()}.'
                                      : 'Updates come from GitHub. WillowWallet checks once a day.'),
                      style: TextStyle(fontSize: 13, color: updater.problem != null ? p.danger : p.muted),
                    ),
                  ],
                ),
              ),
            ],
          ),
          if (updater.progress != null) ...[
            const SizedBox(height: 14),
            ProgressBar(value: updater.progress!),
            const SizedBox(height: 6),
            Text('Downloading… ${(updater.progress! * 100).round()}%', style: TextStyle(fontSize: 13, color: p.muted)),
          ] else ...[
            const SizedBox(height: 14),
            if (u != null)
              FilledButton.icon(
                onPressed: () async {
                  final error = await updater.install();
                  if (error != null && context.mounted) toast(context, error);
                },
                icon: const Icon(Icons.download_rounded),
                label: Text('Download and install ${u.version}'),
              )
            else
              OutlinedButton.icon(
                onPressed: updater.checking ? null : () => updater.check(force: true),
                icon: const Icon(Icons.refresh_rounded, size: 19),
                label: const Text('Check for updates'),
              ),
            if (u != null) ...[
              const SizedBox(height: 4),
              TextButton(onPressed: () => Platform2.openUrl(u.pageUrl), child: const Text('What\'s new')),
            ],
          ],
        ],
      ),
    );
  }
}

class UpdaterScope extends InheritedNotifier<Updater> {
  const UpdaterScope({super.key, required Updater updater, required super.child}) : super(notifier: updater);
  static Updater of(BuildContext context) => context.dependOnInheritedWidgetOfExactType<UpdaterScope>()!.notifier!;
  static Updater read(BuildContext context) => context.getInheritedWidgetOfExactType<UpdaterScope>()!.notifier!;
}

class _LockSheet extends StatefulWidget {
  const _LockSheet(this.lock);
  final AppLock lock;

  @override
  State<_LockSheet> createState() => _LockSheetState();
}

class _LockSheetState extends State<_LockSheet> {
  String _kind = 'PIN';
  final _a = TextEditingController();
  final _b = TextEditingController();
  final _current = TextEditingController();
  String? _error;
  bool _busy = false;

  @override
  void dispose() {
    _a.dispose();
    _b.dispose();
    _current.dispose();
    super.dispose();
  }

  Future<void> _turnOn() async {
    final problem = AppLock.validate(_a.text, _kind);
    if (problem != null) return setState(() => _error = problem);
    if (_a.text != _b.text) return setState(() => _error = 'The two don\'t match.');
    setState(() => _busy = true);
    await widget.lock.turnOn(_a.text, _kind);
    if (!mounted) return;
    Navigator.pop(context);
    toast(context, 'WillowWallet now asks for your ${_kind == 'PIN' ? 'PIN' : 'password'}.');
  }

  Future<void> _turnOff() async {
    setState(() => _busy = true);
    final ok = await widget.lock.check(_current.text);
    if (!mounted) return;
    if (!ok) return setState(() {
          _busy = false;
          _error = 'That\'s not it.';
        });
    widget.lock.turnOff();
    Navigator.pop(context);
    toast(context, 'The lock is off.');
  }

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final s = store.settings;
    if (s.isLocked) {
      return SheetFrame(
        title: 'App lock',
        subtitle: 'WillowWallet asks for your ${s.lockKind == 'PIN' ? 'PIN' : 'password'} when it opens.',
        error: _error,
        primary: _busy ? 'Checking…' : 'Turn off the lock',
        onPrimary: _busy ? null : _turnOff,
        children: [
          const FieldLabel('Lock again after'),
          Choices(
            options: const ['Every time', '1 minute', '5 minutes', '15 minutes'],
            selected: switch (s.autoLockMinutes) { 0 => 'Every time', 1 => '1 minute', 5 => '5 minutes', _ => '15 minutes' },
            onChanged: (v) => store.updateSettings((x) => x.autoLockMinutes = switch (v) { 'Every time' => 0, '1 minute' => 1, '5 minutes' => 5, _ => 15 }),
          ),
          LabeledField(label: 'To turn it off, enter your ${s.lockKind == 'PIN' ? 'PIN' : 'password'}', controller: _current, obscure: true, keyboard: s.lockKind == 'PIN' ? TextInputType.number : null),
        ],
      );
    }
    return SheetFrame(
      title: 'Add a PIN or password',
      subtitle: 'Asked when WillowWallet opens and after time away. If you forget it, uninstalling removes it (and the data on this phone), so sync or back up.',
      error: _error,
      primary: _busy ? 'Turning on…' : 'Turn on',
      onPrimary: _busy ? null : _turnOn,
      children: [
        const SizedBox(height: 14),
        PillToggle(options: const ['PIN', 'Password'], selected: _kind == 'PIN' ? 0 : 1, onChanged: (i) => setState(() => _kind = i == 0 ? 'PIN' : 'Password')),
        LabeledField(label: _kind == 'PIN' ? 'PIN (4 to 12 digits)' : 'Password (6 or more characters)', controller: _a, obscure: true, keyboard: _kind == 'PIN' ? TextInputType.number : null),
        LabeledField(label: 'Again', controller: _b, obscure: true, keyboard: _kind == 'PIN' ? TextInputType.number : null),
      ],
    );
  }
}

class _Heading extends StatelessWidget {
  const _Heading(this.text);
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: 22, bottom: 10, left: 4),
        child: Text(text.toUpperCase(), style: TextStyle(fontSize: 12, letterSpacing: 1.1, fontWeight: FontWeight.w700, color: context.pal.muted)),
      );
}

class _Swatch extends StatelessWidget {
  const _Swatch({required this.name, required this.color, required this.selected, required this.onTap});
  final String name;
  final Color color;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Semantics(
      label: '$name color theme',
      selected: selected,
      button: true,
      child: GestureDetector(
        onTap: onTap,
        behavior: HitTestBehavior.opaque,
        child: Column(
          children: [
            AnimatedContainer(
              duration: const Duration(milliseconds: 180),
              width: 48,
              height: 48,
              padding: const EdgeInsets.all(3),
              decoration: BoxDecoration(shape: BoxShape.circle, border: Border.all(color: selected ? color : Colors.transparent, width: 2.5)),
              child: Container(
                decoration: BoxDecoration(color: color, shape: BoxShape.circle),
                child: selected ? const Icon(Icons.check_rounded, color: Colors.white, size: 22) : null,
              ),
            ),
            const SizedBox(height: 6),
            Text(name,
                maxLines: 1,
                overflow: TextOverflow.fade,
                softWrap: false,
                style: TextStyle(fontSize: 12.5, fontWeight: selected ? FontWeight.w700 : FontWeight.w500, color: selected ? p.text : p.muted)),
          ],
        ),
      ),
    );
  }
}

class _Tile extends StatelessWidget {
  const _Tile({required this.icon, required this.title, this.subtitle, this.onTap, this.danger = false});
  final IconData icon;
  final String title;
  final String? subtitle;
  final VoidCallback? onTap;
  final bool danger;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final c = danger ? p.danger : p.accent;
    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Row(
          children: [
            Container(
              width: 38,
              height: 38,
              decoration: BoxDecoration(color: danger ? p.danger.withValues(alpha: 0.12) : p.accentSoft, borderRadius: BorderRadius.circular(11)),
              child: Icon(icon, size: 20, color: c),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: TextStyle(fontSize: 15, fontWeight: FontWeight.w600, color: danger ? p.danger : p.text)),
                  if (subtitle != null) ...[
                    const SizedBox(height: 2),
                    Text(subtitle!, style: TextStyle(fontSize: 13, height: 1.3, color: p.muted)),
                  ],
                ],
              ),
            ),
            if (onTap != null) Icon(Icons.chevron_right_rounded, color: p.muted),
          ],
        ),
      ),
    );
  }
}
