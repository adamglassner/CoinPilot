const monthNames = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];
const shortMonths = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const shortDays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const longDays = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

DateTime dayOnly(DateTime d) => DateTime(d.year, d.month, d.day);
DateTime monthStart(DateTime d) => DateTime(d.year, d.month);
DateTime addMonths(DateTime month, int n) => DateTime(month.year, month.month + n);
bool sameMonth(DateTime a, DateTime b) => a.year == b.year && a.month == b.month;
bool sameDay(DateTime a, DateTime b) => a.year == b.year && a.month == b.month && a.day == b.day;
int daysInMonth(DateTime m) => DateTime(m.year, m.month + 1, 0).day;

/// "November 2026"
String monthLabel(DateTime m) => '${monthNames[m.month - 1]} ${m.year}';

/// "Nov 11" (with the year when it isn't this year)
String shortDate(DateTime d, {DateTime? now}) {
  now ??= DateTime.now();
  final s = '${shortMonths[d.month - 1]} ${d.day}';
  return d.year == now.year ? s : '$s, ${d.year}';
}

/// "Today", "Yesterday" or "Wed, Nov 11"
String dayLabel(DateTime d, {DateTime? now}) {
  now ??= DateTime.now();
  if (sameDay(d, now)) return 'Today';
  if (sameDay(d, now.subtract(const Duration(days: 1)))) return 'Yesterday';
  return '${shortDays[d.weekday - 1]}, ${shortDate(d, now: now)}';
}

/// Days left in this month, counting today.
int daysLeftInMonth(DateTime now) => daysInMonth(now) - now.day + 1;

String greeting(DateTime now) => now.hour < 12
    ? 'Good morning'
    : now.hour < 18
        ? 'Good afternoon'
        : 'Good evening';
