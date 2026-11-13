import 'package:flutter/material.dart';

import 'categories.dart';
import 'dates.dart';
import 'models.dart';
import 'money.dart';
import 'theme.dart';

/// The CoinPilot coin.
class CoinLogo extends StatelessWidget {
  const CoinLogo({super.key, this.size = 30});
  final double size;

  @override
  Widget build(BuildContext context) => Image.asset('assets/coinpilot.png',
      width: size, height: size, filterQuality: FilterQuality.medium, semanticLabel: 'CoinPilot');
}

/// A white (or dark) rounded card with a hairline border.
class Panel extends StatelessWidget {
  const Panel({super.key, required this.child, this.padding = const EdgeInsets.all(16), this.onTap, this.color});
  final Widget child;
  final EdgeInsetsGeometry padding;
  final VoidCallback? onTap;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final radius = BorderRadius.circular(18);
    return Material(
      color: color ?? p.surface,
      shape: RoundedRectangleBorder(borderRadius: radius, side: BorderSide(color: p.border)),
      clipBehavior: Clip.antiAlias,
      child: InkWell(onTap: onTap, child: Padding(padding: padding, child: child)),
    );
  }
}

/// Big screen title with an optional line under it and something on the right.
class ScreenHeader extends StatelessWidget {
  const ScreenHeader({super.key, required this.title, this.subtitle, this.trailing});
  final String title;
  final String? subtitle;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Padding(
      padding: const EdgeInsets.only(bottom: 18),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: display(30, p.text, weight: FontWeight.w800)),
                if (subtitle != null) ...[
                  const SizedBox(height: 4),
                  Text(subtitle!, style: TextStyle(fontSize: 14.5, color: p.muted)),
                ],
              ],
            ),
          ),
          if (trailing != null) trailing!,
        ],
      ),
    );
  }
}

/// "Budgets ······ See all"
class SectionTitle extends StatelessWidget {
  const SectionTitle(this.title, {super.key, this.action, this.onAction});
  final String title;
  final String? action;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Padding(
      padding: const EdgeInsets.only(top: 22, bottom: 10),
      child: Row(
        children: [
          Expanded(child: Text(title, style: display(19, p.text))),
          if (action != null)
            GestureDetector(
              onTap: onAction,
              behavior: HitTestBehavior.opaque,
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 4, horizontal: 2),
                child: Text(action!, style: TextStyle(fontSize: 14.5, fontWeight: FontWeight.w600, color: p.accent)),
              ),
            ),
        ],
      ),
    );
  }
}

