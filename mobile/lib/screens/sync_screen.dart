import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../store.dart';
import '../sync/api.dart';
import '../sync/engine.dart';
import '../sync/scope.dart';
import '../theme.dart';
import '../widgets.dart';

/// Settings → Sync: connect to a WillowWallet Server, or see how syncing is going.
class SyncScreen extends StatelessWidget {
  const SyncScreen({super.key});

  static Future<void> open(BuildContext context) =>
      Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => const SyncScreen()));

  @override
  Widget build(BuildContext context) {
    final sync = SyncScope.of(context);
    final p = context.pal;
    return Scaffold(
      backgroundColor: p.bg,
      appBar: AppBar(
        backgroundColor: p.bg,
        surfaceTintColor: Colors.transparent,
        title: Text('Sync', style: display(20, p.text)),
      ),
      body: SafeArea(
        top: false,
        child: AnimatedSwitcher(
          duration: const Duration(milliseconds: 220),
          child: sync.isConnected ? const _Connected(key: ValueKey('connected')) : const _Connect(key: ValueKey('connect')),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------------------------
// Not connected: sign in or create an account

class _Connect extends StatefulWidget {
  const _Connect({super.key});

  @override
  State<_Connect> createState() => _ConnectState();
}

class _ConnectState extends State<_Connect> {
  late final SyncEngine _sync = SyncScope.read(context);
  late final _url = TextEditingController(text: _sync.serverUrl);
  late final _user = TextEditingController(text: _sync.username);
  final _password = TextEditingController();
  final _invite = TextEditingController();
  bool _create = false;
  bool _hide = true;
  bool _busy = false;
  String? _error;
  ServerInfo? _server;
  String? _serverProblem;
  bool _checking = false;
  Timer? _checkSoon;

  @override
  void initState() {
    super.initState();
    if (_url.text.isNotEmpty) _check();
  }

  @override
  void dispose() {
    _checkSoon?.cancel();
    for (final c in [_url, _user, _password, _invite]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _check() async {
    final url = _url.text.trim();
    if (url.isEmpty) {
      setState(() { _server = null; _serverProblem = null; });
      return;
    }
    setState(() => _checking = true);
    try {
      final info = await _sync.checkServer(url);
      if (!mounted || url != _url.text.trim()) return;
      setState(() { _server = info; _serverProblem = null; _checking = false; });
    } on SyncException catch (e) {
      if (!mounted || url != _url.text.trim()) return;
      setState(() { _server = null; _serverProblem = e.message; _checking = false; });
    }
  }

  Future<void> _go() async {
    FocusScope.of(context).unfocus();
    setState(() { _busy = true; _error = null; });
    try {
      final recovery = await _sync.connect(_url.text, _user.text, _password.text,
          create: _create, invite: _invite.text.isEmpty ? null : _invite.text);
      if (!mounted) return;
      if (recovery != null) await showRecoveryKey(context, recovery);
      if (!mounted) return;
      await _firstSync(context, _sync);
    } on SyncException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final signedOut = _sync.status == SyncStatus.signedOut;
    final needsInvite = _create && _server != null && !_server!.openSignup;
    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 4, 20, 40),
      children: [
        if (signedOut) ...[
          _Banner(icon: Icons.person_off_outlined, color: p.warn, text: _sync.problem ?? 'Sign in again to keep syncing.'),
          const SizedBox(height: 14),
        ],
        _Intro(),
        const SizedBox(height: 18),
        Panel(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const FieldLabel('Server address'),
              TextField(
                controller: _url,
                keyboardType: TextInputType.url,
                autocorrect: false,
                textInputAction: TextInputAction.next,
                decoration: InputDecoration(
                  hintText: 'http://192.168.1.20:8484',
                  prefixIcon: const Icon(Icons.dns_outlined),
                  suffixIcon: _checking
                      ? const Padding(
                          padding: EdgeInsets.all(14),
                          child: SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2)))
                      : null,
                ),
                onChanged: (_) {
                  _checkSoon?.cancel();
                  _checkSoon = Timer(const Duration(milliseconds: 700), _check);
                },
                onSubmitted: (_) => _check(),
              ),
              const SizedBox(height: 8),
              _ServerLine(info: _server, problem: _serverProblem),
              const SizedBox(height: 16),
              PillToggle(
                options: const ['Sign in', 'Create account'],
                selected: _create ? 1 : 0,
                onChanged: (i) => setState(() { _create = i == 1; _error = null; }),
              ),
              const SizedBox(height: 14),
              const FieldLabel('Username'),
              TextField(
                controller: _user,
                autocorrect: false,
                textInputAction: TextInputAction.next,
                autofillHints: const [AutofillHints.username],
                decoration: const InputDecoration(prefixIcon: Icon(Icons.person_outline_rounded)),
              ),
              const SizedBox(height: 12),
              const FieldLabel('Password'),
              TextField(
                controller: _password,
                obscureText: _hide,
                autocorrect: false,
                enableSuggestions: false,
                textInputAction: needsInvite ? TextInputAction.next : TextInputAction.done,
                autofillHints: [_create ? AutofillHints.newPassword : AutofillHints.password],
                onSubmitted: (_) => needsInvite || _busy ? null : _go(),
                decoration: InputDecoration(
                  prefixIcon: const Icon(Icons.lock_outline_rounded),
                  helperText: _create ? 'At least 8 characters. It also unlocks your data, so pick a good one.' : null,
                  helperMaxLines: 2,
                  suffixIcon: IconButton(
                    tooltip: _hide ? 'Show password' : 'Hide password',
                    icon: Icon(_hide ? Icons.visibility_outlined : Icons.visibility_off_outlined),
                    onPressed: () => setState(() => _hide = !_hide),
                  ),
                ),
              ),
              if (needsInvite) ...[
                const SizedBox(height: 12),
                const FieldLabel('Invite code'),
                TextField(
                  controller: _invite,
                  autocorrect: false,
                  textCapitalization: TextCapitalization.characters,
                  decoration: const InputDecoration(
                    prefixIcon: Icon(Icons.confirmation_number_outlined),
                    helperText: 'Ask the person who runs the server for one.',
                  ),
                ),
              ],
              if (_error != null) ...[
                const SizedBox(height: 14),
                _Banner(icon: Icons.error_outline_rounded, color: p.danger, text: _error!),
              ],
              const SizedBox(height: 18),
              FilledButton(
                onPressed: _busy ? null : _go,
                style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(50)),
                child: _busy
                    ? Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                        SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2, color: p.onAccent)),
                        const SizedBox(width: 12),
                        Text(_create ? 'Creating your account…' : 'Signing in…'),
                      ])
                    : Text(_create ? 'Create account and sync' : 'Sign in and sync'),
              ),
              if (!_create) ...[
                const SizedBox(height: 6),
                TextButton(
                  onPressed: _busy
                      ? null
                      : () => showRecoverSheet(context, url: _url.text, username: _user.text).then((ok) {
                            if (ok == true && context.mounted) _firstSync(context, _sync);
                          }),
                  child: const Text('Forgot your password?'),
                ),
              ],
            ],
          ),
        ),
        const SizedBox(height: 18),
        const _HowItWorks(),
      ],
    );
  }
}

