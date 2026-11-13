import 'dart:math' as math;

import 'package:flutter/material.dart';

/// A color theme: the same eight as WillowWallet for Windows.
class ColorThemeDef {
  const ColorThemeDef(this.name, this.hue, this.sat, this.description);
  final String name;
  final double hue;
  final double sat;
  final String description;
}

const colorThemes = <ColorThemeDef>[
  ColorThemeDef('Pine', 157, 55, 'Deep green'),
  ColorThemeDef('Ocean', 212, 62, 'Calm blue'),
  ColorThemeDef('Lagoon', 186, 72, 'Teal'),
  ColorThemeDef('Indigo', 234, 52, 'Blue-violet'),
  ColorThemeDef('Plum', 282, 38, 'Purple'),
  ColorThemeDef('Rose', 346, 58, 'Raspberry'),
  ColorThemeDef('Ember', 22, 80, 'Warm orange'),
  ColorThemeDef('Graphite', 215, 12, 'Slate'),
];

ColorThemeDef findColorTheme(String name) =>
    colorThemes.firstWhere((t) => t.name == name, orElse: () => colorThemes.first);

/// HSL with h 0–360 and s, l 0–100 (the same math as the desktop app).
Color hsl(double h, double s, double l) =>
    HSLColor.fromAHSL(1, h % 360, (s / 100).clamp(0.0, 1.0), (l / 100).clamp(0.0, 1.0)).toColor();

/// Every color the app uses, for one color theme in light or dark.
@immutable
class Palette extends ThemeExtension<Palette> {
  const Palette({
    required this.dark,
    required this.bg,
    required this.surface,
    required this.surfaceAlt,
    required this.border,
    required this.track,
    required this.text,
    required this.muted,
    required this.hero,
    required this.heroText,
    required this.heroMuted,
    required this.heroTrack,
    required this.accent,
    required this.onAccent,
    required this.accentSoft,
    required this.mint,
    required this.barMuted,
    required this.warn,
    required this.danger,
    required this.positive,
  });

  final bool dark;
  final Color bg, surface, surfaceAlt, border, track, text, muted;
  final Color hero, heroText, heroMuted, heroTrack;
  final Color accent, onAccent, accentSoft, mint, barMuted;
  final Color warn, danger, positive;

  static const pineLight = Palette(
    dark: false,
    bg: Color(0xFFF3F4EF),
    surface: Color(0xFFFFFFFF),
    surfaceAlt: Color(0xFFEEF2EC),
    border: Color(0xFFE2E5DE),
    track: Color(0xFFECEEE8),
    text: Color(0xFF15201B),
    muted: Color(0xFF56635C),
    hero: Color(0xFF15201B),
    heroText: Color(0xFFFFFFFF),
    heroMuted: Color(0xFFB9C4BD),
    heroTrack: Color(0xFF2C3B34),
    accent: Color(0xFF1F6B4F),
    onAccent: Color(0xFFFFFFFF),
    accentSoft: Color(0xFFE3EFE8),
    mint: Color(0xFF5FD39B),
    barMuted: Color(0xFFC9D3CC),
    warn: Color(0xFFB86A12),
    danger: Color(0xFFC2410C),
    positive: Color(0xFF1F6B4F),
  );

  static const pineDark = Palette(
    dark: true,
    bg: Color(0xFF0F1512),
    surface: Color(0xFF18211D),
    surfaceAlt: Color(0xFF202B26),
    border: Color(0xFF2A3631),
    track: Color(0xFF26312C),
    text: Color(0xFFE6ECE8),
    muted: Color(0xFF9AA8A0),
    hero: Color(0xFF1F6B4F),
    heroText: Color(0xFFFFFFFF),
    heroMuted: Color(0xFFCFE3D8),
    heroTrack: Color(0xFF17513C),
    accent: Color(0xFF4FBF8A),
    onAccent: Color(0xFF0B120F),
    accentSoft: Color(0xFF1C3329),
    mint: Color(0xFF7BE3B1),
    barMuted: Color(0xFF3A4741),
    warn: Color(0xFFE9A24A),
    danger: Color(0xFFF07A4E),
    positive: Color(0xFF6FD3A0),
  );

