import 'package:flutter/material.dart';

import 'dates.dart';
import 'money.dart';
import 'theme.dart';
import 'widgets.dart';

// Building blocks shared by the screens: pushed pages with a collapsing title, bottom sheets for editing,
// money and date fields, list rows and small stat tiles.

/// Opens a full-screen page (slides in, with a back arrow).
Future<T?> pushPage<T>(BuildContext context, Widget page) =>
    Navigator.of(context).push<T>(MaterialPageRoute(builder: (_) => page));

/// A pushed page: big title that shrinks as you scroll, optional actions and add button.
class SubPage extends StatelessWidget {
  const SubPage({
    super.key,
    required this.title,
    this.subtitle,
    this.actions = const [],
    this.fab,
    this.fabLabel,
    this.fabIcon = Icons.add_rounded,
    required this.children,
  });
  final String title;
  final String? subtitle;
  final List<Widget> actions;
  final VoidCallback? fab;
  final String? fabLabel;
  final IconData fabIcon;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Scaffold(
      backgroundColor: p.bg,
      floatingActionButton: fab == null
          ? null
          : FloatingActionButton.extended(
              onPressed: fab,
              backgroundColor: p.accent,
              foregroundColor: p.onAccent,
              elevation: 2,
              icon: Icon(fabIcon),
              label: Text(fabLabel ?? 'Add', style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 15)),
            ),
      body: CustomScrollView(
        slivers: [
          SliverAppBar.large(
            title: Text(title, style: display(22, p.text, weight: FontWeight.w800)),
            backgroundColor: p.bg,
            surfaceTintColor: Colors.transparent,
            actions: [...actions, const SizedBox(width: 6)],
          ),
          if (subtitle != null)
            SliverPadding(
              padding: const EdgeInsets.fromLTRB(20, 0, 20, 6),
              sliver: SliverToBoxAdapter(child: Text(subtitle!, style: TextStyle(fontSize: 14.5, color: p.muted, height: 1.35))),
            ),
          SliverPadding(
            padding: EdgeInsets.fromLTRB(20, 8, 20, fab == null ? 40 : 110),
            sliver: SliverList.list(children: children),
          ),
        ],
      ),
    );
  }
}

/// Opens a bottom sheet for adding or editing something.
Future<T?> showSheet<T>(BuildContext context, Widget sheet) => showModalBottomSheet<T>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (_) => sheet,
    );

/// The frame every edit sheet uses: title with close (and delete), the fields, an error and the main button.
class SheetFrame extends StatelessWidget {
  const SheetFrame({
    super.key,
    required this.title,
    this.subtitle,
    this.leading,
    this.onDelete,
    required this.children,
    this.error,
    this.primary,
    this.onPrimary,
    this.secondary,
  });
  final String title;
  final String? subtitle;
  final Widget? leading;
  final VoidCallback? onDelete;
  final List<Widget> children;
  final String? error;
  final String? primary;
  final VoidCallback? onPrimary;
  final Widget? secondary;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(22, 0, 22, 24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                if (leading != null) ...[leading!, const SizedBox(width: 12)],
                Expanded(child: Text(title, style: display(24, p.text))),
                if (onDelete != null)
                  IconButton(tooltip: 'Delete', onPressed: onDelete, icon: Icon(Icons.delete_outline_rounded, color: p.danger)),
                IconButton(tooltip: 'Close', onPressed: () => Navigator.pop(context), icon: Icon(Icons.close_rounded, color: p.muted)),
              ],
            ),
            if (subtitle != null) ...[
              const SizedBox(height: 4),
              Text(subtitle!, style: TextStyle(fontSize: 14.5, color: p.muted, height: 1.35)),
            ],
            ...children,
            if (error != null) ...[const SizedBox(height: 16), ErrorNote(error!)],
            if (primary != null) ...[
              const SizedBox(height: 22),
              FilledButton(onPressed: onPrimary, child: Text(primary!)),
            ],
            if (secondary != null) ...[const SizedBox(height: 8), secondary!],
          ],
        ),
      ),
    );
  }
}

/// A red note explaining what's missing.
class ErrorNote extends StatelessWidget {
  const ErrorNote(this.text, {super.key});
  final String text;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      decoration: BoxDecoration(
        color: p.danger.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: p.danger.withValues(alpha: 0.5)),
      ),
      child: Row(
        children: [
          Icon(Icons.error_outline_rounded, color: p.danger, size: 20),
          const SizedBox(width: 10),
          Expanded(child: Text(text, style: TextStyle(color: p.danger, fontWeight: FontWeight.w600))),
        ],
      ),
    );
  }
}

/// A soft hint with an icon.
class HintNote extends StatelessWidget {
  const HintNote(this.text, {super.key, this.icon = Icons.info_outline_rounded});
  final String text;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(color: p.surfaceAlt, borderRadius: BorderRadius.circular(14)),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 19, color: p.accent),
          const SizedBox(width: 10),
          Expanded(child: Text(text, style: TextStyle(fontSize: 13.5, color: p.muted, height: 1.4))),
        ],
      ),
    );
  }
}