class _Intro extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Container(
      padding: const EdgeInsets.fromLTRB(18, 18, 18, 18),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(20),
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [p.hero, Color.lerp(p.hero, p.accent, 0.45)!],
        ),
      ),
      child: Row(
        children: [
          Container(
            width: 48,
            height: 48,
            decoration: BoxDecoration(color: Colors.white.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(14)),
            child: Icon(Icons.devices_rounded, color: p.mint, size: 26),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Your budget on every device', style: display(17, p.heroText)),
                const SizedBox(height: 4),
                Text('Sync with WillowWallet for Windows through your own WillowWallet Server. Optional: without it, everything stays on this phone.',
                    style: TextStyle(fontSize: 13, height: 1.35, color: p.heroMuted)),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _ServerLine extends StatelessWidget {
  const _ServerLine({this.info, this.problem});
  final ServerInfo? info;
  final String? problem;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final (icon, color, text) = info != null
        ? (
            Icons.check_circle_rounded,
            p.positive,
            '${info!.name} · ${info!.openSignup ? 'anyone can create an account' : 'new accounts need an invite'}'
          )
        : problem != null
            ? (Icons.error_outline_rounded, p.danger, problem!)
            : (Icons.info_outline_rounded, p.muted, 'The address is on the server\'s web page, under Connect a device.');
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(padding: const EdgeInsets.only(top: 1), child: Icon(icon, size: 16, color: color)),
        const SizedBox(width: 6),
        Expanded(child: Text(text, style: TextStyle(fontSize: 12.5, height: 1.3, color: info != null ? p.text : color))),
      ],
    );
  }
}

