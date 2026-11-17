import 'dart:io';

import 'package:local_auth/local_auth.dart';

import 'form.dart';

/// Unlocking with a fingerprint or face: Android (fingerprint or face unlock), Mac (Touch ID) and Linux (fprintd).
class Biometrics {
  static final _auth = LocalAuthentication();

  /// What this device offers ("Fingerprint", "Face unlock", "Touch ID"), or null when there's nothing set up.
  static Future<String?> available() async {
    try {
      if (hostOs == HostOs.linux) return await _linuxEnrolled() ? 'Fingerprint' : null;
      if (!await _auth.isDeviceSupported() || !await _auth.canCheckBiometrics) return null;
      final types = await _auth.getAvailableBiometrics();
      if (types.isEmpty) return null;
      if (hostOs == HostOs.macos) return 'Touch ID';
      if (types.contains(BiometricType.face) && !types.contains(BiometricType.fingerprint)) return 'Face unlock';
      if (types.contains(BiometricType.fingerprint) && !types.contains(BiometricType.face)) return 'Fingerprint';
      return 'Fingerprint or face';
    } catch (_) {
      return null;
    }
  }

  /// Asks for a fingerprint or face. True when it's you.
  static Future<bool> verify(String reason) async {
    try {
      if (hostOs == HostOs.linux) return await _linuxVerify();
      return await _auth.authenticate(localizedReason: reason, biometricOnly: true, persistAcrossBackgrounding: true);
    } catch (_) {
      return false;
    }
  }

  // ----- Linux: fprintd (the fingerprint service GNOME and KDE use) -----

  static String get _user => Platform.environment['USER'] ?? '';

  static Future<bool> _linuxEnrolled() async {
    try {
      final r = await Process.run('fprintd-list', [_user]).timeout(const Duration(seconds: 5));
      return r.exitCode == 0 && RegExp(r'#\d+:').hasMatch('${r.stdout}');
    } catch (_) {
      return false;
    }
  }

  static Future<bool> _linuxVerify() async {
    try {
      final r = await Process.run('fprintd-verify', [_user]).timeout(const Duration(seconds: 40));
      return '${r.stdout}'.contains('verify-match');
    } catch (_) {
      return false;
    }
  }
}
