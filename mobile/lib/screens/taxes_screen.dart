import 'dart:io';

import 'package:flutter/material.dart';

import '../dates.dart';
import '../kit.dart';
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
      subtitle: 'W-2s, 1099s, receipts and returns, kept by tax year. Files stay private on this phone (and your server, when syncing).',
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
          label: Text(here ? 'Open' : 'Not on this phone yet'),
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