class _HowItWorks extends StatelessWidget {
  const _HowItWorks();

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    Widget point(IconData icon, String title, String text) => Padding(
          padding: const EdgeInsets.symmetric(vertical: 8),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: 34,
                height: 34,
                decoration: BoxDecoration(color: p.accentSoft, borderRadius: BorderRadius.circular(10)),
                child: Icon(icon, size: 18, color: p.accent),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(title, style: TextStyle(fontSize: 14.5, fontWeight: FontWeight.w600, color: p.text)),
                  const SizedBox(height: 2),
                  Text(text, style: TextStyle(fontSize: 13, height: 1.35, color: p.muted)),
                ]),
              ),
            ],
          ),
        );
    return Panel(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('How sync works', style: display(16, p.text)),
          const SizedBox(height: 4),
          point(Icons.lock_outline_rounded, 'Encrypted on this phone',
              'Your data is locked with your password before it leaves the phone. The server can\'t read it.'),
          point(Icons.phone_android_rounded, 'Still works offline',
              'Everything is kept on this phone too. Changes sync when the server is reachable again.'),
          point(Icons.receipt_long_outlined, 'Transactions and budgets',
              'Accounts and categories from the PC show up here. Receipts and tax documents stay on the PC.'),
        ],
      ),
    );
  }
}

class _Banner extends StatelessWidget {
  const _Banner({required this.icon, required this.color, required this.text});
  final IconData icon;
  final Color color;
  final String text;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
        decoration: BoxDecoration(color: color.withValues(alpha: 0.1), borderRadius: BorderRadius.circular(12)),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, size: 18, color: color),
            const SizedBox(width: 8),
            Expanded(child: Text(text, style: TextStyle(fontSize: 13, height: 1.35, color: context.pal.text))),
          ],
        ),
      );
}

/// After signing in: combine this phone's data with the server's, or use only the server's.
Future<void> _firstSync(BuildContext context, SyncEngine sync) async {
  var serverOnly = false;
  if (sync.hasLocalData) serverOnly = await showMergeChoice(context) == true;
  final ok = await sync.firstSync(useServerOnly: serverOnly);
  if (context.mounted) toast(context, ok ? 'Synced with ${sync.serverName}' : (sync.problem ?? 'Couldn\'t sync yet'));
}

