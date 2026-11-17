import 'dart:io';

import 'package:flutter/material.dart';

import '../form.dart';

import '../dates.dart';
import '../extras.dart';
import '../kit.dart';
import '../money.dart';
import '../platform.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';

IconData taxIcon(String kind) => switch (kind) {
      'W-2' || '1099' || '1098' || '1095' => Icons.description_outlined,
      'Tax return' => Icons.assignment_turned_in_outlined,
      'Receipt' => Icons.receipt_long_outlined,
      'Charitable donation' => Icons.volunteer_activism_outlined,
      'Property tax' => Icons.home_outlined,
      'Estimated payment' => Icons.event_available_outlined,
      'Letter from IRS/state' => Icons.mail_outline_rounded,
      _ => Icons.insert_drive_file_outlined,
    };

String fileSize(int bytes) => bytes < 1024
    ? '$bytes B'
    : bytes < 1024 * 1024
        ? '${(bytes / 1024).round()} KB'
        : '${(bytes / 1024 / 1024).toStringAsFixed(1)} MB';

class TaxesScreen extends StatefulWidget {
  const TaxesScreen({super.key});

  @override
  State<TaxesScreen> createState() => _TaxesScreenState();
}

class _TaxesScreenState extends State<TaxesScreen> {
  int? _year;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final docs = store.taxDocs;
    final thisYear = DateTime.now().year;
    final years = {thisYear - 1, thisYear, for (final d in docs) d.year}.toList()..sort((a, b) => b.compareTo(a));
    final year = _year ?? (docs.any((d) => d.year == thisYear - 1) || docs.isEmpty ? thisYear - 1 : years.first);
    final inYear = docs.where((d) => d.year == year).toList()..sort((a, b) => a.kind.compareTo(b.kind));
    final byKind = <String, List<TaxDoc>>{};
    for (final d in inYear) {
      (byKind[d.kind] ??= []).add(d);
    }
    return SubPage(
      title: 'Taxes',
      subtitle: 'W-2s, 1099s, receipts and returns, kept by tax year. Files stay private on this $deviceWord (and your server, when syncing).',
      fab: () => addTaxDocument(context, year),
      fabLabel: 'Add document',
      fabIcon: Icons.upload_file_rounded,
      children: [
        SizedBox(
          height: 40,
          child: ListView(
            scrollDirection: Axis.horizontal,
            children: [
              for (final y in years) Padding(padding: const EdgeInsets.only(right: 8), child: ChoicePill(label: '$y', selected: y == year, onTap: () => setState(() => _year = y))),
            ],
          ),
        ),
        const SizedBox(height: 6),
        TaxDeductions(year: year),
        const SectionTitle('Documents'),
        if (inYear.isEmpty) ...[
          const SizedBox(height: 8),
          EmptyState(
            icon: Icons.folder_open_rounded,
            title: 'Nothing for $year yet',
            text: 'Add tax forms as they arrive (PDFs or photos). At tax time everything is in one place.',
            actions: [
              SizedBox(
                width: double.infinity,
                child: FilledButton.icon(onPressed: () => addTaxDocument(context, year), icon: const Icon(Icons.upload_file_rounded), label: const Text('Add a document')),
              ),
            ],
          ),
        ] else
          for (final MapEntry(:key, :value) in byKind.entries) ...[
            SectionTitle(key),
            ListPanel(
              children: [
                for (final d in value)
                  RowTile(
                    leading: IconBox(taxIcon(d.kind), size: 40),
                    title: d.title,
                    subtitle: [if (d.originalName.isNotEmpty) d.originalName, if (d.size > 0) fileSize(d.size), if (d.note.isNotEmpty) d.note].join(' · '),
                    onTap: () => _open(context, store, d),
                  ),
              ],
            ),
          ],
        const SizedBox(height: 12),
        Text('Tap a document to open it. Edit it from the menu that opens.', textAlign: TextAlign.center, style: TextStyle(fontSize: 13, color: p.muted)),
      ],
    );
  }

  Future<void> _open(BuildContext context, Store store, TaxDoc d) => showSheet<void>(context, _TaxActions(d));
}

