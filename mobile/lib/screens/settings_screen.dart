import 'package:flutter/material.dart';

import '../money.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

const appVersion = '0.1.0';

class SettingsScreen extends StatelessWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final s = store.settings;
    const modes = ['system', 'light', 'dark'];

    return SafeArea(
      bottom: false,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 18, 20, 40),
        children: [
          const ScreenHeader(title: 'Settings', subtitle: 'Make CoinPilot yours.'),
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
              childAspectRatio: 0.92,
              padding: EdgeInsets.zero,
              children: [
                for (final t in colorThemes)
                  _Swatch(
                    name: t.name,
                    color: Palette.swatch(t),
                    selected: t.name == s.colorTheme,
                    onTap: () => store.updateSettings((x) => x.colorTheme = t.name),
                  ),
              ],
            ),
          ),
          const _Heading('Money'),
          Panel(
            padding: EdgeInsets.zero,
            child: _Tile(
              icon: Icons.payments_outlined,
              title: 'Currency',
              subtitle: '${Money.currency.name} (${Money.currency.symbol})',
              onTap: () => _pickCurrency(context, store),
            ),
          ),
          const _Heading('Your data'),
          Panel(
            padding: EdgeInsets.zero,
            child: Column(
              children: [
                const _Tile(
                  icon: Icons.lock_outline_rounded,
                  title: 'Saved on this phone',
                  subtitle: 'Private to CoinPilot. Sync with CoinPilot for Windows is coming soon.',
                ),
                const Divider(indent: 64),
                _Tile(
                  icon: Icons.auto_awesome_outlined,
                  title: 'Add sample data',
                  subtitle: 'Three months of example spending to try things out',
                  onTap: () {
                    store.loadSample();
                    toast(context, 'Sample data added');
                  },
                ),
                const Divider(indent: 64),
                _Tile(
                  icon: Icons.delete_outline_rounded,
                  title: 'Erase everything',
                  subtitle: '${store.transactions.length} transactions and ${store.budgets.length} budgets',
                  danger: true,
                  onTap: () async {
                    final ok = await confirm(context,
                        title: 'Erase everything?',
                        message: 'This deletes all transactions and budgets on this phone. It can\'t be undone.',
                        confirmText: 'Erase',
                        danger: true);
                    if (ok) store.eraseAll();
                  },
                ),
              ],
            ),
          ),
          const SizedBox(height: 28),
          Column(
            children: [
              const CoinLogo(size: 44),
              const SizedBox(height: 8),
              Text('CoinPilot for Android', style: display(17, p.text)),
              const SizedBox(height: 2),
              Text('Version $appVersion · Bytehawk Studio', style: TextStyle(fontSize: 13, color: p.muted)),
            ],
          ),
        ],
      ),
    );
  }

  void _pickCurrency(BuildContext context, Store store) {
    showModalBottomSheet<void>(
      context: context,
      useSafeArea: true,
      isScrollControlled: true,
      builder: (context) {
        final p = context.pal;
        return SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(22, 0, 22, 24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('Currency', style: display(24, p.text)),
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
          ),
        );
      },
    );
  }
}

class _Heading extends StatelessWidget {
  const _Heading(this.text);
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: 22, bottom: 10, left: 4),
        child: Text(text.toUpperCase(),
            style: TextStyle(fontSize: 12, letterSpacing: 1.1, fontWeight: FontWeight.w700, color: context.pal.muted)),
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
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                border: Border.all(color: selected ? color : Colors.transparent, width: 2.5),
              ),
              child: Container(
                decoration: BoxDecoration(color: color, shape: BoxShape.circle),
                child: selected ? const Icon(Icons.check_rounded, color: Colors.white, size: 22) : null,
              ),
            ),
            const SizedBox(height: 6),
            Text(name,
                style: TextStyle(
                    fontSize: 12.5, fontWeight: selected ? FontWeight.w700 : FontWeight.w500, color: selected ? p.text : p.muted)),
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
        padding: const EdgeInsets.fromLTRB(14, 14, 14, 14),
        child: Row(
          children: [
            Container(
              width: 38,
              height: 38,
              decoration: BoxDecoration(
                color: danger ? p.danger.withValues(alpha: 0.12) : p.accentSoft,
                borderRadius: BorderRadius.circular(11),
              ),
              child: Icon(icon, size: 20, color: c),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title,
                      style: TextStyle(fontSize: 15, fontWeight: FontWeight.w600, color: danger ? p.danger : p.text)),
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