/// Returns true for "use only the server's data", false for "combine", null if dismissed (combine).
Future<bool?> showMergeChoice(BuildContext context) {
  final store = StoreScope.read(context);
  return showModalBottomSheet<bool>(
    context: context,
    useSafeArea: true,
    isScrollControlled: true,
    isDismissible: false,
    enableDrag: false,
    builder: (context) {
      final p = context.pal;
      var serverOnly = false;
      return StatefulBuilder(builder: (context, setState) {
        Widget option(bool value, IconData icon, String title, String text) {
          final on = serverOnly == value;
          return Semantics(
            selected: on,
            button: true,
            child: GestureDetector(
              onTap: () => setState(() => serverOnly = value),
              child: AnimatedContainer(
                duration: const Duration(milliseconds: 160),
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(
                  color: on ? p.accentSoft : p.surface,
                  borderRadius: BorderRadius.circular(16),
                  border: Border.all(color: on ? p.accent : p.border, width: on ? 1.6 : 1),
                ),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Icon(icon, color: on ? p.accent : p.muted),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Text(title, style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, color: p.text)),
                        const SizedBox(height: 3),
                        Text(text, style: TextStyle(fontSize: 13, height: 1.35, color: p.muted)),
                      ]),
                    ),
                    const SizedBox(width: 8),
                    Icon(on ? Icons.radio_button_checked_rounded : Icons.radio_button_off_rounded,
                        color: on ? p.accent : p.muted, size: 22),
                  ],
                ),
              ),
            ),
          );
        }

        return Padding(
          padding: const EdgeInsets.fromLTRB(22, 4, 22, 22),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('This phone already has data', style: display(22, p.text)),
              const SizedBox(height: 6),
              Text(
                  '${store.transactions.length} transactions and ${store.budgets.length} budgets are saved here. What should happen to them?',
                  style: TextStyle(fontSize: 14, height: 1.4, color: p.muted)),
              const SizedBox(height: 16),
              option(false, Icons.merge_rounded, 'Combine',
                  'Keep this phone\'s data and add the server\'s. Where both changed the same thing, the newest edit wins.'),
              const SizedBox(height: 10),
              option(true, Icons.cloud_download_outlined, 'Use only the server\'s data',
                  'Replace what\'s on this phone with your synced data. Good if this phone only has sample data.'),
              const SizedBox(height: 18),
              FilledButton(
                style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(50)),
                onPressed: () => Navigator.pop(context, serverOnly),
                child: const Text('Continue'),
              ),
            ],
          ),
        );
      });
    },
  );
}

/// Shows a new account's recovery key until they confirm they've saved it.
Future<void> showRecoveryKey(BuildContext context, String key) {
  return showDialog<void>(
    context: context,
    barrierDismissible: false,
    builder: (context) {
      final p = context.pal;
      var saved = false;
      return StatefulBuilder(
        builder: (context, setState) => AlertDialog(
          icon: Center(
            child: Container(
              width: 52,
              height: 52,
              decoration: BoxDecoration(color: p.accentSoft, borderRadius: BorderRadius.circular(16)),
              child: Icon(Icons.key_rounded, color: p.accent, size: 28),
            ),
          ),
          title: const Text('Save your recovery key', textAlign: TextAlign.center),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                  'If you forget your password, this key is the only way back into your synced data. Nobody else has it, not even the server.',
                  textAlign: TextAlign.center,
                  style: TextStyle(fontSize: 14, height: 1.4, color: p.muted)),
              const SizedBox(height: 16),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 14),
                decoration: BoxDecoration(
                  color: p.surfaceAlt,
                  borderRadius: BorderRadius.circular(14),
                  border: Border.all(color: p.border),
                ),
                child: SelectableText(key,
                    textAlign: TextAlign.center,
                    style: TextStyle(
                        fontFamily: 'monospace', fontSize: 15.5, height: 1.5, letterSpacing: 0.6, fontWeight: FontWeight.w600, color: p.text)),
              ),
              const SizedBox(height: 8),
              TextButton.icon(
                icon: const Icon(Icons.copy_rounded, size: 18),
                label: const Text('Copy'),
                onPressed: () {
                  Clipboard.setData(ClipboardData(text: key));
                  toast(context, 'Recovery key copied');
                },
              ),
              CheckboxListTile(
                value: saved,
                onChanged: (v) => setState(() => saved = v ?? false),
                contentPadding: EdgeInsets.zero,
                controlAffinity: ListTileControlAffinity.leading,
                title: const Text('I\'ve saved it somewhere safe', style: TextStyle(fontSize: 14)),
              ),
            ],
          ),
          actionsPadding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
          actions: [
            FilledButton(
              style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(46)),
              onPressed: saved ? () => Navigator.pop(context) : null,
              child: const Text('Done'),
            ),
          ],
        ),
      );
    },
  );
}

