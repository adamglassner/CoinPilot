import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:path_provider/path_provider.dart';

import 'backup.dart';
import 'form.dart';
import 'store.dart';

/// Optional automatic backups, off until you turn them on: a backup .zip once a week, keeping the last few.
/// On a computer they go to a folder you choose (a NAS or USB drive works); on Android, to WillowWallet's own folder
/// (Android/data/…/files/Backups), which a USB cable or file manager can reach.
class AutoBackup {
  static Future<String?> chooseFolder() => FilePicker.platform.getDirectoryPath(dialogTitle: 'Where should WillowWallet keep backups?');

  static Future<Directory?> folder(Store store) async {
    if (isDesktop) return store.settings.backupFolder.isEmpty ? null : Directory(store.settings.backupFolder);
    final base = await getExternalStorageDirectory() ?? await getApplicationDocumentsDirectory();
    return Directory('${base.path}/Backups');
  }

  /// Makes a backup when one is due (or [force]). Returns the file, or null when none was made.
  static Future<File?> run(Store store, {bool force = false, DateTime? now}) async {
    final s = store.settings;
    if (!s.autoBackup && !force) return null;
    final today = now ?? DateTime.now();
    final last = DateTime.tryParse(s.lastAutoBackup);
    if (!force && last != null && today.difference(last).inDays < 7) return null;
    final dir = await folder(store);
    if (dir == null) return null;
    try {
      await dir.create(recursive: true);
      final file = File('${dir.path}/${Backup.suggestedName(today)}');
      await file.writeAsBytes(await Backup.create(store), flush: true);
      // Keep the newest few
      final old = dir.listSync().whereType<File>().where((f) => f.path.split(Platform.pathSeparator).last.startsWith('WillowWallet backup ') && f.path.endsWith('.zip')).toList()
        ..sort((a, b) => b.path.compareTo(a.path));
      for (final f in old.skip(s.backupKeep.clamp(1, 52))) {
        await f.delete();
      }
      store.updateSettings((x) => x.lastAutoBackup = today.toIso8601String());
      return file;
    } catch (_) {
      return null;
    }
  }
}
