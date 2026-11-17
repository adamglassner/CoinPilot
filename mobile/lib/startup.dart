import 'dart:io';

import 'form.dart';

/// Mac and Linux: open WillowWallet when you sign in (so bill reminders arrive on time), like "Run in the background"
/// in WillowWallet for Windows.
class StartAtLogin {
  static String get _home => Platform.environment['HOME'] ?? '';

  /// The program to start: the AppImage when running from one (its path stays the same after updates), else this app.
  static String get _program => Platform.environment['APPIMAGE'] ?? Platform.resolvedExecutable;

  static File get _linuxFile => File('$_home/.config/autostart/willowwallet.desktop');
  static File get _macFile => File('$_home/Library/LaunchAgents/com.bytehawkstudio.willowwallet.plist');

  static bool get isOn => switch (hostOs) {
        HostOs.linux => _linuxFile.existsSync(),
        HostOs.macos => _macFile.existsSync(),
        _ => false,
      };

  /// Turns it on or off. Returns null, or why it couldn't.
  static Future<String?> set(bool on) async {
    try {
      final f = hostOs == HostOs.macos ? _macFile : _linuxFile;
      if (!on) {
        if (await f.exists()) await f.delete();
        return null;
      }
      await f.parent.create(recursive: true);
      if (hostOs == HostOs.linux) {
        await f.writeAsString('[Desktop Entry]\n'
            'Type=Application\n'
            'Name=WillowWallet\n'
            'Comment=Bill reminders from WillowWallet\n'
            'Exec="$_program"\n'
            'X-GNOME-Autostart-enabled=true\n');
      } else {
        await f.writeAsString('<?xml version="1.0" encoding="UTF-8"?>\n'
            '<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">\n'
            '<plist version="1.0"><dict>\n'
            '  <key>Label</key><string>com.bytehawkstudio.willowwallet</string>\n'
            '  <key>ProgramArguments</key><array><string>${_xml(_program)}</string></array>\n'
            '  <key>RunAtLoad</key><true/>\n'
            '</dict></plist>\n');
      }
      return null;
    } catch (e) {
      return 'Couldn\'t change it ($e).';
    }
  }

  static String _xml(String s) => s.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
}