/// Forgot password: set a new one with the recovery key. True when signed in.
Future<bool?> showRecoverSheet(BuildContext context, {required String url, required String username}) {
  final sync = SyncScope.read(context);
  final user = TextEditingController(text: username);
  final key = TextEditingController();
  final password = TextEditingController();
  return showModalBottomSheet<bool>(
    context: context,
    useSafeArea: true,
    isScrollControlled: true,
    builder: (context) {
      final p = context.pal;
      var busy = false;
      String? error;
      return StatefulBuilder(builder: (context, setState) {
        Future<void> go() async {
          setState(() { busy = true; error = null; });
          try {
            await sync.recover(url, user.text, key.text, password.text);
            if (context.mounted) Navigator.pop(context, true);
          } on SyncException catch (e) {
            setState(() { busy = false; error = e.message; });
          }
        }

        return Padding(
          padding: EdgeInsets.fromLTRB(22, 4, 22, 22 + MediaQuery.viewInsetsOf(context).bottom),
          child: SingleChildScrollView(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text('Reset your password', style: display(22, p.text)),
                const SizedBox(height: 6),
                Text('Use the recovery key you saved when you created your account. Your other devices will need the new password.',
                    style: TextStyle(fontSize: 14, height: 1.4, color: p.muted)),
                const SizedBox(height: 16),
                const FieldLabel('Username'),
                TextField(controller: user, autocorrect: false, decoration: const InputDecoration(prefixIcon: Icon(Icons.person_outline_rounded))),
                const SizedBox(height: 12),
                const FieldLabel('Recovery key'),
                TextField(
                  controller: key,
                  autocorrect: false,
                  textCapitalization: TextCapitalization.characters,
                  decoration: const InputDecoration(prefixIcon: Icon(Icons.key_rounded), hintText: 'ABCD-EFGH-…'),
                ),
                const SizedBox(height: 12),
                const FieldLabel('New password'),
                TextField(
                  controller: password,
                  obscureText: true,
                  autocorrect: false,
                  enableSuggestions: false,
                  decoration: const InputDecoration(prefixIcon: Icon(Icons.lock_outline_rounded), helperText: 'At least 8 characters'),
                ),
                if (error != null) ...[
                  const SizedBox(height: 12),
                  _Banner(icon: Icons.error_outline_rounded, color: p.danger, text: error!),
                ],
                const SizedBox(height: 18),
                FilledButton(
                  style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(50)),
                  onPressed: busy ? null : go,
                  child: Text(busy ? 'Resetting…' : 'Reset password and sign in'),
                ),
              ],
            ),
          ),
        );
      });
    },
  );
}

// ---------------------------------------------------------------------------------------------
// Connected

class _Connected extends StatefulWidget {
  const _Connected({super.key});

  @override
  State<_Connected> createState() => _ConnectedState();
}