  /// The palette for a color theme (Pine is hand-tuned; the others are worked out from their hue).
  static Palette build(String themeName, bool dark) {
    final t = findColorTheme(themeName);
    final base = dark ? pineDark : pineLight;
    if (t.name == 'Pine') return base;
    final h = t.hue, s = t.sat;
    final tint = math.min(s, 30) / 30.0;
    if (!dark) {
      return base.copyWith(
        accent: hsl(h, s, s < 20 ? 34 : (h > 15 && h < 60 ? 36 : 38)),
        bg: hsl(h, 14 * tint, 95.5),
        surfaceAlt: hsl(h, 22 * tint + 6, 93),
        border: hsl(h, 12 * tint + 4, 88),
        track: hsl(h, 12 * tint + 4, 92),
        text: hsl(h, 25 * tint + 5, 10),
        muted: hsl(h, 8 * tint + 4, 36),
        hero: hsl(h, 28 * tint + 8, 11),
        heroMuted: hsl(h, 12 * tint + 4, 76),
        heroTrack: hsl(h, 20 * tint + 6, 23),
        accentSoft: hsl(h, math.min(55, s), 92),
        mint: hsl(h, math.min(75, s + 15), 68),
        barMuted: hsl(h, 14 * tint + 4, 80),
      );
    }
    return base.copyWith(
      accent: hsl(h, math.min(70, s + 5), s < 20 ? 68 : 62),
      bg: hsl(h, 16 * tint + 4, 7),
      surface: hsl(h, 14 * tint + 4, 11),
      surfaceAlt: hsl(h, 14 * tint + 4, 15),
      border: hsl(h, 12 * tint + 4, 19),
      track: hsl(h, 12 * tint + 4, 17),
      text: hsl(h, 14 * tint + 4, 92),
      muted: hsl(h, 8 * tint + 4, 64),
      hero: hsl(h, math.min(60, s), 26),
      heroMuted: hsl(h, 30 * tint + 6, 86),
      heroTrack: hsl(h, math.min(60, s), 19),
      onAccent: hsl(h, 20 * tint + 6, 7),
      accentSoft: hsl(h, 28 * tint + 8, 18),
      mint: hsl(h, math.min(75, s + 15), 74),
      barMuted: hsl(h, 10 * tint + 4, 28),
    );
  }

  /// The swatch shown in Settings.
  static Color swatch(ColorThemeDef t) => t.name == 'Pine' ? const Color(0xFF1F6B4F) : build(t.name, false).accent;

  @override
  Palette copyWith({
    Color? bg,
    Color? surface,
    Color? surfaceAlt,
    Color? border,
    Color? track,
    Color? text,
    Color? muted,
    Color? hero,
    Color? heroText,
    Color? heroMuted,
    Color? heroTrack,
    Color? accent,
    Color? onAccent,
    Color? accentSoft,
    Color? mint,
    Color? barMuted,
    Color? warn,
    Color? danger,
    Color? positive,
  }) =>
      Palette(
        dark: dark,
        bg: bg ?? this.bg,
        surface: surface ?? this.surface,
        surfaceAlt: surfaceAlt ?? this.surfaceAlt,
        border: border ?? this.border,
        track: track ?? this.track,
        text: text ?? this.text,
        muted: muted ?? this.muted,
        hero: hero ?? this.hero,
        heroText: heroText ?? this.heroText,
        heroMuted: heroMuted ?? this.heroMuted,
        heroTrack: heroTrack ?? this.heroTrack,
        accent: accent ?? this.accent,
        onAccent: onAccent ?? this.onAccent,
        accentSoft: accentSoft ?? this.accentSoft,
        mint: mint ?? this.mint,
        barMuted: barMuted ?? this.barMuted,
        warn: warn ?? this.warn,
        danger: danger ?? this.danger,
        positive: positive ?? this.positive,
      );

  @override
  Palette lerp(ThemeExtension<Palette>? other, double t) => t < 0.5 || other is! Palette ? this : other;
}

extension PaletteContext on BuildContext {
  Palette get pal => Theme.of(this).extension<Palette>()!;
}

const displayFont = 'Bricolage';

/// Headline / big-number text in Bricolage Grotesque.
TextStyle display(double size, Color color, {FontWeight weight = FontWeight.w700}) => TextStyle(
      fontFamily: displayFont,
      fontSize: size,
      fontWeight: weight,
      color: color,
      letterSpacing: -0.02 * size,
      height: 1.15,
    );