/// A big money field with the currency symbol in front.
class MoneyField extends StatelessWidget {
  const MoneyField({super.key, required this.controller, this.autofocus = false, this.color, this.hint, this.onChanged, this.size = 30});
  final TextEditingController controller;
  final bool autofocus;
  final Color? color;
  final String? hint;
  final ValueChanged<String>? onChanged;
  final double size;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return TextField(
      controller: controller,
      autofocus: autofocus,
      onChanged: onChanged,
      keyboardType: const TextInputType.numberWithOptions(decimal: true, signed: false),
      textInputAction: TextInputAction.next,
      style: display(size, color ?? p.text),
      decoration: InputDecoration(
        hintText: hint ?? (Money.currency.decimals == 0 ? '0' : '0.00'),
        hintStyle: display(size, p.border),
        prefixIcon: Padding(
          padding: const EdgeInsets.only(left: 18, right: 6),
          child: Text(Money.currency.symbol, style: display(size, p.muted)),
        ),
        prefixIconConstraints: const BoxConstraints(minWidth: 0, minHeight: 0),
        contentPadding: EdgeInsets.symmetric(horizontal: 12, vertical: size > 24 ? 14 : 12),
      ),
    );
  }
}

/// A tappable date row that opens the date picker.
class DateTile extends StatelessWidget {
  const DateTile({super.key, required this.date, required this.onChanged, this.allowClear = false, this.emptyText = 'No date', this.first, this.last});
  final DateTime? date;
  final ValueChanged<DateTime?> onChanged;
  final bool allowClear;
  final String emptyText;
  final DateTime? first, last;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final d = date;
    return Panel(
      onTap: () async {
        final now = DateTime.now();
        final picked = await showDatePicker(
          context: context,
          initialDate: d ?? now,
          firstDate: first ?? DateTime(2000),
          lastDate: last ?? DateTime(now.year + 30, 12, 31),
        );
        if (picked != null) onChanged(picked);
      },
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 15),
      child: Row(
        children: [
          Icon(Icons.calendar_today_rounded, size: 19, color: p.accent),
          const SizedBox(width: 12),
          Expanded(
            child: Text(d == null ? emptyText : (dayLabel(d) == 'Today' ? 'Today, ${shortDate(d)}' : '${longDays[d.weekday - 1]}, ${shortDate(d)}'),
                style: TextStyle(fontSize: 15.5, fontWeight: FontWeight.w600, color: d == null ? p.muted : p.text)),
          ),
          if (allowClear && d != null)
            GestureDetector(onTap: () => onChanged(null), child: Icon(Icons.close_rounded, color: p.muted, size: 20))
          else
            Icon(Icons.chevron_right_rounded, color: p.muted),
        ],
      ),
    );
  }
}

/// Pills to pick one of a few options.
class Choices extends StatelessWidget {
  const Choices({super.key, required this.options, required this.selected, required this.onChanged, this.icons});
  final List<String> options;
  final String? selected;
  final ValueChanged<String> onChanged;
  final IconData Function(String)? icons;

  @override
  Widget build(BuildContext context) => Wrap(
        spacing: 8,
        runSpacing: 8,
        children: [
          for (final o in options)
            ChoicePill(label: o, icon: icons?.call(o), selected: o == selected, onTap: () => onChanged(o)),
        ],
      );
}

/// A rounded square with an icon.
class IconBox extends StatelessWidget {
  const IconBox(this.icon, {super.key, this.size = 42, this.color, this.background});
  final IconData icon;
  final double size;
  final Color? color;
  final Color? background;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(color: background ?? p.accentSoft, borderRadius: BorderRadius.circular(size * 0.3)),
      child: Icon(icon, size: size * 0.5, color: color ?? p.accent),
    );
  }
}

/// A list row: icon, title and subtitle, something on the right.
class RowTile extends StatelessWidget {
  const RowTile({super.key, this.leading, required this.title, this.subtitle, this.trailing, this.trailingSub, this.onTap, this.trailingColor, this.subtitleColor, this.trailingSubColor});
  final Widget? leading;
  final String title;
  final String? subtitle;
  final String? trailing;
  final String? trailingSub;
  final Color? trailingColor;
  final Color? subtitleColor;
  final Color? trailingSubColor;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(14),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 11),
        child: Row(
          children: [
            if (leading != null) ...[leading!, const SizedBox(width: 12)],
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w600)),
                  if (subtitle != null) ...[
                    const SizedBox(height: 2),
                    Text(subtitle!, maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 13, color: subtitleColor ?? p.muted, height: 1.3)),
                  ],
                ],
              ),
            ),
            if (trailing != null) ...[
              const SizedBox(width: 10),
              Column(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Text(trailing!,
                      style: TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700, color: trailingColor ?? p.text, fontFeatures: const [FontFeature.tabularFigures()])),
                  if (trailingSub != null) ...[
                    const SizedBox(height: 2),
                    Text(trailingSub!, style: TextStyle(fontSize: 12.5, color: trailingSubColor ?? p.muted, fontWeight: trailingSubColor == null ? FontWeight.w400 : FontWeight.w600)),
                  ],
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// A white panel holding rows separated by hairlines.
class ListPanel extends StatelessWidget {
  const ListPanel({super.key, required this.children, this.indent = 0});
  final List<Widget> children;
  final double indent;

  @override
  Widget build(BuildContext context) => Panel(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 3),
        child: Column(
          children: [
            for (final (i, c) in children.indexed) ...[if (i > 0) Divider(indent: indent), c],
          ],
        ),
      );
}

