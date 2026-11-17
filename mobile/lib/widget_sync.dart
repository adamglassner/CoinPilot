import 'dart:async';

import 'package:home_widget/home_widget.dart';

import 'forecast.dart';
import 'form.dart';
import 'money.dart';
import 'store.dart';

/// Keeps the Android home screen widget up to date: safe to spend and what's left this month.
/// With a PIN or password on, it shows no amounts.
class HomeScreenWidget {
  static Timer? _soon;
  static const provider = 'com.bytehawkstudio.coinpilot.SpendWidget';

  static void start(Store store) {
    if (hostOs != HostOs.android) return;
    store.addListener(() {
      _soon?.cancel();
      _soon = Timer(const Duration(seconds: 2), () => update(store));
    });
    update(store);
  }

  /// What the widget shows (label, big number, line under it).
  static (String, String, String) content(Store store, DateTime now) {
    if (store.settings.isLocked) return ('WillowWallet', 'Locked', 'Open to see your numbers');
    if (store.isEmpty) return ('WillowWallet', '—', 'Open WillowWallet to set up');
    final month = store.budgetMonth(now);
    final left = store.leftToSpend(month);
    final f = store.forecast(now);
    return (
      'Safe to spend',
      Money.format(f.safeToSpend, whole: true),
      store.budgets.isEmpty ? 'Forecast to ${f.end.month}/${f.end.day}' : '${Money.format(left, whole: true)} left in budgets',
    );
  }

  static Future<void> update(Store store) async {
    try {
      final (label, big, line) = content(store, DateTime.now());
      await HomeWidget.saveWidgetData<String>('safeLabel', label);
      await HomeWidget.saveWidgetData<String>('safe', big);
      await HomeWidget.saveWidgetData<String>('left', line);
      await HomeWidget.updateWidget(qualifiedAndroidName: provider);
    } catch (_) {
      // No widget on the home screen, or not Android: nothing to do.
    }
  }

  /// "add" when the + on the widget opened the app (now or earlier), else null.
  static Future<String?> launchedWith() async {
    if (hostOs != HostOs.android) return null;
    try {
      final uri = await HomeWidget.initiallyLaunchedFromHomeWidget();
      return uri?.host;
    } catch (_) {
      return null;
    }
  }

  static Stream<String?> clicks() => hostOs != HostOs.android ? const Stream.empty() : HomeWidget.widgetClicked.map((u) => u?.host);
}
