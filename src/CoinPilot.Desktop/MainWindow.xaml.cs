using System.Windows;
using System.Windows.Threading;
using CoinPilot.Models;
using CoinPilot.Services;
using CoinPilot.Views;

namespace CoinPilot;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.8) };

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;

        NavList.ItemsSource = ModuleCatalog.Sidebar;
        StatusText.Text = $"CoinPilot {App.VersionText} (preview) · Bytehawk Studio · Saved on this PC";

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.Visibility = Visibility.Collapsed;
        };

        App.ThemeChanged += OnThemeChanged;
        SourceInitialized += (_, _) => SystemTheme.ApplyTitleBar(this, App.IsDark);
        UpdateThemeButton();

        Loaded += async (_, _) =>
        {
            if (App.StartPage == "settings") ShowSettings();
            else GoTo(App.StartPage ?? ModuleCatalog.Dashboard.Key);

            if (SelfTest.IsRunning)
            {
                int failed = await SelfTest.RunAsync(this);
                Application.Current.Shutdown(failed);
            }
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
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
        var module = ModuleCatalog.Find(key) ?? ModuleCatalog.Dashboard;
        if (module.IsSelected) Show(module);
        else module.IsSelected = true; // fires Nav_Checked → Show
    }

    private void Show(ModuleInfo module) => SetPage(module.Key switch
    {
        "dashboard" => new DashboardView(),
        "transactions" => new TransactionsView(),
        "budgets" => new BudgetsView(),
        "accounts" => new AccountsView(),
        "debts" => new DebtsView(),
        "bills" => new BillsView(),
        "goals" => new GoalsView(),
        "reports" => new ReportsView(),
        _ => new ComingSoonView(module),
    });

    private void SetPage(object page)
    {
        PageHost.Content = page;
        PageScroller.ScrollToTop();
    }

    // ----- Sidebar buttons -----

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    public void ShowSettings()
    {
        foreach (var m in ModuleCatalog.Sidebar) m.IsSelected = false;
        SetPage(new SettingsView());
    }

    private void Theme_Click(object sender, RoutedEventArgs e) =>
        App.SetThemePreference(App.IsDark ? AppTheme.Light : AppTheme.Dark);

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

    public void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