/// A small figure tile: label, big value, optional note.
class StatTile extends StatelessWidget {
  const StatTile({super.key, required this.label, required this.value, this.note, this.icon, this.valueColor, this.onTap});
  final String label;
  final String value;
  final String? note;
  final IconData? icon;
  final Color? valueColor;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Panel(
      onTap: onTap,
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              if (icon != null) ...[Icon(icon, size: 16, color: p.muted), const SizedBox(width: 6)],
              Flexible(child: Text(label, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 13, color: p.muted))),
            ],
          ),
          const SizedBox(height: 6),
          FittedBox(
            fit: BoxFit.scaleDown,
            alignment: Alignment.centerLeft,
            child: Text(value, style: display(21, valueColor ?? p.text)),
          ),
          if (note != null) ...[
            const SizedBox(height: 2),
            Text(note!, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 12.5, color: p.muted)),
          ],
        ],
      ),
    );
  }
}

/// The dark highlight card with a label, a big number and something under it.
class HeroCard extends StatelessWidget {
  const HeroCard({super.key, required this.label, required this.value, this.valueColor, this.footer});
  final String label;
  final String value;
  final Color? valueColor;
  final Widget? footer;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Container(
      padding: const EdgeInsets.fromLTRB(22, 20, 22, 20),
      decoration: BoxDecoration(color: p.hero, borderRadius: BorderRadius.circular(24)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: TextStyle(fontSize: 14.5, color: p.heroMuted)),
          const SizedBox(height: 6),
          FittedBox(
            fit: BoxFit.scaleDown,
            alignment: Alignment.centerLeft,
            child: Text(value, style: display(40, valueColor ?? p.heroText, weight: FontWeight.w800)),
          ),
          if (footer != null) ...[const SizedBox(height: 12), footer!],
        ],
      ),
    );
  }
}

/// A switch row with a title and a line under it.
class SwitchRow extends StatelessWidget {
  const SwitchRow({super.key, required this.title, this.subtitle, required this.value, required this.onChanged});
  final String title;
  final String? subtitle;
  final bool value;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return InkWell(
      onTap: () => onChanged(!value),
      borderRadius: BorderRadius.circular(14),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 8),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w600)),
                  if (subtitle != null) ...[
                    const SizedBox(height: 2),
                    Text(subtitle!, style: TextStyle(fontSize: 13, color: p.muted, height: 1.3)),
                  ],
                ],
              ),
            ),
            const SizedBox(width: 12),
            Switch(value: value, onChanged: onChanged),
          ],
        ),
      ),
    );
  }
}

/// A status pill ("Overdue", "Autopay").
class Tag extends StatelessWidget {
  const Tag(this.text, {super.key, this.color});
  final String text;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final c = color ?? p.accent;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 3),
      decoration: BoxDecoration(color: c.withValues(alpha: 0.13), borderRadius: BorderRadius.circular(99)),
      child: Text(text, style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700, color: c)),
    );
  }
}

/// A small text field with a label above it.
class LabeledField extends StatelessWidget {
  const LabeledField({super.key, required this.label, required this.controller, this.hint, this.keyboard, this.caps = TextCapitalization.sentences, this.autofocus = false, this.maxLines = 1, this.onChanged, this.obscure = false});
  final String label;
  final TextEditingController controller;
  final String? hint;
  final TextInputType? keyboard;
  final TextCapitalization caps;
  final bool autofocus;
  final int maxLines;
  final ValueChanged<String>? onChanged;
  final bool obscure;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          FieldLabel(label),
          TextField(
            controller: controller,
            autofocus: autofocus,
            keyboardType: keyboard,
            textCapitalization: caps,
            maxLines: obscure ? 1 : maxLines,
            minLines: 1,
            obscureText: obscure,
            onChanged: onChanged,
            decoration: InputDecoration(hintText: hint),
          ),
        ],
      );
}

/// Reads a money field: cents, or null when it isn't a number. Empty counts as 0 with [emptyIsZero].
int? readMoney(TextEditingController c, {bool emptyIsZero = false}) {
  if (c.text.trim().isEmpty) return emptyIsZero ? 0 : null;
  return Money.parse(c.text);
}