/// A rounded progress bar (amber near the limit, red when over).
class ProgressBar extends StatelessWidget {
  const ProgressBar({super.key, required this.value, this.color, this.track, this.height = 8});
  final double value;
  final Color? color;
  final Color? track;
  final double height;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return ClipRRect(
      borderRadius: BorderRadius.circular(height),
      child: SizedBox(
        height: height,
        child: Stack(
          children: [
            Positioned.fill(child: ColoredBox(color: track ?? p.track)),
            FractionallySizedBox(
              alignment: Alignment.centerLeft,
              widthFactor: value.clamp(0.0, 1.0),
              heightFactor: 1,
              child: DecoratedBox(
                decoration: BoxDecoration(color: color ?? p.accent, borderRadius: BorderRadius.circular(height)),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

Color budgetColor(Palette p, int spent, int limit) {
  if (limit <= 0) return p.accent;
  final f = spent / limit;
  if (f > 1) return p.danger;
  if (f >= 0.85) return p.warn;
  return p.accent;
}

/// A category's icon in a soft rounded square.
class CategoryAvatar extends StatelessWidget {
  const CategoryAvatar(this.category, {super.key, this.size = 42, this.income = false});
  final String category;
  final double size;
  final bool income;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: income ? p.accentSoft : p.surfaceAlt,
        borderRadius: BorderRadius.circular(size * 0.3),
      ),
      child: Icon(categoryIcon(category), size: size * 0.5, color: income ? p.positive : p.accent),
    );
  }
}

/// One transaction row.
class TxnTile extends StatelessWidget {
  const TxnTile(this.t, {super.key, this.onTap, this.showDate = false});
  final Txn t;
  final VoidCallback? onTap;
  final bool showDate;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final sub = [t.category, if (showDate) dayLabel(t.date) else t.account].join(' · ');
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(14),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 10),
        child: Row(
          children: [
            CategoryAvatar(t.category, income: t.isIncome),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(t.payee, maxLines: 1, overflow: TextOverflow.ellipsis,
                      style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w600)),
                  const SizedBox(height: 2),
                  Text(sub, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 13, color: p.muted)),
                ],
              ),
            ),
            const SizedBox(width: 10),
            Text(
              Money.format(t.cents, signed: true),
              maxLines: 1,
              style: TextStyle(
                fontSize: 15.5,
                fontWeight: FontWeight.w600,
                color: t.isIncome ? p.positive : p.text,
                fontFeatures: const [FontFeature.tabularFigures()],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// ‹ November 2026 ›
class MonthSwitcher extends StatelessWidget {
  const MonthSwitcher({super.key, required this.month, required this.onChanged});
  final DateTime month;
  final ValueChanged<DateTime> onChanged;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final isCurrent = sameMonth(month, DateTime.now());
    Widget arrow(IconData icon, String label, int step, bool enabled) => IconButton(
          tooltip: label,
          onPressed: enabled ? () => onChanged(addMonths(month, step)) : null,
          icon: Icon(icon, size: 22),
          color: p.text,
          disabledColor: p.border,
          visualDensity: VisualDensity.compact,
        );
    return Container(
      decoration: BoxDecoration(
        color: p.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: p.border),
      ),
      padding: const EdgeInsets.symmetric(horizontal: 4),
      child: Row(
        children: [
          arrow(Icons.chevron_left_rounded, 'Previous month', -1, true),
          Expanded(
            child: Text(isCurrent ? 'This month · ${shortMonths[month.month - 1]}' : monthLabel(month),
                textAlign: TextAlign.center, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
          ),
          arrow(Icons.chevron_right_rounded, 'Next month', 1, !isCurrent),
        ],
      ),
    );
  }
}

/// A two- or three-way switch (Spending / Income, Auto / Light / Dark).
class PillToggle extends StatelessWidget {
  const PillToggle({super.key, required this.options, required this.selected, required this.onChanged});
  final List<String> options;
  final int selected;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Container(
      padding: const EdgeInsets.all(4),
      decoration: BoxDecoration(color: p.surfaceAlt, borderRadius: BorderRadius.circular(14)),
      child: Row(
        children: [
          for (var i = 0; i < options.length; i++)
            Expanded(
              child: GestureDetector(
                onTap: () => onChanged(i),
                behavior: HitTestBehavior.opaque,
                child: AnimatedContainer(
                  duration: const Duration(milliseconds: 180),
                  curve: Curves.easeOut,
                  padding: const EdgeInsets.symmetric(vertical: 10),
                  decoration: BoxDecoration(
                    color: i == selected ? p.surface : Colors.transparent,
                    borderRadius: BorderRadius.circular(11),
                    boxShadow: i == selected && !p.dark
                        ? const [BoxShadow(color: Color(0x14000000), blurRadius: 6, offset: Offset(0, 2))]
                        : null,
                  ),
                  child: Text(
                    options[i],
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      fontSize: 14.5,
                      fontWeight: FontWeight.w600,
                      color: i == selected ? p.text : p.muted,
                    ),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}

/// A selectable pill with an optional icon (categories, accounts).
class ChoicePill extends StatelessWidget {
  const ChoicePill({super.key, required this.label, required this.selected, required this.onTap, this.icon});
  final String label;
  final bool selected;
  final VoidCallback onTap;
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Semantics(
      selected: selected,
      button: true,
      child: GestureDetector(
        onTap: onTap,
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 160),
          padding: EdgeInsets.fromLTRB(icon == null ? 14 : 10, 9, 14, 9),
          decoration: BoxDecoration(
            color: selected ? p.accent : p.surface,
            borderRadius: BorderRadius.circular(99),
            border: Border.all(color: selected ? p.accent : p.border),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (icon != null) ...[
                Icon(icon, size: 17, color: selected ? p.onAccent : p.accent),
                const SizedBox(width: 6),
              ],
              Text(label,
                  style: TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: selected ? p.onAccent : p.text)),
            ],
          ),
        ),
      ),
    );
  }
}

/// A small label above a field.
class FieldLabel extends StatelessWidget {
  const FieldLabel(this.text, {super.key});
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: 20, bottom: 8),
        child: Text(text, style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w600, color: context.pal.muted)),
      );
}