class _ConnectedState extends State<_Connected> {
  late final SyncEngine _sync = SyncScope.read(context);
  Account? _account;
  String? _accountProblem;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final a = await _sync.account();
      if (mounted) setState(() { _account = a; _accountProblem = null; });
    } on SyncException catch (e) {
      if (mounted) setState(() => _accountProblem = e.message);
    }
  }

  Future<void> _syncNow() async {
    final ok = await _sync.syncNow(manual: true);
    if (!mounted) return;
    toast(context, ok ? 'Synced' : (_sync.problem ?? 'Couldn\'t sync'));
    if (ok) _load();
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final sync = SyncScope.of(context);
    final store = StoreScope.of(context);
    final (icon, color) = syncLook(sync.status, p);
    final busy = sync.status == SyncStatus.syncing;
    final pending = sync.pendingCount;

    return RefreshIndicator(
      onRefresh: _syncNow,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 4, 20, 40),
        children: [
          Container(
            padding: const EdgeInsets.fromLTRB(18, 18, 18, 16),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(22),
              gradient: LinearGradient(
                begin: Alignment.topLeft,
                end: Alignment.bottomRight,
                colors: [p.hero, Color.lerp(p.hero, p.accent, 0.5)!],
              ),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Container(
                      width: 46,
                      height: 46,
                      decoration: BoxDecoration(color: Colors.white.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(14)),
                      child: busy
                          ? Padding(padding: const EdgeInsets.all(13), child: CircularProgressIndicator(strokeWidth: 2.4, color: p.mint))
                          : Icon(icon == Icons.cloud_done_outlined ? Icons.cloud_done_rounded : icon,
                              color: sync.status == SyncStatus.idle ? p.mint : color, size: 26),
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(sync.statusText, style: display(19, p.heroText)),
                          const SizedBox(height: 3),
                          Text('${sync.username} on ${sync.serverName}',
                              maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 13, color: p.heroMuted)),
                        ],
                      ),
                    ),
                  ],
                ),
                if (sync.problem != null && sync.status != SyncStatus.idle) ...[
                  const SizedBox(height: 12),
                  Text(sync.problem!, style: TextStyle(fontSize: 13, height: 1.35, color: p.heroText.withValues(alpha: 0.85))),
                ],
                const SizedBox(height: 16),
                Row(
                  children: [
                    Expanded(child: _HeroStat(label: 'Transactions', value: '${store.transactions.length}')),
                    Expanded(child: _HeroStat(label: 'Budgets', value: '${store.budgets.length}')),
                    Expanded(child: _HeroStat(label: 'Waiting to send', value: '$pending')),
                  ],
                ),
                const SizedBox(height: 16),
                SizedBox(
                  width: double.infinity,
                  child: FilledButton.icon(
                    style: FilledButton.styleFrom(
                      minimumSize: const Size.fromHeight(46),
                      backgroundColor: p.mint,
                      foregroundColor: const Color(0xFF0D2219),
                    ),
                    onPressed: busy ? null : _syncNow,
                    icon: const Icon(Icons.sync_rounded, size: 20),
                    label: Text(busy ? 'Syncing…' : 'Sync now'),
                  ),
                ),
              ],
            ),
          ),
          const SectionTitle('Devices'),
          Panel(
            padding: EdgeInsets.zero,
            child: _account == null
                ? Padding(
                    padding: const EdgeInsets.all(18),
                    child: Row(children: [
                      if (_accountProblem == null)
                        const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
                      else
                        Icon(Icons.cloud_off_outlined, size: 18, color: p.muted),
                      const SizedBox(width: 12),
                      Expanded(
                          child: Text(_accountProblem ?? 'Loading your devices…', style: TextStyle(fontSize: 13.5, color: p.muted))),
                    ]),
                  )
                : Column(
                    children: [
                      for (final (i, d) in _account!.devices.indexed) ...[
                        if (i > 0) const Divider(indent: 64),
                        _DeviceTile(
                          device: d,
                          onSignOut: d.isThisDevice
                              ? null
                              : () async {
                                  final ok = await confirm(context,
                                      title: 'Sign out ${d.name}?',
                                      message: 'It stops syncing until someone signs in on it again. Its data stays on it.',
                                      confirmText: 'Sign out');
                                  if (!ok) return;
                                  try {
                                    await _sync.signOutDevice(d.id);
                                    _load();
                                  } on SyncException catch (e) {
                                    if (context.mounted) toast(context, e.message);
                                  }
                                },
                        ),
                      ],
                    ],
                  ),
          ),
          const SectionTitle('Account'),
          Panel(
            padding: EdgeInsets.zero,
            child: Column(
              children: [
                _ActionTile(
                  icon: Icons.dns_outlined,
                  title: sync.serverName,
                  subtitle: sync.serverUrl,
                ),
                const Divider(indent: 64),
                _ActionTile(
                  icon: Icons.password_rounded,
                  title: 'Change password',
                  subtitle: 'Your other devices will ask for the new one',
                  onTap: () => _changePassword(context),
                ),
                const Divider(indent: 64),
                _ActionTile(
                  icon: Icons.link_off_rounded,
                  title: 'Stop syncing on this phone',
                  subtitle: 'Your data stays here and on the server',
                  danger: true,
                  onTap: () async {
                    final ok = await confirm(context,
                        title: 'Stop syncing?',
                        message: 'This phone signs out of ${sync.serverName}. Everything stays on the phone, and you can sign in again later.',
                        confirmText: 'Stop syncing',
                        danger: true);
                    if (ok) await _sync.signOut();
                  },
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _changePassword(BuildContext context) async {
    final current = TextEditingController();
    final next = TextEditingController();
    await showModalBottomSheet<void>(
      context: context,
      useSafeArea: true,
      isScrollControlled: true,
      builder: (context) {
        final p = context.pal;
        var busy = false;
        String? error;
        return StatefulBuilder(builder: (context, setState) {
          Future<void> go() async {
            setState(() { busy = true; error = null; });
            try {
              await _sync.changePassword(current.text, next.text);
              if (!context.mounted) return;
              Navigator.pop(context);
              toast(this.context, 'Password changed');
            } on SyncException catch (e) {
              setState(() { busy = false; error = e.message; });
            }
          }

          return Padding(
            padding: EdgeInsets.fromLTRB(22, 4, 22, 22 + MediaQuery.viewInsetsOf(context).bottom),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text('Change password', style: display(22, p.text)),
                const SizedBox(height: 16),
                const FieldLabel('Current password'),
                TextField(controller: current, obscureText: true, decoration: const InputDecoration(prefixIcon: Icon(Icons.lock_outline_rounded))),
                const SizedBox(height: 12),
                const FieldLabel('New password'),
                TextField(
                    controller: next,
                    obscureText: true,
                    decoration: const InputDecoration(prefixIcon: Icon(Icons.lock_reset_rounded), helperText: 'At least 8 characters')),
                if (error != null) ...[
                  const SizedBox(height: 12),
                  _Banner(icon: Icons.error_outline_rounded, color: p.danger, text: error!),
                ],
                const SizedBox(height: 18),
                FilledButton(
                  style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(50)),
                  onPressed: busy ? null : go,
                  child: Text(busy ? 'Changing…' : 'Change password'),
                ),
              ],
            ),
          );
        });
      },
    );
  }
}