class _TaxActions extends StatelessWidget {
  const _TaxActions(this.doc);
  final TaxDoc doc;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final d = store.taxDocs.firstWhere((x) => x.id == doc.id, orElse: () => doc);
    final path = store.taxPath(d);
    final here = path != null && File(path).existsSync();
    return SheetFrame(
      title: d.title,
      leading: IconBox(taxIcon(d.kind), size: 44),
      subtitle: '${d.kind} · ${d.year}${d.size > 0 ? ' · ${fileSize(d.size)}' : ''}',
      children: [
        const SizedBox(height: 18),
        FilledButton.icon(
          onPressed: !here
              ? null
              : () async {
                  final error = await Platform2.open(path);
                  if (error != null && context.mounted) toast(context, 'Couldn\'t open it: $error');
                },
          icon: const Icon(Icons.open_in_new_rounded),
          label: Text(here ? 'Open' : 'Not on this $deviceWord yet'),
        ),
        if (!here) ...[
          const SizedBox(height: 8),
          const HintNote('It was added on another device. It downloads the next time WillowWallet syncs.', icon: Icons.cloud_download_outlined),
        ],
        const SizedBox(height: 8),
        OutlinedButton.icon(
          onPressed: () {
            Navigator.pop(context);
            showSheet<void>(context, TaxDocSheet(existing: d));
          },
          icon: const Icon(Icons.edit_outlined, size: 19),
          label: const Text('Edit or move to another year'),
        ),
      ],
    );
  }
}

/// Picks a file, keeps a copy in the app's folder and asks for its details.
Future<void> addTaxDocument(BuildContext context, int year) async {
  final picked = await Platform2.pickFile();
  if (picked == null || !context.mounted) return;
  final store = StoreScope.read(context);
  final name = await store.keepFile(picked.bytes, picked.extension, folder: 'taxes/$year');
  final dot = picked.name.lastIndexOf('.');
  final doc = TaxDoc.create(year: year, title: dot > 0 ? picked.name.substring(0, dot) : picked.name, fileName: name, originalName: picked.name, size: picked.bytes.length);
  if (!context.mounted) return;
  await showSheet<void>(context, TaxDocSheet(existing: doc, isNew: true));
}

class TaxDocSheet extends StatefulWidget {
  const TaxDocSheet({super.key, required this.existing, this.isNew = false});
  final TaxDoc existing;
  final bool isNew;

  @override
  State<TaxDocSheet> createState() => _TaxDocSheetState();
}

class _TaxDocSheetState extends State<TaxDocSheet> {
  late final TaxDoc _d = widget.existing.copy();
  late final _title = TextEditingController(text: _d.title);
  late final _note = TextEditingController(text: _d.note);
  String? _error;

  @override
  void dispose() {
    _title.dispose();
    _note.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final store = StoreScope.read(context);
    final title = _title.text.trim();
    if (title.isEmpty) return setState(() => _error = 'Enter a title, like "W-2 from Acme".');
    final oldYear = widget.existing.year;
    _d
      ..title = title
      ..note = _note.text.trim();
    if (!widget.isNew && oldYear != _d.year && store.filesDir != null && _d.fileName.isNotEmpty) {
      // Move the file to the new year's folder
      final from = File('${store.filesDir!.path}/taxes/$oldYear/${_d.fileName}');
      if (await from.exists()) {
        final to = File('${store.filesDir!.path}/taxes/${_d.year}/${_d.fileName}');
        await to.parent.create(recursive: true);
        await from.rename(to.path);
      }
    } else if (widget.isNew && _d.year != oldYear && store.filesDir != null) {
      final from = File('${store.filesDir!.path}/taxes/$oldYear/${_d.fileName}');
      if (await from.exists()) {
        final to = File('${store.filesDir!.path}/taxes/${_d.year}/${_d.fileName}');
        await to.parent.create(recursive: true);
        await from.rename(to.path);
      }
    }
    store.put(_d);
    if (!mounted) return;
    Navigator.pop(context);
    toast(context, widget.isNew ? 'Added $title to ${_d.year}.' : 'Saved $title.');
  }

