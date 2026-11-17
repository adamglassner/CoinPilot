import 'dart:async';

import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:timezone/data/latest_all.dart' as tzdata;
import 'package:timezone/timezone.dart' as tz;

import 'dates.dart';
import 'finance.dart';
import 'form.dart';
import 'insights.dart';
import 'money.dart';
import 'records.dart';
import 'store.dart';

/// Bill reminders as notifications, using the same settings as WillowWallet for Windows (Settings → Reminders: on or
/// off, and how many days before a due date; synced).
///
/// - Android schedules them ahead (at 9 AM on the reminder day and on the due day), so they arrive even when the app
///   is closed, and asks about paydays whose amount changes.
/// - Mac and Linux check when WillowWallet opens and every 30 minutes while it's open, like Windows, and show each
///   bill once a day.
class Reminders {
  static final _n = FlutterLocalNotificationsPlugin();
  static bool _ready = false;
  static Timer? _timer, _soon;

  /// Called with what was typed into "Spent anything today?" (empty when the notification was just tapped).
  static void Function(String input)? onQuickAdd;

  static Future<void> init() async {
    try {
      tzdata.initializeTimeZones();
      const settings = InitializationSettings(
        android: AndroidInitializationSettings('@mipmap/ic_launcher'),
        macOS: DarwinInitializationSettings(requestAlertPermission: false, requestBadgePermission: false, requestSoundPermission: false),
        linux: LinuxInitializationSettings(defaultActionName: 'Open WillowWallet'),
      );
      _ready = await _n.initialize(settings, onDidReceiveNotificationResponse: _tapped) ?? false;
    } catch (_) {
      _ready = false;
    }
  }

  static void _tapped(NotificationResponse r) {
    if (r.payload == quickAddPayload) onQuickAdd?.call(r.input ?? '');
  }

  /// What the app was opened with from "Spent anything today?" (typed text, or empty), else null.
  static Future<String?> launchQuickAdd() async {
    if (!_ready) return null;
    try {
      final d = await _n.getNotificationAppLaunchDetails();
      final r = d?.notificationResponse;
      if (d?.didNotificationLaunchApp != true || r?.payload != quickAddPayload) return null;
      return r!.input ?? '';
    } catch (_) {
      return null;
    }
  }

  static const quickAddPayload = 'quick-add';

  /// "12.50 coffee" or "Coffee 12.50" → (cents, payee).
  static (int?, String?) parseQuickAdd(String text) {
    final m = RegExp(r'\$?(\d+(?:[.,]\d{1,2})?)').firstMatch(text);
    final cents = m == null ? null : Money.parse(m.group(1)!.replaceAll(',', '.'));
    final rest = (m == null ? text : text.replaceRange(m.start, m.end, '')).trim();
    return (cents, rest.isEmpty ? null : rest[0].toUpperCase() + rest.substring(1));
  }

  /// The daily "Spent anything today?" notification (Android), at the hour chosen in Settings.
  static Future<void> _scheduleNudge(Store store) async {
    if (!store.settings.dailyNudge) return;
    final now = DateTime.now();
    var at = DateTime(now.year, now.month, now.day, store.settings.nudgeHour);
    if (!at.isAfter(now)) at = at.add(const Duration(days: 1));
    await _n.zonedSchedule(
      7000,
      'Spent anything today?',
      'Add it now so your budgets stay right. Type it here, like "12.50 coffee".',
      tz.TZDateTime.from(at, tz.UTC),
      NotificationDetails(
        android: AndroidNotificationDetails('quick-add', 'Daily check-in',
            channelDescription: 'A reminder each evening to add what you spent', importance: Importance.defaultImportance, actions: [
          AndroidNotificationAction('quick-add', 'Add', inputs: [AndroidNotificationActionInput(label: '12.50 coffee')], showsUserInterface: true),
        ]),
      ),
      androidScheduleMode: AndroidScheduleMode.inexactAllowWhileIdle,
      matchDateTimeComponents: DateTimeComponents.time,
      payload: quickAddPayload,
    );
  }

  /// Asks Android 13+ and macOS for permission to show notifications. True when allowed.
  static Future<bool> requestPermission() async {
    if (!_ready) return false;
    try {
      if (hostOs == HostOs.android) {
        return await _n.resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()?.requestNotificationsPermission() ?? true;
      }
      if (hostOs == HostOs.macos) {
        return await _n.resolvePlatformSpecificImplementation<MacOSFlutterLocalNotificationsPlugin>()?.requestPermissions(alert: true, sound: true) ?? false;
      }
      return true;
    } catch (_) {
      return false;
    }
  }

  /// Starts reminders: schedules them (Android) or checks now and every 30 minutes (Mac, Linux), and again after changes.
  static void start(Store store) {
    if (!_ready) return;
    store.addListener(() {
      _soon?.cancel();
      _soon = Timer(const Duration(seconds: 3), () => refresh(store));
    });
    refresh(store);
    if (isDesktop) {
      _timer?.cancel();
      _timer = Timer.periodic(const Duration(minutes: 30), (_) => refresh(store));
    }
  }

  static Future<void> refresh(Store store) async {
    if (!_ready) return;
    try {
      if (hostOs == HostOs.android) {
        await _schedule(store);
      } else {
        await _checkNow(store);
      }
      await _alerts(store);
    } catch (_) {
      // Notifications aren't worth crashing over.
    }
  }

  /// Spending alerts not sent yet: one notification for each (a few at most), on every platform.
  static Future<void> _alerts(Store store) async {
    final fresh = newAlerts(store, DateTime.now());
    if (fresh.isEmpty) return;
    var id = 500;
    for (final a in fresh.take(3)) {
      await _n.show(id++, a.title, a.body, _alertDetails);
    }
    store.updateSettings((s) {
      final keep = s.alertsNotified.length > 300 ? s.alertsNotified.sublist(s.alertsNotified.length - 200) : s.alertsNotified;
      s.alertsNotified = [...keep, for (final a in fresh) a.key];
    });
  }

