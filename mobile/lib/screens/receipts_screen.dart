import 'dart:io';

import 'package:flutter/material.dart';

import '../dates.dart';
import '../form.dart';
import '../kit.dart';
import '../models.dart';
import '../money.dart';
import '../platform.dart';
import '../receipt_reader.dart';
import '../records.dart';
import '../store.dart';
import '../theme.dart';
import '../widgets.dart';
import 'transaction_sheet.dart';

/// Takes a photo of a receipt (or picks one), reads the store, total and date on the device, and opens a new
/// transaction with them filled in and the receipt attached.
Future<void> scanReceipt(BuildContext context, {bool camera = true}) async {
  final store = StoreScope.read(context);
  PickedFile? photo;
  try {
    photo = camera ? await Platform2.photo(camera: true) : await Platform2.pickFile(extensions: const ['jpg', 'jpeg', 'png', 'heic', 'webp', 'pdf']);
  } catch (e) {
    if (context.mounted) toast(context, 'Couldn\'t open the camera: $e');
    return;
  }
  if (photo == null || !context.mounted) return;
  final name = await store.keepFile(photo.bytes, photo.extension.isEmpty ? '.jpg' : photo.extension, folder: 'receipts');
  final path = store.receiptPath(name);
  var guess = const ReceiptGuess();
  if (path != null && !name.endsWith('.pdf')) guess = await ReceiptReader.read(path);
  if (!context.mounted) return;
  if (guess.isEmpty) {
    toast(context, await ReceiptReader.available() ? 'Couldn\'t read that receipt. Fill in what it says.' : 'Receipt attached. Fill in what it says.');
  }
  await showTransactionSheet(context, prefill: TxnPrefill(payee: guess.payee, cents: guess.cents == null ? null : -guess.cents!, date: guess.date, receipt: name));
}

/// Every receipt, by year and month, like Taxes keeps documents. Scan new ones from here.
class ReceiptsScreen extends StatefulWidget {
  const ReceiptsScreen({super.key});

  @override
  State<ReceiptsScreen> createState() => _ReceiptsScreenState();
}

class _ReceiptsScreenState extends State<ReceiptsScreen> {
  int? _year;
  String _search = '';

