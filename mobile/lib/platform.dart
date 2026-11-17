import 'dart:io';
import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:image_picker/image_picker.dart';
import 'package:open_filex/open_filex.dart';
import 'package:url_launcher/url_launcher.dart';

import 'form.dart';

/// A file the person picked: its name and contents.
class PickedFile {
  PickedFile(this.name, this.bytes);
  final String name;
  final Uint8List bytes;

  String get extension {
    final i = name.lastIndexOf('.');
    return i < 0 ? '' : name.substring(i).toLowerCase();
  }
}

/// The phone's own pickers, viewers and browser (kept in one place so tests never touch them).
class Platform2 {
  /// Picks one file (optionally only some extensions, like ["csv", "ofx"]). Null when cancelled.
  static Future<PickedFile?> pickFile({List<String>? extensions, bool images = false}) async {
    final r = await FilePicker.platform.pickFiles(
      type: images ? FileType.image : (extensions == null ? FileType.any : FileType.custom),
      allowedExtensions: images ? null : extensions,
      withData: true,
    );
    final f = r?.files.single;
    if (f == null) return null;
    var bytes = f.bytes;
    if (bytes == null && f.path != null) bytes = await File(f.path!).readAsBytes();
    if (bytes == null) return null;
    return PickedFile(f.name, bytes);
  }

  /// Takes a photo with the camera (or picks one from the gallery). Null when cancelled.
  static Future<PickedFile?> photo({bool camera = true}) async {
    if (isDesktop) return pickFile(images: true); // no camera on a computer
    final x = await ImagePicker().pickImage(source: camera ? ImageSource.camera : ImageSource.gallery, maxWidth: 2200, imageQuality: 85);
    if (x == null) return null;
    return PickedFile(x.name, await x.readAsBytes());
  }

  /// Asks where to save a file (Downloads, Drive…) and saves it. Returns false when cancelled.
  static Future<bool> saveFile(String name, Uint8List bytes) async {
    final path = await FilePicker.platform.saveFile(fileName: name, bytes: bytes);
    if (path == null) return false;
    // On a computer the picker only chooses where: write the file.
    if (isDesktop) await File(path).writeAsBytes(bytes, flush: true);
    return true;
  }

  /// Opens a file in the app that handles it (a PDF viewer, the photo viewer, the package installer).
  /// Returns an error message, or null when it opened.
  static Future<String?> open(String path) async {
    if (isDesktop) return await launchUrl(Uri.file(path)) ? null : 'no app to open it';
    final r = await OpenFilex.open(path);
    return r.type == ResultType.done ? null : r.message;
  }

  static Future<bool> openUrl(String url) => launchUrl(Uri.parse(url), mode: LaunchMode.externalApplication);
}
