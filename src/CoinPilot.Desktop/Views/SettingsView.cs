using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>Settings: appearance and color theme, money and budget month, categories, reminders, security, data, shortcuts.</summary>
public sealed class SettingsView : DataPage
{
    protected override void Build()
    {
        Add(PageKit.Header("", "Settings", "Make CoinPilot work the way you like."));

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel();
        var right = new StackPanel().At(2);
        columns.Children.Add(left);
        columns.Children.Add(right);

        left.Children.Add(Appearance());
        left.Children.Add(MoneySettings());
        left.Children.Add(CategoriesCard());
        left.Children.Add(Reminders());
        right.Children.Add(Security());
        right.Children.Add(DataCard());
        right.Children.Add(Shortcuts());
        right.Children.Add(SyncCard());
        right.Children.Add(About());
        Add(columns);
    }

    private static TextBlock Muted(string text, double top = 0) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 10),
        Style = (Style)Application.Current.FindResource("Muted"),
    };

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6) };

    private static WrapPanel Buttons(params Button[] buttons)
    {
        var wrap = new WrapPanel();
        foreach (var b in buttons)
        {
            b.Margin = new Thickness(0, 0, 8, 8);
            wrap.Children.Add(b);
        }
        return wrap;
    }

    private Button Ghost(string text, Action onClick)
    {
        var b = new Button { Content = text, Style = Res("GhostButton") };
        b.Click += (_, _) => onClick();
        return b;
    }

    private Button Primary(string text, Action onClick)
    {
        var b = new Button { Content = text, Style = Res("PrimaryButton") };
        b.Click += (_, _) => onClick();
        return b;
    }

    // ----- Appearance -----

    private Border Appearance()
    {
        var panel = new StackPanel();
        panel.Children.Add(Label("Mode"));
        string group = "mode";
        foreach (var (mode, text) in new[] { (AppTheme.System, "Match Windows (light or dark)"), (AppTheme.Light, "Light"), (AppTheme.Dark, "Dark") })
        {
            var r = new RadioButton { Content = text, GroupName = group, IsChecked = App.Settings.Theme == mode };
            r.Checked += (_, _) => App.SetThemePreference(mode);
            panel.Children.Add(r);
        }

        panel.Children.Add(Label("Color theme").Also(l => l.Margin = new Thickness(0, 16, 0, 8)));
        var swatches = new WrapPanel();
        foreach (var theme in ThemePalettes.All)
        {
            var brush = new SolidColorBrush(ThemePalettes.Swatch(theme));
            brush.Freeze();
            var r = new RadioButton
            {
                Content = theme.Name, Tag = brush, GroupName = "color", Style = Res("SwatchRadio"),
                IsChecked = ThemePalettes.Find(App.Settings.ColorTheme).Name == theme.Name, ToolTip = theme.Description,
            };
            r.Checked += (_, _) => App.SetColorTheme(theme.Name);
            swatches.Children.Add(r);
        }
        panel.Children.Add(swatches);
        return PageKit.Card(panel, "Appearance");
    }

    // ----- Money -----

    private Border MoneySettings()
    {
        var prefs = DataStore.Prefs;
        var panel = new StackPanel();

        panel.Children.Add(Label("Currency"));
        var currency = new ComboBox { Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var c in Money.Currencies) currency.Items.Add(new ComboBoxItem { Content = c.Label, Tag = c.Culture });
        currency.SelectedItem = currency.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == prefs.Currency) ?? currency.Items[0];
        currency.SelectionChanged += (_, _) =>
        {
            if (currency.SelectedItem is ComboBoxItem { Tag: string culture } && culture != prefs.Currency)
            {
                prefs.Currency = culture;
                prefs.Save();
            }
        };
        System.Windows.Automation.AutomationProperties.SetName(currency, "Currency");
        panel.Children.Add(currency);
        panel.Children.Add(Muted($"Example: {Money.Format(1284.5m)}", 6));

        panel.Children.Add(Label("Budget months start on"));
        var start = new ComboBox { Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
        for (int d = 1; d <= 28; d++)
            start.Items.Add(new ComboBoxItem { Content = d == 1 ? "The 1st (calendar months)" : $"The {Ordinal(d)} of each month", Tag = d });
        start.SelectedIndex = Math.Clamp(prefs.MonthStartDay, 1, 28) - 1;
        start.SelectionChanged += (_, _) =>
        {
            int day = (int)((ComboBoxItem)start.SelectedItem).Tag;
            if (day == prefs.MonthStartDay) return;
            prefs.MonthStartDay = day;
            MonthSwitcher.Current = Budgeting.MonthStart(DateTime.Today);
            prefs.Save();
        };
        System.Windows.Automation.AutomationProperties.SetName(start, "Budget months start on");
        panel.Children.Add(start);
        panel.Children.Add(Muted($"Paid on the 15th? Start budget months on your payday. This budget month: {Money.MonthName(DateTime.Today)}.", 6));
        return PageKit.Card(panel, "Money");
    }

    private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    // ----- Categories -----

    private Border CategoriesCard()
    {
        var panel = new StackPanel();
        int spending = Categories.Spending.Count, income = Categories.Income.Count, rules = DataStore.Rules.Items.Count;
        panel.Children.Add(Muted($"{spending} spending and {income} income categories · {rules} rule{(rules == 1 ? "" : "s")}. " +
                                 "Add your own, make subcategories, and set rules that categorize payees automatically."));
        panel.Children.Add(Buttons(Ghost("Edit categories and rules…", () => new CategoriesDialog().ShowDialog())));
        return PageKit.Card(panel, "Categories");
    }

    // ----- Reminders -----

    private Border Reminders()
    {
        var prefs = DataStore.Prefs;
        var panel = new StackPanel();
        var on = new CheckBox { Content = "Remind me about bills that are due soon", IsChecked = prefs.RemindersOn };
        on.Checked += (_, _) => { prefs.RemindersOn = true; prefs.Save(); };
        on.Unchecked += (_, _) => { prefs.RemindersOn = false; prefs.Save(); };
        panel.Children.Add(on);

        var days = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(27, 6, 0, 10) };
        days.Children.Add(Ui.Text("How far ahead", muted: true).Also(t => t.Margin = new Thickness(0, 0, 10, 0)));
        var combo = new ComboBox { Width = 150 };
        foreach (int d in new[] { 1, 2, 3, 5, 7, 10, 14 })
            combo.Items.Add(new ComboBoxItem { Content = d == 1 ? "1 day before" : $"{d} days before", Tag = d });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == prefs.ReminderDays) ?? combo.Items[2];
        combo.SelectionChanged += (_, _) => { prefs.ReminderDays = (int)((ComboBoxItem)combo.SelectedItem).Tag; prefs.Save(); };
        System.Windows.Automation.AutomationProperties.SetName(combo, "Remind how many days before");
        days.Children.Add(combo);
        panel.Children.Add(days);

        var background = new CheckBox { Content = "Run in the background so reminders arrive even when CoinPilot is closed", IsChecked = App.Settings.RunInBackground };
        background.Checked += (_, _) => Services.Reminders.SetBackground(true);
        background.Unchecked += (_, _) => Services.Reminders.SetBackground(false);
        panel.Children.Add(background);
        panel.Children.Add(Muted("CoinPilot starts when you sign in to Windows and waits in the notification area. Closing the window keeps it there; right-click its icon to exit. Bills set to auto-pay don't get reminders.", 6));
        return PageKit.Card(panel, "Bill reminders");
    }

    // ----- Security -----

    private Border Security()
    {
        var panel = new StackPanel();
        if (!DataLock.IsEnabled)
        {
            panel.Children.Add(Muted("Protect your data with a PIN or password. Everything CoinPilot saves (transactions, accounts, receipts and tax documents) is encrypted, and you unlock it when you open CoinPilot."));
            panel.Children.Add(Buttons(Primary("Turn on PIN or password…", TurnOn)));
            return PageKit.Card(panel, "Security", PageKit.Pill("Off", "Brush.TextMuted"));
        }

        var info = DataLock.Info!;
        panel.Children.Add(Muted($"Your data is encrypted and protected with a {(info.Kind == "PIN" ? "PIN" : "password")}. Keep your recovery key safe: it's the only way in if you forget it."));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(Ui.Text("Lock automatically after", muted: true).Also(t => t.Margin = new Thickness(0, 0, 10, 0)));
        var auto = new ComboBox { Width = 150 };
        foreach (var (minutes, label) in SetLockDialog.AutoLockChoices) auto.Items.Add(new ComboBoxItem { Content = label, Tag = minutes });
        auto.SelectedItem = auto.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == info.AutoLockMinutes) ?? auto.Items[2];
        auto.SelectionChanged += (_, _) => DataLock.SetAutoLock((int)((ComboBoxItem)auto.SelectedItem).Tag);
        System.Windows.Automation.AutomationProperties.SetName(auto, "Lock automatically after");
        row.Children.Add(auto);
        panel.Children.Add(row);
        panel.Children.Add(Buttons(
            Ghost($"Change {(info.Kind == "PIN" ? "PIN" : "password")}…", () =>
            {
                if (new SetLockDialog(change: true).ShowDialog() == true) Toaster.Show("Changed. Use the new one next time you unlock.");
            }),
            Ghost("Lock now", () => MainWindow.Instance?.LockNow()),
            Ghost("Turn off…", TurnOff)));
        return PageKit.Card(panel, "Security", PageKit.Pill("On", "Brush.Positive"));
    }

    private void TurnOn()
    {
        var dialog = new SetLockDialog(change: false);
        if (dialog.ShowDialog() != true || dialog.RecoveryKey == null) return;
        new RecoveryKeyDialog(dialog.RecoveryKey).ShowDialog();
        Toaster.Show("Protection is on. Your data is encrypted.");
        DataStore.RaiseChanged();
    }

    private void TurnOff()
    {
        if (new VerifySecretDialog("Turn off protection", "Your data will be saved without encryption, and CoinPilot won't ask for a PIN or password.", "Turn off").ShowDialog() != true) return;
        try
        {
            DataLock.Disable();
            Toaster.Show("Protection is off.");
        }
        catch (Exception ex)
        {
            Toaster.Show($"Couldn't turn it off: {ex.Message}");
        }
        DataStore.RaiseChanged();
    }

    // ----- Data -----

    private Border DataCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(Muted("Everything is saved on this PC, in this folder:"));
        panel.Children.Add(new TextBox
        {
            Text = DataStore.Folder, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, -4, 0, 12),
        });
        panel.Children.Add(Buttons(
            Ghost("Back up…", BackUp),
            Ghost("Restore…", Restore),
            Ghost("Open folder", OpenFolder)));
        panel.Children.Add(Buttons(
            Ghost("Import a bank statement…", () => ImportDialog.Run()),
            Ghost("Export all transactions (CSV)…", () => CsvExporter.Export("coinpilot-all-transactions", Backup.AllTransactionHeaders, Backup.AllTransactionRows()))));
        panel.Children.Add(Muted("A backup is one .zip with all your data, receipts and tax documents. With protection on, it stays encrypted.", 2));
        return PageKit.Card(panel, "Your data");
    }

    private static void BackUp()
    {
        var save = new Microsoft.Win32.SaveFileDialog { FileName = Backup.SuggestedName, Filter = "CoinPilot backup (*.zip)|*.zip", Title = "Back up CoinPilot" };
        if (save.ShowDialog() != true) return;
        try
        {
            Backup.Create(save.FileName);
            Toaster.Show($"Backed up to {Path.GetFileName(save.FileName)}.");
        }
        catch (Exception ex)
        {
            Toaster.Show($"Couldn't back up: {ex.Message}");
        }
    }

    private static void Restore()
    {
        var open = new Microsoft.Win32.OpenFileDialog { Filter = "CoinPilot backup (*.zip)|*.zip", Title = "Restore a backup" };
        if (open.ShowDialog() != true) return;
        if (Backup.Check(open.FileName) is string problem)
        {
            Toaster.Show(problem);
            return;
        }
        if (!ConfirmDialog.Ask("Restore this backup?",
                "Your current data will be replaced with the backup. (A copy of the current data is kept in a \"data before restore\" folder, just in case.)",
                "Restore", danger: true)) return;
        try
        {
            string kept = Backup.Restore(open.FileName);
            if (DataLock.IsEnabled)
            {
                MainWindow.Instance?.ShowLock();
                Toaster.Show("Backup restored. It's protected: unlock it with the PIN or password it was made with.");
            }
            else
            {
                MainWindow.Instance?.GoTo("dashboard");
                Toaster.Show("Backup restored.");
            }
        }
        catch (Exception ex)
        {
            Toaster.Show($"Couldn't restore: {ex.Message}");
        }
    }

    private static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(DataStore.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataStore.Folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toaster.Show($"Couldn't open the folder: {ex.Message}");
        }
    }

    // ----- Shortcuts, sync, about -----

    private static Border Shortcuts()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        (string Keys, string What)[] items =
        [
            ("Ctrl+N", "Add a transaction"), ("Ctrl+F", "Search transactions"), ("Ctrl+1 … 9", "Go to a section"),
            ("Ctrl+,", "Settings"), ("Ctrl+L", "Lock (when protection is on)"), ("Ctrl+Enter", "Save in an add/edit window"), ("Esc", "Close a window"),
        ];
        for (int i = 0; i < items.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var key = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 3), Margin = new Thickness(0, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(1) };
            key.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            key.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            key.Child = Ui.Text(items[i].Keys, size: 12, weight: FontWeights.SemiBold);
            Grid.SetRow(key, i);
            grid.Children.Add(key);
            var what = Ui.Text(items[i].What, size: 13).At(1);
            Grid.SetRow(what, i);
            grid.Children.Add(what);
        }
        return PageKit.Card(grid, "Keyboard shortcuts");
    }

    private static Border SyncCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(Muted("Sign in to keep your budgets, transactions and goals in sync between this PC and the CoinPilot app for iPhone and Android."));
        return PageKit.Card(panel, "Sync & mobile app", PageKit.Pill("Coming soon", "Brush.Accent"));
    }

    private static Border About()
    {
        var panel = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/coinpilot.png")),
            Width = 48, Height = 48, Margin = new Thickness(0, 0, 14, 0),
        }.Also(i => RenderOptions.SetBitmapScalingMode(i, BitmapScalingMode.HighQuality)));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text($"CoinPilot {App.VersionText}", size: 16, weight: FontWeights.SemiBold));
        text.Children.Add(Ui.Text("© 2026 Bytehawk Studio", muted: true));
        row.Children.Add(text);
        panel.Children.Add(row);
        panel.Children.Add(Muted("Updates arrive through the Bytehawk Studio Launcher. Free and open source (MIT).", 12));
        return PageKit.Card(panel, "About");
    }
}