class _HeroStat extends StatelessWidget {
  const _HeroStat({required this.label, required this.value});
  final String label, value;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(value, style: display(20, p.heroText)),
        const SizedBox(height: 1),
        Text(label, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 12, color: p.heroMuted)),
      ],
    );
  }
}

class _DeviceTile extends StatelessWidget {
  const _DeviceTile({required this.device, this.onSignOut});
  final Device device;
  final VoidCallback? onSignOut;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final icon = switch (device.platform.toLowerCase()) {
      'windows' => Icons.desktop_windows_outlined,
      'android' => Icons.phone_android_rounded,
      _ => Icons.devices_other_rounded,
    };
    final seen = device.isThisDevice ? 'This phone' : 'Seen ${SyncEngine.ago(device.lastSeen.toLocal())}';
    return Padding(
      padding: const EdgeInsets.fromLTRB(14, 12, 6, 12),
      child: Row(
        children: [
          Container(
            width: 38,
            height: 38,
            decoration: BoxDecoration(color: p.accentSoft, borderRadius: BorderRadius.circular(11)),
            child: Icon(icon, size: 20, color: p.accent),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(device.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 15, fontWeight: FontWeight.w600, color: p.text)),
                const SizedBox(height: 2),
                Text('${device.platform}${device.appVersion.isEmpty ? '' : ' ${device.appVersion}'} · $seen',
                    style: TextStyle(fontSize: 12.5, color: device.isThisDevice ? p.accent : p.muted)),
              ],
            ),
          ),
          if (onSignOut != null)
            IconButton(tooltip: 'Sign out ${device.name}', onPressed: onSignOut, icon: Icon(Icons.logout_rounded, color: p.muted, size: 20)),
        ],
      ),
    );
  }
}

class _ActionTile extends StatelessWidget {
  const _ActionTile({required this.icon, required this.title, this.subtitle, this.onTap, this.danger = false});
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
                  Text(title, maxLines: 1, overflow: TextOverflow.ellipsis,
                      style: TextStyle(fontSize: 15, fontWeight: FontWeight.w600, color: danger ? p.danger : p.text)),
                  if (subtitle != null) ...[
                    const SizedBox(height: 2),
                    Text(subtitle!, maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 13, height: 1.3, color: p.muted)),
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
