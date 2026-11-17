import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../form.dart';
import '../kit.dart';
import '../store.dart';
import '../theme.dart';
import '../version.dart';
import '../widgets.dart';

/// The changelog that ships inside the app: CHANGELOG.md for Android, CHANGELOG-desktop.md for Mac and Linux.
String get changelogAsset => isDesktop ? 'CHANGELOG-desktop.md' : 'CHANGELOG.md';

class Release {
  Release(this.title, this.lines);
  final String title;
  final List<String> lines;
}

/// "## 1.6.0 — 2026-11-17" sections, newest first, with their lines (wrapped lines joined to their bullet).
List<Release> parseChangelog(String markdown) {
  final result = <Release>[];
  for (final raw in markdown.replaceAll('\r', '').split('\n')) {
    if (raw.startsWith('## ')) {
      result.add(Release(raw.substring(3).replaceAll(' — ', ' · ').trim(), []));
      continue;
    }
    if (result.isEmpty || raw.trim().isEmpty) continue;
    final lines = result.last.lines;
    final bullet = raw.trimLeft().startsWith('- ');
    if (!bullet && lines.isNotEmpty && raw.startsWith(' ')) {
      lines[lines.length - 1] = '${lines.last} ${raw.trim()}';
    } else {
      lines.add(raw.trim());
    }
  }
  return result;
}

/// Opens What's new (a page on the phone, a dialog on a computer).
Future<void> showWhatsNew(BuildContext context) async {
  final text = await rootBundle.loadString(changelogAsset);
  if (!context.mounted) return;
  final releases = parseChangelog(text);
  if (isDesktop) {
    await showSheet<void>(context, SheetFrame(title: 'What\'s new', subtitle: 'WillowWallet $appVersion for $hostName. The newest changes first.', children: [_Body(releases)]));
  } else {
    await pushPage<void>(context, SubPage(title: 'What\'s new', subtitle: 'WillowWallet $appVersion for $hostName. The newest changes first.', children: [_Body(releases)]));
  }
}

/// Opens What's new once after an update (not on a first install).
Future<void> showWhatsNewAfterUpdate(BuildContext context) async {
  final store = StoreScope.read(context);
  final last = store.settings.lastSeenVersion;
  if (last == appVersion) return;
  final firstRun = last.isEmpty && store.isEmpty;
  store.updateSettings((s) => s.lastSeenVersion = appVersion);
  if (!firstRun) await showWhatsNew(context);
}

class _Body extends StatelessWidget {
  const _Body(this.releases);
  final List<Release> releases;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final r in releases.take(8)) ...[
          Padding(padding: const EdgeInsets.only(top: 20, bottom: 8), child: Text(r.title, style: display(19, p.text))),
          for (final line in r.lines) _line(context, line),
        ],
      ],
    );
  }

  Widget _line(BuildContext context, String line) {
    final p = context.pal;
    final bullet = line.startsWith('- ');
    var text = bullet ? line.substring(2) : line;
    text = text.replaceAllMapped(RegExp(r'\[([^\]]+)\]\([^)]+\)'), (m) => m[1]!).replaceAll('`', '');
    // **bold**
    final rich = <TextSpan>[];
    final re = RegExp(r'\*\*([^*]+)\*\*');
    var at = 0;
    for (final m in re.allMatches(text)) {
      if (m.start > at) rich.add(TextSpan(text: text.substring(at, m.start)));
      rich.add(TextSpan(text: m[1], style: const TextStyle(fontWeight: FontWeight.w700)));
      at = m.end;
    }
    if (at < text.length) rich.add(TextSpan(text: text.substring(at)));
    return Padding(
      padding: EdgeInsets.only(left: bullet ? 4 : 0, bottom: 7),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (bullet) Padding(padding: const EdgeInsets.only(top: 7, right: 10), child: Container(width: 5, height: 5, decoration: BoxDecoration(color: p.accent, shape: BoxShape.circle))),
          Expanded(child: Text.rich(TextSpan(children: rich), style: TextStyle(fontSize: 14.5, height: 1.45, color: p.text))),
        ],
      ),
    );
  }
}
