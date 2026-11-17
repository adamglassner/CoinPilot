import 'package:flutter/material.dart';

import '../theme.dart';
import 'engine.dart';

/// Makes the sync engine available to every screen.
class SyncScope extends InheritedNotifier<SyncEngine> {
  const SyncScope({super.key, required SyncEngine engine, required super.child}) : super(notifier: engine);

  static SyncEngine of(BuildContext context) => context.dependOnInheritedWidgetOfExactType<SyncScope>()!.notifier!;
  static SyncEngine read(BuildContext context) => context.getInheritedWidgetOfExactType<SyncScope>()!.notifier!;
}

/// Icon and color for a sync state.
(IconData, Color) syncLook(SyncStatus s, Palette p) => switch (s) {
      SyncStatus.off => (Icons.phone_android_rounded, p.accent),
      SyncStatus.idle => (Icons.cloud_done_outlined, p.accent),
      SyncStatus.syncing => (Icons.sync_rounded, p.accent),
      SyncStatus.offline => (Icons.cloud_off_outlined, p.muted),
      SyncStatus.paused => (Icons.pause_circle_outline_rounded, p.warn),
      SyncStatus.signedOut => (Icons.person_off_outlined, p.warn),
      SyncStatus.error => (Icons.error_outline_rounded, p.danger),
    };

/// "On this phone" / "Synced" pill for the top of the home screen.
class SyncPill extends StatelessWidget {
  const SyncPill({super.key, this.onTap, this.compact = false});
  final VoidCallback? onTap;

  /// Just the icon (for big phone text, where the words would push the app name off the screen).
  final bool compact;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final sync = SyncScope.of(context);
    final (icon, color) = syncLook(sync.status, p);
    final label = switch (sync.status) {
      SyncStatus.off => 'On this phone',
      SyncStatus.idle => 'Synced',
      SyncStatus.syncing => 'Syncing',
      SyncStatus.offline => 'Offline',
      SyncStatus.paused => 'Paused',
      SyncStatus.signedOut => 'Signed out',
      SyncStatus.error => 'Sync problem',
    };
    return Semantics(
      button: onTap != null,
      label: 'Sync: ${sync.statusText}',
      excludeSemantics: true,
      child: Material(
        color: p.surface,
        shape: StadiumBorder(side: BorderSide(color: p.border)),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Padding(
            padding: compact ? const EdgeInsets.all(9) : const EdgeInsets.fromLTRB(10, 6, 12, 6),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(icon, size: compact ? 18 : 15, color: color),
                if (!compact) ...[
                  const SizedBox(width: 5),
                  Text(label, maxLines: 1, style: TextStyle(fontSize: 12.5, fontWeight: FontWeight.w600, color: p.text)),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
