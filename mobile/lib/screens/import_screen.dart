import 'dart:convert';

import 'package:flutter/material.dart';

import '../dates.dart';
import '../importer.dart';
import '../kit.dart';
import '../money.dart';
import '../platform.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

/// Import a statement downloaded from your bank, card or payment app (CSV, OFX or QFX).
class ImportScreen extends StatefulWidget {
  const ImportScreen({super.key, this.initialText, this.initialName});
  final String? initialText;
  final String? initialName;

  @override
  State<ImportScreen> createState() => _ImportScreenState();
}

class _ImportScreenState extends State<ImportScreen> {
  String? _fileName;
  Statement? _statement;
  CsvMapping? _mapping;
  List<ImportedRow> _rows = [];
  int _skipped = 0;
  String _account = '';
  String? _error;

  @override
  void initState() {
    super.initState();
    if (widget.initialText != null) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) _load(widget.initialText!, widget.initialName ?? 'statement.csv');
      });
    }
  }

  Future<void> _pick() async {
    try {
      final f = await Platform2.pickFile(extensions: const ['csv', 'ofx', 'qfx', 'txt']);
      if (f == null) return;
      _load(utf8.decode(f.bytes, allowMalformed: true), f.name);
    } catch (e) {
      setState(() => _error = 'Couldn\'t read that file: $e');
    }
  }

  void _load(String text, String name) {
    final store = StoreScope.read(context);
    final s = Importer.read(text, name);
    _fileName = name;
    _statement = s;
    _error = null;
    if (s.format == 'CSV') {
      _mapping = Importer.guessMapping(s.headers);
      // A payment app's file goes to that app's account when there is one
      final source = _mapping!.source;
      if (source.isNotEmpty) {
        final match = store.accountRecords.where((a) => a.name.toLowerCase() == source.toLowerCase()).toList();
        if (match.isNotEmpty) _account = match.first.name;
      }
    } else {
      _mapping = null;
    }
    _apply();
  }

  void _apply() {
    final s = _statement;
    if (s == null) return;
    final store = StoreScope.read(context);
    if (_account.isEmpty) _account = store.accountNames().first;
    if (_mapping != null) {
      final (rows, skipped) = Importer.apply(s, _mapping!);
      _rows = rows;
      _skipped = skipped;
    } else {
      _rows = s.rows;
      _skipped = 0;
    }
    store.markDuplicates(_rows, _account);
    setState(() {});
  }

  void _commit() {
    final store = StoreScope.read(context);
    final n = store.commitImport(_rows, _account);
    final dupes = _rows.where((r) => r.duplicate).length;
    // An OFX file says the account's balance: a new account starts from it.
    final s = _statement!;
    if (s.ledgerCents != null && s.ledgerDate != null && !store.accountRecords.any((a) => a.name == _account)) {
      store.put(Account.create(name: _account, startingCents: s.ledgerCents!, balanceDate: s.ledgerDate!));
    }
    Navigator.pop(context);
    toast(context, 'Imported $n transaction${n == 1 ? '' : 's'} into $_account${dupes > 0 ? ' ($dupes already here were skipped)' : ''}.');
  }

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final s = _statement;
    final fresh = _rows.where((r) => !r.duplicate).toList();
    return SubPage(
      title: 'Import',
      subtitle: 'Download a statement from your bank, card or payment app (CSV, OFX or QFX), then pick it here.',
      children: [
        Panel(
          onTap: _pick,
          child: Row(
            children: [
              const IconBox(Icons.upload_file_rounded),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(_fileName ?? 'Choose a file', style: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700)),
                    Text(
                      s == null
                          ? 'CSV, OFX or QFX. PayPal, Venmo and Cash App downloads are recognized.'
                          : '${s.format}${_mapping?.source.isNotEmpty == true ? ' from ${_mapping!.source}' : ''} · ${_rows.length} rows${_skipped > 0 ? ' · $_skipped skipped' : ''}',
                      style: TextStyle(fontSize: 13, color: p.muted),
                    ),
                  ],
                ),
              ),
              Icon(Icons.chevron_right_rounded, color: p.muted),
            ],
          ),
        ),
        if (_error != null) ...[const SizedBox(height: 12), ErrorNote(_error!)],
        if (s != null) ...[
          const FieldLabel('Into account'),
          Choices(
            options: store.accountNames(current: _account),
            selected: _account,
            onChanged: (v) {
              _account = v;
              _apply();
            },
          ),
          if (_mapping != null) ...[
            const SectionTitle('Columns'),
            Panel(
              child: Column(
                children: [
                  _column('Date', _mapping!.date, (i) => _mapping!.date = i),
                  _column('Description', _mapping!.payee, (i) => _mapping!.payee = i),
                  _column('Amount', _mapping!.amount, (i) => _mapping!.amount = i, optional: true),
                  if (_mapping!.amount < 0) ...[
                    _column('Money out', _mapping!.debit, (i) => _mapping!.debit = i, optional: true),
                    _column('Money in', _mapping!.credit, (i) => _mapping!.credit = i, optional: true),
                  ],
                  _column('Category', _mapping!.category, (i) => _mapping!.category = i, optional: true),
                  SwitchRow(
                    title: 'Spending shows as positive',
                    subtitle: 'Common for credit card files. Flips every amount.',
                    value: _mapping!.flipSign,
                    onChanged: (v) {
                      _mapping!.flipSign = v;
                      _apply();
                    },
                  ),
                ],
              ),
            ),
            if (!_mapping!.isComplete) ...[const SizedBox(height: 10), const ErrorNote('Pick the date, description and amount columns.')],
          ],
          SectionTitle('Preview', action: '${fresh.length} new'),
          if (_rows.isEmpty)
            Text('No transactions found in this file.', style: TextStyle(color: p.muted))
          else
            ListPanel(
              children: [
                for (final r in _rows.take(40))
                  RowTile(
                    leading: CategoryAvatar(store.categoryForRow(r), size: 36, income: r.cents > 0),
                    title: r.payee.isEmpty ? '(no description)' : r.payee,
                    subtitle: '${shortDate(r.date)} · ${store.categoryForRow(r)}${r.duplicate ? ' · already here' : ''}',
                    subtitleColor: r.duplicate ? p.warn : null,
                    trailing: Money.format(r.cents, signed: true),
                    trailingColor: r.duplicate ? p.muted : (r.cents > 0 ? p.positive : null),
                  ),
              ],
            ),
          if (_rows.length > 40) Padding(padding: const EdgeInsets.only(top: 8), child: Text('…and ${_rows.length - 40} more.', style: TextStyle(color: p.muted))),
          const SizedBox(height: 18),
          FilledButton(
            onPressed: fresh.isEmpty ? null : _commit,
            child: Text(fresh.isEmpty ? 'Nothing new to import' : 'Import ${fresh.length} transaction${fresh.length == 1 ? '' : 's'}'),
          ),
        ],
      ],
    );
  }

  Widget _column(String label, int value, void Function(int) set, {bool optional = false}) {
    final p = context.pal;
    final headers = _statement!.headers;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        children: [
          SizedBox(width: 108, child: Text(label, style: const TextStyle(fontWeight: FontWeight.w600))),
          Expanded(
            child: PopupMenuButton<int>(
              tooltip: label,
              onSelected: (i) {
                set(i);
                _apply();
              },
              itemBuilder: (_) => [
                if (optional) const PopupMenuItem(value: -1, child: Text('None')),
                for (var i = 0; i < headers.length; i++) PopupMenuItem(value: i, child: Text(headers[i].isEmpty ? 'Column ${i + 1}' : headers[i])),
              ],
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                decoration: BoxDecoration(color: p.surfaceAlt, borderRadius: BorderRadius.circular(10)),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(value >= 0 && value < headers.length ? (headers[value].isEmpty ? 'Column ${value + 1}' : headers[value]) : 'None',
                          maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: value >= 0 ? p.text : p.muted)),
                    ),
                    Icon(Icons.arrow_drop_down_rounded, color: p.muted),
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