ThemeData buildTheme(Palette p) {
  final scheme = ColorScheme.fromSeed(
    seedColor: p.accent,
    brightness: p.dark ? Brightness.dark : Brightness.light,
  ).copyWith(
    primary: p.accent,
    onPrimary: p.onAccent,
    primaryContainer: p.accentSoft,
    onPrimaryContainer: p.text,
    secondaryContainer: p.accentSoft,
    onSecondaryContainer: p.text,
    surface: p.surface,
    onSurface: p.text,
    onSurfaceVariant: p.muted,
    surfaceContainerHighest: p.surfaceAlt,
    surfaceContainerHigh: p.surface,
    surfaceContainer: p.surface,
    surfaceContainerLow: p.surface,
    error: p.danger,
    outline: p.border,
    outlineVariant: p.border,
  );
  final radius14 = BorderRadius.circular(14);
  final base = ThemeData(
    useMaterial3: true,
    colorScheme: scheme,
    fontFamily: 'InstrumentSans',
    scaffoldBackgroundColor: p.bg,
    splashFactory: InkRipple.splashFactory,
    extensions: [p],
  );
  OutlineInputBorder outline(Color c, [double w = 1]) =>
      OutlineInputBorder(borderRadius: radius14, borderSide: BorderSide(color: c, width: w));
  return base.copyWith(
    textTheme: base.textTheme.apply(bodyColor: p.text, displayColor: p.text),
    dividerTheme: DividerThemeData(color: p.border, thickness: 1, space: 1),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: p.dark ? p.surfaceAlt : p.surface,
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
      border: outline(p.border),
      enabledBorder: outline(p.border),
      focusedBorder: outline(p.accent, 2),
      errorBorder: outline(p.danger),
      focusedErrorBorder: outline(p.danger, 2),
      hintStyle: TextStyle(color: p.muted.withValues(alpha: 0.8)),
      labelStyle: TextStyle(color: p.muted),
      floatingLabelStyle: TextStyle(color: p.accent, fontWeight: FontWeight.w600),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        backgroundColor: p.accent,
        foregroundColor: p.onAccent,
        minimumSize: const Size(0, 54),
        shape: RoundedRectangleBorder(borderRadius: radius14),
        textStyle: const TextStyle(fontFamily: 'InstrumentSans', fontSize: 16, fontWeight: FontWeight.w600),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        foregroundColor: p.text,
        minimumSize: const Size(0, 46),
        side: BorderSide(color: p.border),
        shape: RoundedRectangleBorder(borderRadius: radius14),
        textStyle: const TextStyle(fontFamily: 'InstrumentSans', fontSize: 14.5, fontWeight: FontWeight.w600),
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(
        foregroundColor: p.accent,
        textStyle: const TextStyle(fontFamily: 'InstrumentSans', fontSize: 14.5, fontWeight: FontWeight.w600),
      ),
    ),
    dialogTheme: DialogThemeData(
      backgroundColor: p.surface,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(26)),
      titleTextStyle: display(21, p.text),
      contentTextStyle: TextStyle(fontFamily: 'InstrumentSans', fontSize: 15, height: 1.4, color: p.muted),
    ),
    bottomSheetTheme: BottomSheetThemeData(
      backgroundColor: p.surface,
      surfaceTintColor: Colors.transparent,
      modalBackgroundColor: p.surface,
      showDragHandle: true,
      dragHandleColor: p.border,
      dragHandleSize: const Size(40, 4),
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(28))),
    ),
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      backgroundColor: p.dark ? p.text : p.hero,
      contentTextStyle: TextStyle(fontFamily: 'InstrumentSans', fontSize: 14.5, color: p.dark ? p.bg : p.heroText),
      actionTextColor: p.dark ? p.accent : p.mint,
      shape: RoundedRectangleBorder(borderRadius: radius14),
    ),
    datePickerTheme: DatePickerThemeData(
      backgroundColor: p.surface,
      surfaceTintColor: Colors.transparent,
      headerBackgroundColor: p.hero,
      headerForegroundColor: p.heroText,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(26)),
    ),
    progressIndicatorTheme: ProgressIndicatorThemeData(color: p.accent),
  );
}
