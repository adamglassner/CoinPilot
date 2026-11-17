import 'dart:convert';
import 'dart:isolate';
import 'dart:math';

import 'package:cryptography/cryptography.dart';
import 'package:flutter/material.dart';

import 'form.dart';

import 'biometrics.dart';
import 'store.dart';
import 'theme.dart';
import 'widgets.dart';

/// The app lock: a PIN or password asked for when WillowWallet opens (and after time away).
/// Only a salted PBKDF2 hash of it is kept.
class AppLock extends ChangeNotifier {
  AppLock(this.store) {
    locked = store.settings.isLocked;
  }

  final Store store;
  bool locked = false;
  DateTime? _away;

  static const _iterations = 150000;

  static Future<String> hash(String secret, String salt) => Isolate.run(() async {
        final k = await Pbkdf2(macAlgorithm: Hmac.sha256(), iterations: _iterations, bits: 256)
            .deriveKey(secretKey: SecretKey(utf8.encode(secret)), nonce: base64.decode(salt));
        return base64.encode(await k.extractBytes());
      });

  /// A PIN is 4–12 digits; a password is at least 6 characters (the same rules as the PC).
  static String? validate(String secret, String kind) => kind == 'PIN'
      ? (secret.length >= 4 && secret.length <= 12 && RegExp(r'^\d+$').hasMatch(secret) ? null : 'A PIN is 4 to 12 digits.')
      : (secret.length >= 6 ? null : 'Use at least 6 characters for your password.');

  Future<void> turnOn(String secret, String kind) async {
    final rng = Random.secure();
    final salt = base64.encode(List.generate(16, (_) => rng.nextInt(256)));
    final h = await hash(secret, salt);
    store.updateSettings((s) => s
      ..lockKind = kind
      ..lockSalt = salt
      ..lockHash = h);
  }

  void turnOff() {
    store.updateSettings((s) => s
      ..lockKind = ''
      ..lockSalt = ''
      ..lockHash = ''
      ..biometricUnlock = false);
    locked = false;
    notifyListeners();
  }

  Future<bool> check(String secret) async {
    final s = store.settings;
    if (!s.isLocked) return true;
    return await hash(secret, s.lockSalt) == s.lockHash;
  }

  Future<bool> unlock(String secret) async {
    final ok = await check(secret);
    if (ok) {
      locked = false;
      notifyListeners();
    }
    return ok;
  }

  /// Unlocks with a fingerprint, face or Touch ID (when turned on).
  Future<bool> unlockWithBiometrics() async {
    if (!store.settings.biometricUnlock) return false;
    final ok = await Biometrics.verify('Unlock WillowWallet');
    if (ok) {
      locked = false;
      notifyListeners();
    }
    return ok;
  }

  void lockNow() {
    if (!store.settings.isLocked) return;
    locked = true;
    notifyListeners();
  }

  void wentAway() => _away = DateTime.now();

  void cameBack() {
    final away = _away;
    _away = null;
    if (away == null || !store.settings.isLocked) return;
    if (DateTime.now().difference(away).inSeconds >= store.settings.autoLockMinutes * 60) lockNow();
  }
}

class LockScope extends InheritedNotifier<AppLock> {
  const LockScope({super.key, required AppLock lock, required super.child}) : super(notifier: lock);
  static AppLock of(BuildContext context) => context.dependOnInheritedWidgetOfExactType<LockScope>()!.notifier!;
  static AppLock read(BuildContext context) => context.getInheritedWidgetOfExactType<LockScope>()!.notifier!;
}

/// Covers the app until the PIN or password is entered.
class LockScreen extends StatefulWidget {
  const LockScreen({super.key});

  @override
  State<LockScreen> createState() => _LockScreenState();
}