  /// Alerts that haven't been sent as notifications on this device.
  static List<MoneyAlert> newAlerts(Store store, DateTime now) {
    final sent = store.settings.alertsNotified.toSet();
    return [for (final a in store.alerts(now)) if (!sent.contains(a.key)) a];
  }

  static NotificationDetails get _alertDetails => const NotificationDetails(
        android: AndroidNotificationDetails('alerts', 'Spending alerts',
            channelDescription: 'Budgets running low, bill price changes, unusual charges and forecast dips', importance: Importance.defaultImportance),
        macOS: DarwinNotificationDetails(),
        linux: LinuxNotificationDetails(),
      );

  /// Bills due within the reminder window (or overdue), not paid automatically.
  static List<Bill> due(Store store, DateTime today) {
    final p = store.prefs;
    if (!p.remindersOn) return [];
    return store.openBills.where((b) => !b.autoPay && dayOnly(b.nextDue).difference(dayOnly(today)).inDays <= p.reminderDays).toList();
  }

  static String titleFor(List<Bill> bills, DateTime today) =>
      bills.length == 1 ? '${bills.first.name} is ${Bills.statusText(bills.first, today).toLowerCase()}' : '${bills.length} bills need attention';

  static String bodyFor(List<Bill> bills, DateTime today) {
    final lines = [for (final b in bills.take(4)) '${b.name} · ${Money.format(b.amountCents)} · ${Bills.statusText(b, today)}'];
    if (bills.length > 4) lines.add('+ ${bills.length - 4} more');
    return lines.join('\n');
  }

  static NotificationDetails get _details => const NotificationDetails(
        android: AndroidNotificationDetails('bills', 'Bill reminders',
            channelDescription: 'Bills coming due, and paydays that need an amount', importance: Importance.defaultImportance),
        macOS: DarwinNotificationDetails(),
        linux: LinuxNotificationDetails(),
      );

  // ----- Android: scheduled ahead -----

  static Future<void> _schedule(Store store) async {
    await _n.cancelAllPendingNotifications(); // (alerts already shown stay)
    await _scheduleNudge(store);
    final p = store.prefs;
    if (!p.remindersOn) return;
    final now = DateTime.now();
    var id = 1;
    Future<void> at(DateTime day, String title, String body) async {
      final when = DateTime(day.year, day.month, day.day, 9);
      if (!when.isAfter(now) || id > 60) return;
      await _n.zonedSchedule(id++, title, body, tz.TZDateTime.from(when, tz.UTC), _details,
          androidScheduleMode: AndroidScheduleMode.inexactAllowWhileIdle);
    }

    final bills = store.openBills.where((b) => !b.autoPay && b.nextDue.isBefore(now.add(const Duration(days: 60)))).toList();
    for (final b in bills) {
      final due = dayOnly(b.nextDue);
      final early = due.subtract(Duration(days: p.reminderDays));
      if (p.reminderDays > 0) await at(early, '${b.name} is due ${p.reminderDays == 1 ? 'tomorrow' : 'in ${p.reminderDays} days'}', '${Money.format(b.amountCents)} due ${shortDate(due)}.');
      await at(due, '${b.name} is due today', '${Money.format(b.amountCents)}. Open WillowWallet to mark it paid.');
    }
    // Overdue or due now: today at 9 (or soon, if that's passed)
    final now9 = DateTime(now.year, now.month, now.day, 9);
    final dueNow = due(store, now).where((b) => !dayOnly(b.nextDue).isAfter(dayOnly(now))).toList();
    if (dueNow.isNotEmpty && now.isAfter(now9)) {
      final last = DateTime.tryParse(store.settings.lastReminderDay);
      if (last == null || !sameDay(last, now)) {
        await _n.show(9999, titleFor(dueNow, now), bodyFor(dueNow, now), _details);
        store.updateSettings((s) => s.lastReminderDay = now.toIso8601String());
      }
    }
    for (final s in store.incomeSources.where((s) => !s.isDone && s.needsAsking)) {
      await at(s.nextDate, 'Payday: ${s.name}', s.varies ? 'How much came in? Tap to add it.' : 'Did it come in? Tap to confirm.');
    }
  }

  // ----- Mac and Linux: like Windows, while the app is open -----

  static Future<void> _checkNow(Store store) async {
    final today = dayOnly(DateTime.now());
    final shown = store.settings.remindersShown;
    String stamp(Bill b) => '${b.id}|${isoDay(b.nextDue)}|${isoDay(today)}';
    final fresh = due(store, today).where((b) => !shown.contains(stamp(b))).toList();
    final asking = store.askingPaydays(today).where((s) => !shown.contains('income:${s.id}:${isoDay(today)}')).toList();
    if (fresh.isNotEmpty) await _n.show(1, titleFor(fresh, today), bodyFor(fresh, today), _details);
    if (asking.isNotEmpty) {
      await _n.show(2, asking.length == 1 ? 'Payday: ${asking.first.name}' : '${asking.length} paydays', 'Open WillowWallet to enter how much came in.', _details);
    }
    if (fresh.isEmpty && asking.isEmpty) return;
    store.updateSettings((s) {
      s.remindersShown = [
        ...s.remindersShown.where((x) => x.endsWith(isoDay(today))),
        for (final b in fresh) stamp(b),
        for (final i in asking) 'income:${i.id}:${isoDay(today)}',
      ];
    });
  }
}
