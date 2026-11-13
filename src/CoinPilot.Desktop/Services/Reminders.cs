using System.Windows;
using System.Windows.Threading;
using CoinPilot.Data;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CoinPilot.Services;

/// <summary>
/// Bill reminders and the notification-area (tray) icon.
///
/// While CoinPilot is open it checks every 30 minutes for bills due within the reminder window (Settings) and shows
/// a notification once per bill per day. With "Run in the background" on, CoinPilot starts when you sign in to
/// Windows, sits in the notification area, and closing the window keeps it running there.
/// </summary>
public static class Reminders
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "CoinPilot";

    private static Forms.NotifyIcon? _tray;
    private static DispatcherTimer? _timer;

    /// <summary>Set by Exit in the tray menu, so closing the window really quits.</summary>
    public static bool Exiting { get; private set; }

    public static bool BackgroundEnabled => App.Settings.RunInBackground;

    public static void Start()
    {
        if (SelfTest.IsRunning) return;
        UpdateTray();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
    }

    /// <summary>Turns "run in the background" on or off: the tray icon and starting with Windows.</summary>
    public static void SetBackground(bool on)
    {
        App.Settings.RunInBackground = on;
        App.Settings.Save();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Toaster.Show($"Couldn't change the Windows startup setting: {ex.Message}");
        }
        UpdateTray();
    }

    private static void UpdateTray()
    {
        if (!BackgroundEnabled)
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            return;
        }
        if (_tray != null) return;

        System.Drawing.Icon icon;
        using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/coinpilot.ico"))!.Stream)
            icon = new System.Drawing.Icon(stream, 32, 32);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open CoinPilot", null, (_, _) => ShowWindow());
        menu.Items.Add("Add transaction", null, (_, _) => { ShowWindow(); MainWindow.Instance?.QuickAdd(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new Forms.NotifyIcon { Icon = icon, Text = "CoinPilot", Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => ShowWindow();
        _tray.BalloonTipClicked += (_, _) => { ShowWindow(); MainWindow.Instance?.GoTo("bills"); };
    }

    public static void ShowWindow()
    {
        var w = MainWindow.Instance;
        if (w == null) return;
        if (!w.IsVisible) w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    public static void ExitApp()
    {
        Exiting = true;
        Stop();
        Application.Current.Shutdown();
    }

    /// <summary>Bills due within the reminder window (or overdue) that haven't been announced today.</summary>
    public static List<Bill> Due(DateTime today, bool onlyNew)
    {
        var prefs = DataStore.Prefs;
        if (!prefs.RemindersOn) return [];
        var shown = App.Settings.RemindersShown.ToHashSet();
        return DataStore.Bills.Items
            .Where(b => !b.IsDone && !b.AutoPay && (b.NextDue.Date - today).Days <= prefs.ReminderDays)
            .Where(b => !onlyNew || !shown.Contains(Stamp(b, today)))
            .OrderBy(b => b.NextDue).ToList();
    }

    private static string Stamp(Bill b, DateTime today) => $"{b.Id}|{b.NextDue:yyyy-MM-dd}|{today:yyyy-MM-dd}";

    /// <summary>Shows a notification for newly due bills (tray balloon when in the background, otherwise a toast).</summary>
    public static void Check()
    {
        if (SelfTest.IsRunning || !DataLock.IsOpen) return;
        var today = DateTime.Today;
        var due = Due(today, onlyNew: true);
        if (due.Count == 0) return;

        string title = due.Count == 1 ? $"{due[0].Name} is {BillSchedule.StatusText(due[0], today).ToLowerInvariant()}" : $"{due.Count} bills need attention";
        string body = string.Join("\n", due.Take(4).Select(b => $"{b.Name} · {Money.Format(b.Amount)} · {BillSchedule.StatusText(b, today)}"));
        if (due.Count > 4) body += $"\n+ {due.Count - 4} more";

        if (_tray != null) _tray.ShowBalloonTip(8000, title, body, Forms.ToolTipIcon.Info);
        if (MainWindow.Instance is { IsVisible: true }) Toaster.Show(due.Count == 1 ? $"Reminder: {title}." : $"Reminder: {title}. See Bills.");

        // Remember what was shown; keep the list short.
        var list = App.Settings.RemindersShown;
        list.AddRange(due.Select(b => Stamp(b, today)));
        list.RemoveAll(s => !s.EndsWith(today.ToString("yyyy-MM-dd")));
        App.Settings.Save();
    }
}