class _LockScreenState extends State<LockScreen> {
  final _text = TextEditingController();
  String _pin = '';
  String? _error;
  bool _busy = false;
  String? _bioKind;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      if (!mounted || !StoreScope.read(context).settings.biometricUnlock) return;
      final kind = await Biometrics.available();
      if (!mounted || kind == null) return;
      setState(() => _bioKind = kind);
      _bio();
    });
  }

  Future<void> _bio() async {
    if (_busy) return;
    final ok = await LockScope.read(context).unlockWithBiometrics();
    if (!ok && mounted) setState(() => _error = null);
  }

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  Future<void> _try(String secret) async {
    if (secret.isEmpty || _busy) return;
    setState(() => _busy = true);
    final ok = await LockScope.read(context).unlock(secret);
    if (!mounted) return;
    setState(() {
      _busy = false;
      if (!ok) {
        _error = 'That\'s not it. Try again.';
        _pin = '';
        _text.clear();
      }
    });
  }

  void _digit(String d) {
    if (_pin.length >= 12) return;
    setState(() {
      _pin += d;
      _error = null;
    });
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final store = StoreScope.of(context);
    final pin = store.settings.lockKind == 'PIN';
    return Scaffold(
      backgroundColor: p.bg,
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.symmetric(horizontal: 28, vertical: 20),
            child: Column(
              children: [
                const CoinLogo(size: 64),
                const SizedBox(height: 16),
                Text('WillowWallet is locked', style: display(24, p.text)),
                const SizedBox(height: 6),
                Text(pin ? 'Enter your PIN' : 'Enter your password', style: TextStyle(fontSize: 15, color: p.muted)),
                const SizedBox(height: 26),
                if (pin) ...[
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      for (var i = 0; i < (_pin.length < 4 ? 4 : _pin.length); i++)
                        Container(
                          width: 14,
                          height: 14,
                          margin: const EdgeInsets.symmetric(horizontal: 7),
                          decoration: BoxDecoration(shape: BoxShape.circle, color: i < _pin.length ? p.accent : p.border),
                        ),
                    ],
                  ),
                  const SizedBox(height: 14),
                  SizedBox(height: 22, child: Text(_error ?? '', style: TextStyle(color: p.danger, fontWeight: FontWeight.w600))),
                  const SizedBox(height: 10),
                  for (final row in const [
                    ['1', '2', '3'],
                    ['4', '5', '6'],
                    ['7', '8', '9'],
                    ['', '0', '<'],
                  ])
                    Padding(
                      padding: const EdgeInsets.only(bottom: 12),
                      child: Row(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          for (final k in row)
                            Padding(
                              padding: const EdgeInsets.symmetric(horizontal: 12),
                              child: k.isEmpty
                                  ? const SizedBox(width: 72, height: 72)
                                  : Material(
                                      color: k == '<' ? Colors.transparent : p.surface,
                                      shape: CircleBorder(side: BorderSide(color: k == '<' ? Colors.transparent : p.border)),
                                      child: InkWell(
                                        customBorder: const CircleBorder(),
                                        onTap: () => k == '<' ? setState(() => _pin = _pin.isEmpty ? '' : _pin.substring(0, _pin.length - 1)) : _digit(k),
                                        child: SizedBox(
                                          width: 72,
                                          height: 72,
                                          child: Center(
                                            child: k == '<'
                                                ? Icon(Icons.backspace_outlined, color: p.muted)
                                                : Text(k, style: display(26, p.text, weight: FontWeight.w600)),
                                          ),
                                        ),
                                      ),
                                    ),
                            ),
                        ],
                      ),
                    ),
                  const SizedBox(height: 8),
                  SizedBox(
                    width: 240,
                    child: FilledButton(
                      onPressed: _pin.length >= 4 && !_busy ? () => _try(_pin) : null,
                      child: _busy ? SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: p.onAccent)) : const Text('Unlock'),
                    ),
                  ),
                ] else ...[
                  TextField(
                    controller: _text,
                    obscureText: true,
                    autofocus: true,
                    onSubmitted: _try,
                    decoration: InputDecoration(hintText: 'Password', errorText: _error),
                  ),
                  const SizedBox(height: 16),
                  SizedBox(
                    width: double.infinity,
                    child: FilledButton(
                      onPressed: _busy ? null : () => _try(_text.text),
                      child: _busy ? SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: p.onAccent)) : const Text('Unlock'),
                    ),
                  ),
                ],
                if (_bioKind != null) ...[
                  const SizedBox(height: 12),
                  TextButton.icon(
                    onPressed: _bio,
                    icon: Icon(_bioKind == 'Face unlock' ? Icons.face_retouching_natural_rounded : Icons.fingerprint_rounded, size: 26),
                    label: Text('Use ${_bioKind == 'Touch ID' ? 'Touch ID' : _bioKind!.toLowerCase()}'),
                  ),
                ],
                const SizedBox(height: 18),
                Text('Forgot it? Uninstalling WillowWallet removes the lock and the data on this $deviceWord. If you sync, your data is safe on your server.',
                    textAlign: TextAlign.center, style: TextStyle(fontSize: 12.5, color: p.muted, height: 1.4)),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