  @override
  Widget build(BuildContext context) {
    final store = StoreScope.of(context);
    final p = context.pal;
    final all = store.transactions.where((t) => t.receipt.isNotEmpty).toList()..sort((a, b) => b.date.compareTo(a.date));
    final thisYear = DateTime.now().year;
    final years = {thisYear, for (final t in all) t.date.year}.toList()..sort((a, b) => b.compareTo(a));
    final year = _year ?? thisYear;
    final q = _search.trim().toLowerCase();
    final shown = [
      for (final t in all)
        if (t.date.year == year && (q.isEmpty || t.payee.toLowerCase().contains(q) || t.category.toLowerCase().contains(q) || t.note.toLowerCase().contains(q))) t,
    ];
    final byMonth = <int, List<Txn>>{};
    for (final t in shown) {
      (byMonth[t.date.month] ??= []).add(t);
    }
    final total = shown.fold<int>(0, (s, t) => s - t.cents);

    return SubPage(
      title: 'Receipts',
      subtitle: 'Every receipt you\'ve kept, by year. Scan one and WillowWallet reads the store, total and date for you.',
      fab: () => _scan(context),
      fabLabel: 'Scan a receipt',
      fabIcon: Icons.document_scanner_outlined,
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
        const SizedBox(height: 10),
        TextField(
          onChanged: (v) => setState(() => _search = v),
          decoration: const InputDecoration(hintText: 'Search receipts', prefixIcon: Icon(Icons.search_rounded)),
        ),
        const SizedBox(height: 12),
        if (shown.isEmpty)
          EmptyState(
            icon: Icons.receipt_long_outlined,
            title: q.isEmpty ? 'No receipts for $year yet' : 'No receipts match',
            text: 'Scan a receipt and it becomes a transaction with the photo attached, kept here by year.',
            actions: [
              SizedBox(width: double.infinity, child: FilledButton.icon(onPressed: () => _scan(context), icon: const Icon(Icons.document_scanner_outlined), label: const Text('Scan a receipt'))),
            ],
          )
        else ...[
          Text('${shown.length} receipt${shown.length == 1 ? '' : 's'} · ${Money.format(total, whole: true)}', style: TextStyle(color: p.muted, fontSize: 13.5)),
          for (final MapEntry(:key, :value) in byMonth.entries) ...[
            SectionTitle(monthNames[key - 1]),
            ListPanel(
              children: [
                for (final t in value)
                  InkWell(
                    onTap: () => _actions(context, t),
                    borderRadius: BorderRadius.circular(14),
                    child: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 8),
                      child: Row(
                        children: [
                          ReceiptThumb(t.receipt, size: 52),
                          const SizedBox(width: 12),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(t.payee.isEmpty ? 'Receipt' : t.payee, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
                                Text('${shortDate(t.date)} · ${t.category} · ${t.account}', style: TextStyle(fontSize: 12.5, color: p.muted)),
                              ],
                            ),
                          ),
                          Text(Money.format(t.cents, signed: true), style: const TextStyle(fontWeight: FontWeight.w700)),
                        ],
                      ),
                    ),
                  ),
              ],
            ),
          ],
        ],
      ],
    );
  }

  Future<void> _scan(BuildContext context) async {
    if (isDesktop) return scanReceipt(context, camera: false);
    final camera = await showSheet<bool>(
      context,
      Builder(
        builder: (c) => SheetFrame(
          title: 'Scan a receipt',
          subtitle: 'Read on this phone. Nothing is sent anywhere.',
          children: [
            const SizedBox(height: 12),
            RowTile(leading: const IconBox(Icons.photo_camera_outlined, size: 40), title: 'Take a photo', onTap: () => Navigator.pop(c, true)),
            RowTile(leading: const IconBox(Icons.photo_library_outlined, size: 40), title: 'Choose a photo or PDF', onTap: () => Navigator.pop(c, false)),
          ],
        ),
      ),
    );
    if (camera != null && context.mounted) await scanReceipt(context, camera: camera);
  }

  Future<void> _actions(BuildContext context, Txn t) async {
    final store = StoreScope.read(context);
    final path = store.receiptPath(t.receipt);
    final here = path != null && File(path).existsSync();
    await showSheet<void>(
      context,
      Builder(
        builder: (c) => SheetFrame(
          title: t.payee.isEmpty ? 'Receipt' : t.payee,
          subtitle: '${Money.format(t.cents, signed: true)} · ${shortDate(t.date)}',
          leading: ReceiptThumb(t.receipt, size: 48),
          children: [
            const SizedBox(height: 12),
            if (here)
              RowTile(
                  leading: const IconBox(Icons.open_in_new_rounded, size: 40),
                  title: 'Open the receipt',
                  onTap: () async {
                    final problem = await Platform2.open(path);
                    if (problem != null && c.mounted) toast(c, 'Couldn\'t open it: $problem');
                  })
            else
              const HintNote('This receipt is on another device. It downloads when you sync.'),
            RowTile(
                leading: const IconBox(Icons.edit_outlined, size: 40),
                title: 'Edit the transaction',
                onTap: () {
                  Navigator.pop(c);
                  showTransactionSheet(context, existing: t);
                }),
            if (here)
              RowTile(
                leading: const IconBox(Icons.folder_copy_outlined, size: 40),
                title: 'Copy to Taxes',
                subtitle: 'Keep it with your ${t.date.year} tax documents (a donation, a medical bill)',
                onTap: () async {
                  final bytes = await File(path).readAsBytes();
                  final ext = path.contains('.') ? path.substring(path.lastIndexOf('.')) : '.jpg';
                  final name = await store.keepFile(bytes, ext, folder: 'taxes/${t.date.year}');
                  store.put(TaxDoc.create(
                      year: t.date.year, title: '${t.payee.isEmpty ? 'Receipt' : t.payee} ${shortDate(t.date)}', kind: 'Receipt', fileName: name, originalName: t.receipt, size: bytes.length));
                  if (c.mounted) {
                    Navigator.pop(c);
                    toast(context, 'Copied to Taxes, ${t.date.year}.');
                  }
                },
              ),
          ],
        ),
      ),
    );
  }
}

/// A receipt's photo (or a document icon for a PDF, or when it's on another device).
class ReceiptThumb extends StatelessWidget {
  const ReceiptThumb(this.fileName, {super.key, this.size = 52});
  final String fileName;
  final double size;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final path = StoreScope.read(context).receiptPath(fileName);
    final here = path != null && !fileName.toLowerCase().endsWith('.pdf') && File(path).existsSync();
    return ClipRRect(
      borderRadius: BorderRadius.circular(10),
      child: here
          ? Image.file(File(path), width: size, height: size, fit: BoxFit.cover, cacheWidth: (size * 3).round())
          : Container(width: size, height: size, color: p.surfaceAlt, child: Icon(Icons.receipt_long_outlined, color: p.accent)),
    );
  }
}
