import 'dart:convert';
import 'dart:isolate';
import 'dart:math';
import 'dart:typed_data';

import 'package:cryptography/cryptography.dart';

/// End-to-end encryption for WillowWallet sync, the same as the Windows app and server/PROTOCOL.md:
/// password → PBKDF2-SHA256 → 32-byte auth secret (sent to the server) + 32-byte key-encryption key (stays here);
/// a random data key sealed with AES-256-GCM; records and files sealed with the data key.
class SyncCrypto {
  static const defaultIterations = 600000;
  static const keyAad = 'ww-key-v1';
  static final _random = Random.secure();
  static final _aes = AesGcm.with256bits(nonceLength: 12);

  static Uint8List randomBytes(int n) => Uint8List.fromList(List.generate(n, (_) => _random.nextInt(256)));

  /// Runs in a background isolate so the screen stays smooth (it takes a second or two on a phone).
  static Future<Credentials> fromPassword(String password, List<int> salt, int iterations) =>
      Isolate.run(() => _derive(password, salt, iterations));

  static Future<Credentials> _derive(String password, List<int> salt, int iterations) async {
    final pbkdf2 = Pbkdf2(macAlgorithm: Hmac.sha256(), iterations: iterations, bits: 512);
    final key = await pbkdf2.deriveKey(secretKey: SecretKey(utf8.encode(password)), nonce: salt);
    final bytes = await key.extractBytes();
    return Credentials(Uint8List.fromList(bytes.sublist(0, 32)), Uint8List.fromList(bytes.sublist(32, 64)));
  }

  static Future<Credentials> fromRecoveryKey(List<int> recoveryKey) async {
    Future<Uint8List> h(String info) async {
      final k = await Hkdf(hmac: Hmac.sha256(), outputLength: 32)
          .deriveKey(secretKey: SecretKey(recoveryKey), nonce: const <int>[], info: utf8.encode(info));
      return Uint8List.fromList(await k.extractBytes());
    }

    return Credentials(await h('ww-recovery-auth'), await h('ww-recovery-kek'));
  }

  /// nonce ‖ ciphertext ‖ tag
  static Future<Uint8List> sealBytes(List<int> key, List<int> plain, String aad, {List<int>? nonce}) async {
    final box = await _aes.encrypt(plain, secretKey: SecretKey(key), nonce: nonce ?? randomBytes(12), aad: utf8.encode(aad));
    return Uint8List.fromList([...box.nonce, ...box.cipherText, ...box.mac.bytes]);
  }

  static Future<Uint8List> openBytes(List<int> key, List<int> sealed, String aad) async {
    if (sealed.length < 28) throw const FormatException('Too short');
    final box = SecretBox(sealed.sublist(12, sealed.length - 16), nonce: sealed.sublist(0, 12), mac: Mac(sealed.sublist(sealed.length - 16)));
    return Uint8List.fromList(await _aes.decrypt(box, secretKey: SecretKey(key), aad: utf8.encode(aad)));
  }

  /// "v1." + base64(nonce ‖ ciphertext ‖ tag)
  static Future<String> seal(List<int> key, List<int> plain, String aad, {List<int>? nonce}) async =>
      'v1.${base64.encode(await sealBytes(key, plain, aad, nonce: nonce))}';

  static Future<Uint8List> open(List<int> key, String sealed, String aad) {
    if (!sealed.startsWith('v1.')) throw const FormatException('Unknown format');
    return openBytes(key, base64.decode(sealed.substring(3)), aad);
  }

  static String recordAad(String collection, String id) => 'ww-rec-v1:$collection:$id';

  static Future<String> sealRecord(List<int> key, String collection, String id, Map<String, dynamic> record) =>
      seal(key, utf8.encode(jsonEncode(record)), recordAad(collection, id));

  static Future<Map<String, dynamic>> openRecord(List<int> key, String collection, String id, String payload) async =>
      jsonDecode(utf8.decode(await open(key, payload, recordAad(collection, id)))) as Map<String, dynamic>;

  /// "GIZT-INJW-…": 160 random bits in base32, groups of four.
  static (Uint8List, String) newRecoveryKey() {
    final bytes = randomBytes(20);
    return (bytes, formatRecoveryKey(bytes));
  }

  static String formatRecoveryKey(List<int> bytes) {
    final b32 = base32Encode(bytes);
    return [for (var i = 0; i < b32.length; i += 4) b32.substring(i, min(i + 4, b32.length))].join('-');
  }

  static Uint8List? parseRecoveryKey(String text) {
    final clean = text.toUpperCase().replaceAll(RegExp(r'[^A-Z0-9]'), '');
    if (clean.length != 32) return null;
    return base32Decode(clean);
  }

  static const _alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';

  static String base32Encode(List<int> data) {
    final sb = StringBuffer();
    var buffer = 0, bits = 0;
    for (final b in data) {
      buffer = ((buffer << 8) | b) & 0xFFFF;
      bits += 8;
      while (bits >= 5) {
        sb.write(_alphabet[(buffer >> (bits - 5)) & 31]);
        bits -= 5;
      }
    }
    if (bits > 0) sb.write(_alphabet[(buffer << (5 - bits)) & 31]);
    return sb.toString();
  }

  static Uint8List? base32Decode(String text) {
    final out = <int>[];
    var buffer = 0, bits = 0;
    for (final c in text.split('')) {
      final v = _alphabet.indexOf(c);
      if (v < 0) return null;
      buffer = ((buffer << 5) | v) & 0xFFFF;
      bits += 5;
      if (bits >= 8) {
        out.add((buffer >> (bits - 8)) & 0xFF);
        bits -= 8;
      }
    }
    return Uint8List.fromList(out);
  }
}

class Credentials {
  Credentials(this.authSecret, this.keyEncryptionKey);
  final Uint8List authSecret;
  final Uint8List keyEncryptionKey;
}