/// A friendly empty state.
class EmptyState extends StatelessWidget {
  const EmptyState({super.key, required this.icon, required this.title, required this.text, this.actions = const []});
  final IconData icon;
  final String title;
  final String text;
  final List<Widget> actions;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Panel(
      padding: const EdgeInsets.fromLTRB(22, 26, 22, 22),
      child: Column(
        children: [
          Container(
            width: 60,
            height: 60,
            decoration: BoxDecoration(color: p.accentSoft, borderRadius: BorderRadius.circular(18)),
            child: Icon(icon, color: p.accent, size: 30),
          ),
          const SizedBox(height: 14),
          Text(title, textAlign: TextAlign.center, style: display(20, p.text)),
          const SizedBox(height: 6),
          Text(text, textAlign: TextAlign.center, style: TextStyle(fontSize: 14.5, height: 1.4, color: p.muted)),
          if (actions.isNotEmpty) ...[
            const SizedBox(height: 18),
            for (final a in actions) Padding(padding: const EdgeInsets.only(top: 8), child: a),
          ],
        ],
      ),
    );
  }
}

/// Shows a short message with an optional action at the bottom of the screen.
void toast(BuildContext context, String message, {String? action, VoidCallback? onAction}) {
  final m = ScaffoldMessenger.of(context);
  m.hideCurrentSnackBar();
  m.showSnackBar(SnackBar(
    content: Text(message),
    action: action == null ? null : SnackBarAction(label: action, onPressed: onAction ?? () {}),
    duration: const Duration(seconds: 4),
  ));
}

/// A themed yes/no question; true when confirmed.
Future<bool> confirm(BuildContext context,
    {required String title, required String message, required String confirmText, bool danger = false}) async {
  final p = context.pal;
  final ok = await showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      icon: Container(
        width: 52,
        height: 52,
        decoration: BoxDecoration(
          color: danger ? p.danger.withValues(alpha: 0.12) : p.accentSoft,
          borderRadius: BorderRadius.circular(16),
        ),
        child: Icon(danger ? Icons.warning_amber_rounded : Icons.help_outline_rounded,
            color: danger ? p.danger : p.accent, size: 28),
      ),
      title: Text(title, textAlign: TextAlign.center),
      content: Text(message, textAlign: TextAlign.center),
      actionsPadding: const EdgeInsets.fromLTRB(20, 4, 20, 20),
      actions: [
        Row(
          children: [
            Expanded(
              child: OutlinedButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: FilledButton(
                style: danger
                    ? FilledButton.styleFrom(
                        backgroundColor: p.danger, foregroundColor: Colors.white, minimumSize: const Size(0, 46))
                    : FilledButton.styleFrom(minimumSize: const Size(0, 46)),
                onPressed: () => Navigator.pop(context, true),
                child: Text(confirmText),
              ),
            ),
          ],
        ),
      ],
    ),
  );
  return ok ?? false;
}