  Future<void> _delete() async {
    final ok = await confirm(context, title: 'Delete ${_d.title}?', message: 'The document and its file are removed from WillowWallet.', confirmText: 'Delete', danger: true);
    if (!ok || !mounted) return;
    final store = StoreScope.read(context);
    final path = store.taxPath(widget.existing);
    store.remove(widget.existing);
    if (path != null) {
      try {
        await File(path).delete();
      } catch (_) {}
    }
    if (mounted) Navigator.pop(context);
  }

  @override
  void deactivate() {
    // A new document that was never saved: don't leave its file behind.
    final store = StoreScope.read(context);
    if (widget.isNew && !store.taxDocs.any((d) => d.id == _d.id)) {
      final path = store.taxPath(widget.existing);
      if (path != null) {
        try {
          File(path).deleteSync();
        } catch (_) {}
      }
    }
    super.deactivate();
  }

  @override
  Widget build(BuildContext context) {
    final thisYear = DateTime.now().year;
    return SheetFrame(
      title: widget.isNew ? 'Add document' : 'Edit document',
      subtitle: widget.isNew ? widget.existing.originalName : null,
      onDelete: widget.isNew ? null : _delete,
      error: _error,
      primary: widget.isNew ? 'Add document' : 'Save',
      onPrimary: _save,
      children: [
        LabeledField(label: 'Title', controller: _title, caps: TextCapitalization.sentences),
        const FieldLabel('Kind'),
        Choices(options: TaxKinds.all, selected: _d.kind, icons: taxIcon, onChanged: (v) => setState(() => _d.kind = v)),
        const FieldLabel('Tax year'),
        Choices(options: [for (var y = thisYear; y >= thisYear - 6; y--) '$y'], selected: '${_d.year}', onChanged: (v) => setState(() => _d.year = int.parse(v))),
        LabeledField(label: 'Note (optional)', controller: _note),
        const SizedBox(height: 4),
        Text('Added ${shortDate(_d.updatedAt.toLocal())}', style: TextStyle(fontSize: 12.5, color: context.pal.muted)),
      ],
    );
  }
}

/// Tax-deductible spending (marked on transactions) and the mileage log for a tax year.
class TaxDeductions extends StatelessWidget {
  const TaxDeductions({super.key, required this.year});
  final int year;

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final deductible = store.deductibleTotals(year);
    final miles = store.mileageTotals(year);
    final trips = store.mileage.where((t) => t.date.year == year).toList()..sort((a, b) => b.date.compareTo(a.date));
    final mileageCents = miles.values.fold<int>(0, (s, m) => s + m.$2);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionTitle('Deductions'),
        Panel(
          key: const Key('tax-deductions'),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (deductible.isEmpty)
                Text('Mark spending as tax deductible (charity, medical, business…) when you add it, and it adds up here.', style: TextStyle(color: p.muted, height: 1.35))
              else
                for (final e in deductible.entries)
                  Padding(
                    padding: const EdgeInsets.symmetric(vertical: 4),
                    child: Row(children: [Expanded(child: Text(e.key, style: const TextStyle(fontWeight: FontWeight.w600))), Text(Money.format(e.value))]),
                  ),
              if (mileageCents > 0)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 4),
                  child: Row(children: [
                    const Expanded(child: Text('Mileage', style: TextStyle(fontWeight: FontWeight.w600))),
                    Text(Money.format(mileageCents)),
                  ]),
                ),
            ],
          ),
        ),
        SectionTitle('Mileage log', action: 'Log a trip', onAction: () => showSheet<void>(context, MileageSheet(year: year))),
        if (trips.isEmpty)
          Text('Driving for work, to the doctor or for a charity can be deductible. Log the miles as you go.', style: TextStyle(color: p.muted, height: 1.35))
        else ...[
          ListPanel(children: [
            for (final t in trips.take(12))
              RowTile(
                leading: const IconBox(Icons.directions_car_outlined, size: 38),
                title: t.purpose.isEmpty ? t.kind : t.purpose,
                subtitle: '${shortDate(t.date)} · ${t.kind}',
                trailing: '${t.miles.toStringAsFixed(t.miles == t.miles.roundToDouble() ? 0 : 1)} mi',
                trailingSub: Money.format((t.miles * store.mileageRate(t.kind)).round()),
                onTap: () => showSheet<void>(context, MileageSheet(year: year, existing: t)),
              ),
          ]),
          const SizedBox(height: 6),
          Text(
            [for (final e in miles.entries) '${e.key}: ${e.value.$1.toStringAsFixed(0)} mi at ${store.mileageRate(e.key).toStringAsFixed(1)}¢'].join(' · '),
            style: TextStyle(fontSize: 12.5, color: p.muted),
          ),
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton(onPressed: () => showSheet<void>(context, const _RatesSheet()), child: const Text('Mileage rates')),
          ),
        ],
      ],
    );
  }
}

