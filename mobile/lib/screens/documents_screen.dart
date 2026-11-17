import 'package:flutter/material.dart';

import '../dates.dart';
import '../form.dart';
import '../kit.dart';
import '../platform.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import 'taxes_screen.dart' show fileSize;

/// Insurance policies, car titles, warranties and manuals, kept like tax documents, with a reminder before one ends.
class DocumentsScreen extends StatelessWidget {
  const DocumentsScreen({super.key});

  static IconData icon(String kind) => switch (kind) {
        'Insurance' => Icons.health_and_safety_outlined,
        'Vehicle' => Icons.directions_car_outlined,
        'Home' => Icons.home_outlined,
        'Warranty' => Icons.verified_user_outlined,
        'Manual' => Icons.menu_book_outlined,
        'Medical' => Icons.medical_services_outlined,
        'Legal' => Icons.gavel_outlined,
        'Pet' => Icons.pets_outlined,
        _ => Icons.description_outlined,
      };

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final today = dayOnly(DateTime.now());
    final docs = store.vaultDocs..sort((a, b) => a.title.toLowerCase().compareTo(b.title.toLowerCase()));
    final soon = [for (final d in docs) if (d.expires != null && !d.expires!.isBefore(today) && d.expires!.difference(today).inDays <= 60) d]
      ..sort((a, b) => a.expires!.compareTo(b.expires!));
    final byKind = <String, List<VaultDoc>>{};
    for (final d in docs) {
      (byKind[d.kind] ??= []).add(d);
    }
    return SubPage(
      title: 'Documents',
      subtitle: 'Insurance, car titles, warranties and manuals in one place. Files stay private on this $deviceWord (and your server, when syncing).',
      fab: () => addDocument(context),
      fabLabel: 'Add document',
      fabIcon: Icons.upload_file_rounded,
      children: [
        if (docs.isEmpty)
          EmptyState(
            icon: Icons.folder_special_outlined,
            title: 'No documents yet',
            text: 'Add an insurance policy, a car title or a manual. Give it an end date and you\'ll get a reminder before it runs out.',
            actions: [SizedBox(width: double.infinity, child: FilledButton.icon(onPressed: () => addDocument(context), icon: const Icon(Icons.upload_file_rounded), label: const Text('Add a document')))],
          )
        else ...[
          if (soon.isNotEmpty) ...[
            const SectionTitle('Ending soon'),
            ListPanel(children: [
              for (final d in soon)
                RowTile(
                  leading: IconBox(icon(d.kind), size: 40, color: p.warn),
                  title: d.title,
                  subtitle: 'Ends ${shortDate(d.expires!)}',
                  trailing: '${d.expires!.difference(today).inDays} days',
                  trailingColor: p.warn,
                  onTap: () => _open(context, d),
                ),
            ]),
          ],
          for (final MapEntry(:key, :value) in byKind.entries) ...[
            SectionTitle(key),
            ListPanel(children: [
              for (final d in value)
                RowTile(
                  leading: IconBox(icon(d.kind), size: 40),
                  title: d.title,
                  subtitle: [if (d.expires != null) 'Ends ${shortDate(d.expires!)}', if (d.size > 0) fileSize(d.size), if (d.note.isNotEmpty) d.note].join(' · '),
                  onTap: () => _open(context, d),
                ),
            ]),
          ],
        ],
      ],
    );
  }

  Future<void> _open(BuildContext context, VaultDoc d) => showSheet<void>(
        context,
        Builder(
          builder: (c) {
            final store = StoreScope.read(c);
            final path = store.docPath(d);
            return SheetFrame(
              title: d.title,
              subtitle: [d.kind, if (d.expires != null) 'ends ${shortDate(d.expires!)}', if (d.originalName.isNotEmpty) d.originalName].join(' · '),
              leading: IconBox(icon(d.kind), size: 44),
              children: [
                const SizedBox(height: 12),
                if (path != null)
                  RowTile(
                      leading: const IconBox(Icons.open_in_new_rounded, size: 40),
                      title: 'Open',
                      onTap: () async {
                        final problem = await Platform2.open(path);
                        if (problem != null && c.mounted) toast(c, 'Couldn\'t open it: $problem');
                      }),
                RowTile(
                    leading: const IconBox(Icons.edit_outlined, size: 40),
                    title: 'Edit',
                    onTap: () {
                      Navigator.pop(c);
                      showSheet<void>(context, DocumentSheet(existing: d));
                    }),
              ],
            );
          },
        ),
      );
}

Future<void> addDocument(BuildContext context) async {
  final picked = await Platform2.pickFile();
  if (picked == null || !context.mounted) return;
  final store = StoreScope.read(context);
  final name = await store.keepFile(picked.bytes, picked.extension, folder: 'documents');
  final dot = picked.name.lastIndexOf('.');
  final doc = VaultDoc.create(title: dot > 0 ? picked.name.substring(0, dot) : picked.name, fileName: name, originalName: picked.name, size: picked.bytes.length);
  if (!context.mounted) return;
  await showSheet<void>(context, DocumentSheet(existing: doc, isNew: true));
}

class DocumentSheet extends StatefulWidget {
  const DocumentSheet({super.key, required this.existing, this.isNew = false});
  final VaultDoc existing;
  final bool isNew;

  @override
  State<DocumentSheet> createState() => _DocumentSheetState();
}

class _DocumentSheetState extends State<DocumentSheet> {
  late final VaultDoc _d = widget.existing.copy();
  late final _title = TextEditingController(text: _d.title);
  late final _note = TextEditingController(text: _d.note);
  String? _error;

  @override
  Widget build(BuildContext context) => SheetFrame(
        title: widget.isNew ? 'Add document' : 'Edit document',
        error: _error,
        onDelete: widget.isNew
            ? null
            : () async {
                final ok = await confirm(context, title: 'Delete ${_d.title}?', message: 'The document and its file are removed.', confirmText: 'Delete', danger: true);
                if (!ok || !context.mounted) return;
                StoreScope.read(context).remove(widget.existing);
                Navigator.pop(context);
              },
        primary: widget.isNew ? 'Add' : 'Save',
        onPrimary: () {
          if (_title.text.trim().isEmpty) return setState(() => _error = 'Give it a name.');
          _d
            ..title = _title.text.trim()
            ..note = _note.text.trim();
          StoreScope.read(context).put(_d);
          Navigator.pop(context);
        },
        children: [
          LabeledField(label: 'Name', controller: _title, hint: 'Car insurance policy', caps: TextCapitalization.words),
          const FieldLabel('Kind'),
          Choices(options: DocumentKinds.all, selected: _d.kind, onChanged: (v) => setState(() => _d.kind = v)),
          const FieldLabel('Ends or renews (optional)'),
          DateTile(date: _d.expires, allowClear: true, emptyText: 'No end date', onChanged: (v) => setState(() => _d.expires = v)),
          LabeledField(label: 'Note (optional)', controller: _note, hint: 'Policy number, who to call'),
        ],
      );
}
