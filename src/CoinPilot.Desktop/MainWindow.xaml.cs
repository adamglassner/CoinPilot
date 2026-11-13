using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CoinPilot.Data;
using CoinPilot.Models;
using CoinPilot.Services;
using CoinPilot.Views;

namespace CoinPilot;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3.2) };
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private DateTime _lastActivity = DateTime.Now;
    private int _failedUnlocks;
    private string? _pendingPage;

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;

        NavList.ItemsSource = ModuleCatalog.Sidebar;
        StatusText.Text = $"CoinPilot {App.VersionText} · © 2026 Bytehawk Studio";

        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); HideToast(); };

        App.ThemeChanged += OnThemeChanged;
        DataStore.Changed += (_, _) => UpdateDataStatus();
        SourceInitialized += (_, _) => SystemTheme.ApplyTitleBar(this, App.IsDark);
        UpdateThemeButton();
        RestorePlacement();

        // Auto-lock after inactivity.
        InputManager.Current.PreProcessInput += (_, e) =>
        {
            if (e.StagingItem.Input is MouseButtonEventArgs or KeyEventArgs or MouseWheelEventArgs) _lastActivity = DateTime.Now;
        };
        _idleTimer.Tick += (_, _) => CheckIdle();
        _idleTimer.Start();

        Loaded += async (_, _) =>
        {
            _pendingPage = App.StartPage ?? ModuleCatalog.Dashboard.Key;
            if (DataLock.IsEnabled && !SelfTest.IsRunning) ShowLock();
            else OpenPending();

            if (Screenshots.IsRunning)
            {
                int failed = await Screenshots.RunAsync(this);
                Application.Current.Shutdown(failed);
            }
            else if (SelfTest.IsRunning)
            {
                int failed = await SelfTest.RunAsync(this);
                Application.Current.Shutdown(failed);
            }
            else
            {
                _ = Dispatcher.BeginInvoke(() => Reminders.Check(), DispatcherPriority.ApplicationIdle);
            }
        };
    }

    private void OpenPending()
    {
        string page = _pendingPage ?? ModuleCatalog.Dashboard.Key;
        _pendingPage = null;
        if (page == "settings") ShowSettings();
        else GoTo(page);
        UpdateDataStatus();
    }

    // ----- Navigation -----

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModuleInfo module })
            Show(module);
    }

    /// <summary>Selects a sidebar section by key ("dashboard", "budgets", …). Unknown keys open the Dashboard.</summary>
    public void GoTo(string key)
    {
        if (!DataLock.IsOpen) return;
        var module = ModuleCatalog.Find(key) ?? ModuleCatalog.Dashboard;
        if (module.IsSelected) Show(module);
        else module.IsSelected = true; // fires Nav_Checked → Show
    }

    private void Show(ModuleInfo module)
    {
        if (!DataLock.IsOpen) return;
        SetPage(module.Key switch
        {
            "dashboard" => new DashboardView(),
            "transactions" => new TransactionsView(),
            "budgets" => new BudgetsView(),
            "accounts" => new AccountsView(),
            "debts" => new DebtsView(),
            "bills" => new BillsView(),
            "goals" => new GoalsView(),
            "reports" => new ReportsView(),
            "taxes" => new TaxesView(),
            _ => new ComingSoonView(module),
        });
    }

    private void SetPage(FrameworkElement page)
    {
        PageHost.Content = page;
        PageScroller.ScrollToTop();
        if (SelfTest.IsRunning) return;

        // A short fade-and-rise so page changes feel smooth.
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        PageShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    public object? CurrentPage => PageHost.Content;

    // ----- Sidebar buttons -----

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    public void ShowSettings()
    {
        if (!DataLock.IsOpen) return;
        foreach (var m in ModuleCatalog.Sidebar) m.IsSelected = false;
        SetPage(new SettingsView());
    }

    private void Theme_Click(object sender, RoutedEventArgs e) =>
        App.SetThemePreference(App.IsDark ? AppTheme.Light : AppTheme.Dark);

    private void LockNow_Click(object sender, RoutedEventArgs e) => LockNow();

    /// <summary>Opens the add-transaction dialog from anywhere (Ctrl+N).</summary>
    public void QuickAdd()
    {
        if (!DataLock.IsOpen || OwnedWindows.Count > 0) return;
        var month = MonthSwitcher.Current;
        TransactionDialog.Edit(null, CurrentPage is TransactionsView && !Budgeting.InMonth(DateTime.Today, month) ? month : DateTime.Today);
    }

    // ----- Keyboard shortcuts -----

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.Control || !DataLock.IsOpen || LockOverlay.Visibility == Visibility.Visible) return;

        switch (e.Key)
        {
            case Key.N:
                e.Handled = true;
                QuickAdd();
                break;
            case Key.F:
                if (CurrentPage is ISearchablePage searchable)
                {
                    e.Handled = true;
                    searchable.FocusSearch();
                }
                break;
            case Key.L when DataLock.IsEnabled:
                e.Handled = true;
                LockNow();
                break;
            case Key.OemComma:
                e.Handled = true;
                ShowSettings();
                break;
            case >= Key.D1 and <= Key.D9:
                int index = e.Key - Key.D1;
                if (index < ModuleCatalog.Sidebar.Count)
                {
                    e.Handled = true;
                    GoTo(ModuleCatalog.Sidebar[index].Key);
                }
                break;
        }
    }

    // ----- Lock screen -----

    private void CheckIdle()
    {
        if (!DataLock.IsEnabled || !DataLock.IsOpen || SelfTest.IsRunning) return;
        int minutes = DataLock.Info?.AutoLockMinutes ?? 0;
        if (minutes <= 0 || OwnedWindows.Count > 0) return;
        if (DateTime.Now - _lastActivity >= TimeSpan.FromMinutes(minutes)) LockNow();
    }

    public void LockNow()
    {
        if (!DataLock.IsEnabled) return;
        _pendingPage = (ModuleCatalog.Sidebar.FirstOrDefault(m => m.IsSelected)?.Key) ?? "dashboard";
        foreach (var w in OwnedWindows.Cast<Window>().ToList()) w.Close();
        PageHost.Content = null;
        DataLock.Lock();
        ShowLock();
    }

    /// <summary>Shows the lock screen (at start-up with protection on, after locking, or after restoring a protected backup).</summary>
    public void ShowLock()
    {
        _pendingPage ??= "dashboard";
        PageHost.Content = null;
        var info = DataLock.Info;
        bool pin = info?.Kind == "PIN";
        LockPrompt.Text = pin ? "Enter your PIN to open your data." : "Enter your password to open your data.";
        SetRecoveryMode(false);
        LockError.Visibility = Visibility.Collapsed;
        LockOverlay.Visibility = Visibility.Visible;
        LockPassword.Clear();
        Dispatcher.BeginInvoke(() => LockPassword.Focus(), DispatcherPriority.Input);
    }

    private bool _recoveryMode;

    private void SetRecoveryMode(bool on)
    {
        _recoveryMode = on;
        LockPassword.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        RecoveryBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        RecoveryToggle.Content = on ? "Use your PIN or password instead" : "Forgot it? Use your recovery key";
        if (on) LockPrompt.Text = "Enter the recovery key you saved when you turned on protection.";
        else LockPrompt.Text = DataLock.Info?.Kind == "PIN" ? "Enter your PIN to open your data." : "Enter your password to open your data.";
        LockError.Visibility = Visibility.Collapsed;
        Dispatcher.BeginInvoke(() => { if (on) RecoveryBox.Focus(); else LockPassword.Focus(); }, DispatcherPriority.Input);
    }

    private void RecoveryToggle_Click(object sender, RoutedEventArgs e) => SetRecoveryMode(!_recoveryMode);

    private void LockPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Unlock_Click(sender, e);
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        UnlockButton.IsEnabled = false;
        UnlockButton.Content = "Unlocking…";
        string secret = _recoveryMode ? RecoveryBox.Text : LockPassword.Password;
        bool recovery = _recoveryMode;
        // Key derivation takes a moment; keep the window responsive.
        bool ok = await Task.Run(() => recovery ? DataLock.UnlockWithRecoveryKey(secret) : DataLock.Unlock(secret));
        UnlockButton.IsEnabled = true;
        UnlockButton.Content = "Unlock";

        if (!ok)
        {
            _failedUnlocks++;
            LockError.Text = recovery ? "That recovery key doesn't match." : "That's not right. Try again.";
            if (_failedUnlocks >= 5)
            {
                LockError.Text += " Please wait a few seconds.";
                UnlockButton.IsEnabled = false;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 3 * (_failedUnlocks - 4))));
                UnlockButton.IsEnabled = true;
            }
            LockError.Visibility = Visibility.Visible;
            LockPassword.Clear();
            LockPassword.Focus();
            return;
        }

        CompleteUnlock();
        if (recovery) ShowToast("Unlocked with your recovery key. You can set a new PIN or password in Settings → Security.");
        Reminders.Check();
    }

    /// <summary>After a successful unlock: reload the data and open the page you were on.</summary>
    public void CompleteUnlock()
    {
        _failedUnlocks = 0;
        _lastActivity = DateTime.Now;
        LockPassword.Clear();
        RecoveryBox.Clear();
        DataStore.Reload();
        LockOverlay.Visibility = Visibility.Collapsed;
        OpenPending();
    }

    public bool IsLockScreenShown => LockOverlay.Visibility == Visibility.Visible;

    // ----- Data status (sidebar card + status bar) -----

    private void UpdateDataStatus()
    {
        bool protectedOn = DataLock.IsEnabled;
        DataGlyph.Text = protectedOn ? "" : "";
        DataTitle.Text = protectedOn ? "Protected" : "Saved on this PC";
        DataDetail.Text = protectedOn
            ? "Your data is encrypted with your " + (DataLock.Info?.Kind == "PIN" ? "PIN." : "password.")
            : "Add a PIN or password in Settings → Security.";
        LockButton.Visibility = protectedOn ? Visibility.Visible : Visibility.Collapsed;
        StatusRight.Text = protectedOn ? "Encrypted · saved on this PC" : "All changes saved on this PC";
    }

    // ----- Theme -----

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateThemeButton();
        SystemTheme.ApplyTitleBar(this, App.IsDark);
    }

    private void UpdateThemeButton()
    {
        // Sun when dark (click for light), moon when light (click for dark).
        ThemeGlyph.Text = App.IsDark ? "" : "";
        ThemeLabel.Text = App.IsDark ? "Light mode" : "Dark mode";
        ThemeButton.ToolTip = App.IsDark ? "Switch to light mode" : "Switch to dark mode";
    }

    // ----- Toast -----

    private System.Windows.Shapes.Rectangle? _dim;
    private int _dimCount;

    /// <summary>Darkens the window behind an open dialog (nested dialogs keep it dark until the last one closes).</summary>
    public void SetDimmed(bool on)
    {
        _dimCount = Math.Max(0, _dimCount + (on ? 1 : -1));
        if (_dim == null)
        {
            _dim = new System.Windows.Shapes.Rectangle { Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 8, 14, 12)), Opacity = 0, IsHitTestVisible = false };
            Grid.SetColumnSpan(_dim, 2);
            ((Grid)Content).Children.Add(_dim);
        }
        double target = _dimCount > 0 ? (App.IsDark ? 0.55 : 0.38) : 0;
        if (SelfTest.IsRunning) { _dim.BeginAnimation(OpacityProperty, null); _dim.Opacity = target; return; }
        _dim.BeginAnimation(OpacityProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(170)));
    }

    public void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        ToastShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => { if (Toast.Opacity < 0.05) Toast.Visibility = Visibility.Collapsed; };
        Toast.BeginAnimation(OpacityProperty, fade);
    }

    // ----- Window placement and closing -----

    private void RestorePlacement()
    {
        var s = App.Settings;
        if (s.WindowWidth is double w && s.WindowHeight is double h && s.WindowLeft is double l && s.WindowTop is double t)
        {
            // Only restore if it's still on a visible part of the desktop.
            var area = SystemParameters.VirtualScreenWidth;
            if (l > SystemParameters.VirtualScreenLeft - 100 && l < SystemParameters.VirtualScreenLeft + area - 200
                && t > SystemParameters.VirtualScreenTop - 50 && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = l;
                Top = t;
                Width = Math.Max(MinWidth, w);
                Height = Math.Max(MinHeight, h);
            }
        }
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var s = App.Settings;
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (r.Width > 0 && r.Height > 0)
        {
            s.WindowLeft = r.Left;
            s.WindowTop = r.Top;
            s.WindowWidth = r.Width;
            s.WindowHeight = r.Height;
        }
        s.WindowMaximized = WindowState == WindowState.Maximized;
        s.Save();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SavePlacement();
        if (Reminders.BackgroundEnabled && !Reminders.Exiting && !SelfTest.IsRunning)
        {
            // Keep running in the notification area for bill reminders.
            e.Cancel = true;
            Hide();
            if (DataLock.IsEnabled) LockNow();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
        if (!SelfTest.IsRunning) Application.Current.Shutdown();
    }
}

/// <summary>A page with a search box that Ctrl+F can focus.</summary>
public interface ISearchablePage
{
    void FocusSearch();
}