class MileageSheet extends StatefulWidget {
  const MileageSheet({super.key, required this.year, this.existing});
  final int year;
  final MileageTrip? existing;

  @override
  State<MileageSheet> createState() => _MileageSheetState();
}

class _MileageSheetState extends State<MileageSheet> {
  late final MileageTrip _t = widget.existing?.copy() ??
      MileageTrip.create(date: DateTime.now().year == widget.year ? dayOnly(DateTime.now()) : DateTime(widget.year, 12, 31), miles: 0);
  late final _miles = TextEditingController(text: _t.miles == 0 ? '' : '${_t.miles}');
  late final _purpose = TextEditingController(text: _t.purpose);
  String? _error;

  @override
  Widget build(BuildContext context) => SheetFrame(
        title: widget.existing == null ? 'Log a trip' : 'Edit trip',
        error: _error,
        onDelete: widget.existing == null
            ? null
            : () {
                StoreScope.read(context).remove(widget.existing!);
                Navigator.pop(context);
              },
        primary: 'Save',
        onPrimary: () {
          final m = double.tryParse(_miles.text.trim().replaceAll(',', '.'));
          if (m == null || m <= 0) return setState(() => _error = 'Enter the miles, like 24.5.');
          _t
            ..miles = m
            ..purpose = _purpose.text.trim();
          StoreScope.read(context).put(_t);
          Navigator.pop(context);
        },
        children: [
          LabeledField(label: 'Miles', controller: _miles, hint: '24.5', keyboard: const TextInputType.numberWithOptions(decimal: true), autofocus: widget.existing == null),
          LabeledField(label: 'What for', controller: _purpose, hint: 'Client visit, doctor, food bank'),
          const FieldLabel('Kind'),
          Choices(options: MileageKinds.all, selected: _t.kind, onChanged: (v) => setState(() => _t.kind = v)),
          const FieldLabel('Date'),
          DateTile(date: _t.date, onChanged: (d) => setState(() => _t.date = d ?? _t.date)),
        ],
      );
}

class _RatesSheet extends StatefulWidget {
  const _RatesSheet();

  @override
  State<_RatesSheet> createState() => _RatesSheetState();
}

class _RatesSheetState extends State<_RatesSheet> {
  late final store = StoreScope.read(context);
  late final _c = {for (final k in MileageKinds.all) k: TextEditingController(text: store.mileageRate(k).toString())};

  @override
  Widget build(BuildContext context) => SheetFrame(
        title: 'Mileage rates',
        subtitle: 'Cents per mile. They start at the IRS standard rates; check the rate for your tax year at irs.gov.',
        primary: 'Save',
        onPrimary: () {
          store.updatePrefs((p) => p.mileageRates = {for (final e in _c.entries) e.key: double.tryParse(e.value.text) ?? MileageKinds.defaultRates[e.key]!});
          Navigator.pop(context);
        },
        children: [for (final e in _c.entries) LabeledField(label: e.key, controller: e.value, keyboard: const TextInputType.numberWithOptions(decimal: true))],
      );
}
