using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>
/// Get started: the full-window setup that opens the first time WillowWallet runs with no data (and again from
/// Settings or the Dashboard). Welcome → accounts → income and budgets → bills → debts → protection → done.
/// Every step can be skipped, and everything it creates is ordinary data that can be changed later.
/// </summary>
public sealed class GetStarted : UserControl
{
    private enum Step { Welcome, Accounts, Income, Bills, Debts, Protect, Done }

    private static readonly (Step Step, string Title, string Glyph)[] Steps =
    [
        (Step.Welcome, "Welcome", ""),
        (Step.Accounts, "Your accounts", ""),
        (Step.Income, "Income and budgets", ""),
        (Step.Bills, "Regular bills", ""),
        (Step.Debts, "Loans and cards", ""),
        (Step.Protect, "Keep it private", ""),
        (Step.Done, "All set", ""),
    ];

    private readonly Action _close;
    private Step _step = Step.Welcome;
    private Step _furthest = Step.Welcome;

    private readonly StackPanel _rail = new();
    private readonly StackPanel _body = new() { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ScrollViewer _scroller;
    private readonly DockPanel _footer = new();
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };

    // Kept between steps so Back shows what was entered
    private string _income = "";
    private string _budgetMethod = BudgetTemplateDialog.FiftyThirtyTwenty;
    private readonly List<BillRow> _billRows = [];
    private readonly HashSet<string> _createdBudgets = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="close">Hides Get started (the main window keeps it in an overlay).</param>
    public GetStarted(Action close)
    {
        _close = close;
        AutomationProperties.SetName(this, "Get started");
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        // ----- Left rail: logo and the steps -----
        var rail = new Border { Padding = new Thickness(28, 28, 22, 22) };
        rail.SetResourceReference(Border.BackgroundProperty, "Brush.Sidebar");
        var railDock = new DockPanel();
        var logo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 30) };
        logo.Children.Add(new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/coinpilot.png")),
            Width = 34, Height = 34, Margin = new Thickness(0, 0, 10, 0),
        }.Also(i => RenderOptions.SetBitmapScalingMode(i, BitmapScalingMode.HighQuality)));
        var name = new TextBlock { Text = "WillowWallet", FontSize = 22, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.SidebarText");
        logo.Children.Add(name);
        DockPanel.SetDock(logo, Dock.Top);
        railDock.Children.Add(logo);

        var skip = new Button { Style = (Style)FindResource("SidebarButton"), Content = "Skip setup for now", HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(skip, "Skip setup for now");
        skip.Click += (_, _) => Finish(goTo: null);
        DockPanel.SetDock(skip, Dock.Bottom);
        railDock.Children.Add(skip);
        var skipNote = new TextBlock
        {
            Text = "You can come back to this any time from Settings → Your data.", FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(14, 0, 0, 4), LineHeight = 17,
        };
        skipNote.SetResourceReference(TextBlock.ForegroundProperty, "Brush.SidebarMuted");
        DockPanel.SetDock(skipNote, Dock.Bottom);
        railDock.Children.Add(skipNote);
        railDock.Children.Add(_rail);
        rail.Child = railDock;
        root.Children.Add(rail);

        // ----- Right: the step, with Back / Next at the bottom -----
        var right = new DockPanel();
        right.SetResourceReference(Panel.BackgroundProperty, "Brush.Bg");
        var footerBar = new Border { Padding = new Thickness(56, 14, 56, 14), BorderThickness = new Thickness(0, 1, 0, 0) };
        footerBar.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        footerBar.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        footerBar.Child = _footer;
        DockPanel.SetDock(footerBar, Dock.Bottom);
        right.Children.Add(footerBar);
        _scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(56, 48, 56, 36), Focusable = false, Content = _body,
        };
        right.Children.Add(_scroller);
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        _error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        Content = root;
        Show(Step.Welcome);
    }

    // =====================================================================
    // Navigation
    // =====================================================================

    private void Show(Step step)
    {
        _step = step;
        if (step > _furthest) _furthest = step;
        _error.Visibility = Visibility.Collapsed;
        BuildRail();
        _body.Children.Clear();
        _footer.Children.Clear();
        switch (step)
        {
            case Step.Welcome: BuildWelcome(); break;
            case Step.Accounts: BuildAccounts(); break;
            case Step.Income: BuildIncome(); break;
            case Step.Bills: BuildBills(); break;
            case Step.Debts: BuildDebts(); break;
            case Step.Protect: BuildProtect(); break;
            case Step.Done: BuildDone(); break;
        }
        _scroller.ScrollToTop();
    }

    private void BuildRail()
    {
        _rail.Children.Clear();
        var heading = new TextBlock { Text = "GET STARTED", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 0, 0, 10) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Brush.SidebarMuted");
        _rail.Children.Add(heading);
        for (int i = 0; i < Steps.Length; i++)
        {
            var (step, title, _) = Steps[i];
            bool current = step == _step, done = step < _step || (step < _furthest && step != _step);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 12, 0) };
            var mark = new TextBlock
            {
                Text = done ? "" : (i + 1).ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                FontSize = done ? 12 : 12.5, FontWeight = FontWeights.SemiBold,
            };
            if (done) mark.Style = (Style)FindResource("Icon");
            if (current)
            {
                dot.SetResourceReference(Border.BackgroundProperty, "Brush.Mint");
                mark.Foreground = new SolidColorBrush(Color.FromRgb(0x0D, 0x22, 0x19));
            }
            else
            {
                dot.SetResourceReference(Border.BackgroundProperty, "Brush.SidebarCard");
                mark.SetResourceReference(TextBlock.ForegroundProperty, done ? "Brush.Mint" : "Brush.SidebarMuted");
            }
            dot.Child = mark;
            row.Children.Add(dot);
            var label = new TextBlock { Text = title, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal };
            label.SetResourceReference(TextBlock.ForegroundProperty, current ? "Brush.SidebarText" : "Brush.SidebarMuted");
            row.Children.Add(label);

            // Steps already reached can be revisited
            var button = new Button { Content = row, Style = (Style)FindResource("SidebarButton"), Padding = new Thickness(12, 8, 12, 8), IsEnabled = step <= _furthest && step != _step };
            if (current) button.SetResourceReference(BackgroundProperty, "Brush.SidebarSelected");
            AutomationProperties.SetName(button, $"Step {i + 1}: {title}");
            var target = step;
            button.Click += (_, _) => { if (Leave()) Show(target); };
            _rail.Children.Add(button);
        }
    }

    /// <summary>Back, a skip link and the main button.</summary>
    private void Footer(string nextText, Action next, bool canSkip = true, bool back = true)
    {
        var primary = new Button { Content = nextText, Style = (Style)FindResource("PrimaryButton"), MinWidth = 140, IsDefault = false };
        AutomationProperties.SetName(primary, nextText);
        primary.Click += (_, _) => next();
        DockPanel.SetDock(primary, Dock.Right);
        _footer.Children.Add(primary);
        if (canSkip)
        {
            var skip = PageKit.Link("Skip this step", () => Show(_step + 1));
            skip.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(skip, Dock.Right);
            _footer.Children.Add(skip);
        }
        if (back && _step > Step.Welcome)
        {
            var b = new Button { Content = "Back", Style = (Style)FindResource("GhostButton"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += (_, _) => { if (Leave()) Show(_step - 1); };
            _footer.Children.Add(b);
        }
        else _footer.Children.Add(new Border());
    }

    /// <summary>Saves what's on the current step before moving away from it. False to stay (a problem was shown).</summary>
    private bool Leave() => _step switch
    {
        Step.Accounts => _pendingAccount?.Invoke(false) ?? true,
        Step.Debts => _pendingDebt?.Invoke(false) ?? true,
        Step.Bills => SaveBills(),
        Step.Income => SaveIncome(),
        _ => true,
    };

    private void Next()
    {
        if (Leave()) Show(_step + 1);
    }

    private void Finish(string? goTo)
    {
        DataStore.Prefs.SetupDone = true;
        DataStore.Prefs.Save();
        _close();
        MainWindow.Instance?.GoTo(goTo ?? "dashboard");
    }

    /// <summary>The one error line moves to the current step's form (it can only have one parent).</summary>
    private void AddError(Panel panel)
    {
        (_error.Parent as Panel)?.Children.Remove(_error);
        panel.Children.Add(_error);
    }

    private void ShowError(string text)
    {
        _error.Text = text;
        _error.Visibility = Visibility.Visible;
    }

    // =====================================================================
    // Building blocks
    // =====================================================================

    private void Title(string title, string intro)
    {
        var glyph = Steps.First(s => s.Step == _step).Glyph;
        var tile = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) };
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        tile.Child = Ui.Text(glyph, size: 22, brushKey: "Brush.Accent").Also(t => { t.Style = (Style)FindResource("Icon"); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; });
        _body.Children.Add(tile);
        var h = new TextBlock { Text = title, FontSize = 30, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        h.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        _body.Children.Add(h);
        _body.Children.Add(Ui.Text(intro, muted: true, size: 15).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Margin = new Thickness(0, 8, 0, 26); t.LineHeight = 22; }));
    }

    private static TextBlock Label(string text, double top = 14) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, top, 0, 6) };

    private static TextBlock Hint(string text) => Ui.Text(text, muted: true, size: 12.5).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Margin = new Thickness(0, 5, 0, 0); });

    private static TextBox Box(string text = "", double width = 0, string? automation = null)
    {
        var box = new TextBox { Text = text };
        if (width > 0) { box.Width = width; box.HorizontalAlignment = HorizontalAlignment.Left; }
        if (automation != null) AutomationProperties.SetName(box, automation);
        return box;
    }

    private static TextBox MoneyBox(string text = "", string? automation = null) => Box(text, 180, automation).Also(b => b.TextAlignment = TextAlignment.Right);

    private WrapPanel Chips(IEnumerable<string> options, string selected, Action<string>? onPick = null)
    {
        var panel = new WrapPanel();
        string group = "gs-" + Guid.NewGuid().ToString("N");
        foreach (var option in options)
        {
            var chip = new RadioButton { Content = option, GroupName = group, IsChecked = option == selected, Style = (Style)FindResource("ChipRadio"), Tag = option };
            AutomationProperties.SetName(chip, option);
            var value = option;
            chip.Checked += (_, _) => onPick?.Invoke(value);
            panel.Children.Add(chip);
        }
        return panel;
    }

    private static string SelectedChip(WrapPanel chips) =>
        chips.Children.OfType<RadioButton>().FirstOrDefault(c => c.IsChecked == true)?.Tag as string ?? "";

    private static Border Card(UIElement child, double bottom = 16) => PageKit.Card(child, padding: new Thickness(22, 18, 22, 20), bottom: bottom);

    /// <summary>A list row: icon tile, title and detail, an amount and a Remove link.</summary>
    private Border ItemRow(string glyph, string title, string detail, string amount, Action remove, bool first)
    {
        var dock = new DockPanel { Margin = new Thickness(0, first ? 0 : 10, 0, 0) };
        var tile = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 14, 0) };
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        tile.Child = Ui.Text(glyph, size: 16, brushKey: "Brush.Accent").Also(t => { t.Style = (Style)FindResource("Icon"); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; });
        DockPanel.SetDock(tile, Dock.Left);
        dock.Children.Add(tile);
        var removeLink = PageKit.Link("Remove", remove);
        removeLink.Margin = new Thickness(10, 0, -8, 0);
        DockPanel.SetDock(removeLink, Dock.Right);
        dock.Children.Add(removeLink);
        var value = Ui.Text(amount, weight: FontWeights.SemiBold, size: 14.5).Also(t => t.VerticalAlignment = VerticalAlignment.Center);
        DockPanel.SetDock(value, Dock.Right);
        dock.Children.Add(value);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Ui.Text(title, weight: FontWeights.SemiBold));
        words.Children.Add(Ui.Text(detail, muted: true, size: 12.5));
        dock.Children.Add(words);
        return new Border { Child = dock };
    }

    private static DateTime NextOnDay(int day)
    {
        var t = DateTime.Today;
        DateTime On(DateTime month) => new(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)));
        var d = On(t);
        return d >= t ? d : On(t.AddMonths(1));
    }

    private static bool TryDay(string text, out int day) => int.TryParse(text.Trim(), out day) && day is >= 1 and <= 31;

    private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    // =====================================================================
    // 1. Welcome
    // =====================================================================

    private void BuildWelcome()
    {
        Title("Welcome to WillowWallet",
            "Let's set up your money in a few minutes: the accounts you use, what you bring home, the bills you pay and the debts you're paying off. You can skip any step and change everything later.");

        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -12, 0) };
        grid.Children.Add(Choice("", "Set up step by step", "About 5 minutes. Accounts, income, budgets, bills and debts.", () => Show(Step.Accounts), highlight: true));
        grid.Children.Add(Choice("", "I use WillowWallet on another device", "Connect to your WillowWallet Server and bring your data here.", () => Finish("sync")));
        grid.Children.Add(Choice("", "Restore a backup", "Bring back a WillowWallet backup (.zip) from this or another PC.", () =>
        {
            SettingsView.Restore();
            if (!DataStore.IsEmpty) Finish(null);
        }));
        _body.Children.Add(grid);

        var privacy = new DockPanel { Margin = new Thickness(0, 26, 0, 0) };
        var lockIcon = Ui.Text("", size: 14, brushKey: "Brush.Accent").Also(t => { t.Style = (Style)FindResource("Icon"); t.Margin = new Thickness(0, 2, 10, 0); t.VerticalAlignment = VerticalAlignment.Top; });
        DockPanel.SetDock(lockIcon, Dock.Left);
        privacy.Children.Add(lockIcon);
        privacy.Children.Add(Hint("Everything stays on this PC. WillowWallet doesn't sign in to your bank or send your data anywhere unless you turn on sync with your own server.").Also(t => t.Margin = new Thickness(0)));
        _body.Children.Add(privacy);

        Footer("Let's get started", () => Show(Step.Accounts), canSkip: false, back: false);
    }

    private Button Choice(string glyph, string title, string text, Action go, bool highlight = false)
    {
        var panel = new StackPanel();
        var tile = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
        tile.SetResourceReference(Border.BackgroundProperty, highlight ? "Brush.Accent" : "Brush.AccentSoft");
        tile.Child = Ui.Text(glyph, size: 19, brushKey: highlight ? "Brush.AccentText" : "Brush.Accent").Also(t => { t.Style = (Style)FindResource("Icon"); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; });
        panel.Children.Add(tile);
        panel.Children.Add(Ui.Text(title, weight: FontWeights.SemiBold, size: 15).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }));
        panel.Children.Add(Ui.Text(text, muted: true, size: 12.5).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Margin = new Thickness(0, 5, 0, 0); t.LineHeight = 18; }));
        var card = new Border { Child = panel, CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 18, 18, 20), BorderThickness = new Thickness(highlight ? 1.5 : 1), MinHeight = 178 };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        card.SetResourceReference(Border.BorderBrushProperty, highlight ? "Brush.Accent" : "Brush.Border");
        var button = new Button { Content = card, Style = (Style)FindResource("CardButton"), Margin = new Thickness(0, 0, 12, 0) };
        AutomationProperties.SetName(button, title);
        button.Click += (_, _) => go();
        return button;
    }

    // =====================================================================
    // 2. Accounts
    // =====================================================================

    private Func<bool, bool>? _pendingAccount;

    private void BuildAccounts()
    {
        Title("Where's your money?",
            "Add the accounts you use: checking, savings, credit cards, cash. Use today's balance; the transactions you add or import from now on keep it up to date.");

        var list = new StackPanel();
        var form = new StackPanel();
        string[] types = [AccountTypes.Checking, AccountTypes.Savings, AccountTypes.CreditCard, AccountTypes.Cash, AccountTypes.Investment];
        var balanceLabel = Label("Balance today");
        var nameBox = Box(automation: "Account name");
        var bankBox = Box(automation: "Bank");
        var balanceBox = MoneyBox(automation: "Balance");
        var typeChips = Chips(types, AccountTypes.Checking, type =>
        {
            balanceLabel.Text = type == AccountTypes.CreditCard ? "What you owe on it today" : "Balance today";
        });
        form.Children.Add(Label("Type", 0));
        form.Children.Add(typeChips);
        var cols = Ui.Columns(1, 0.06, 1);
        var nameCol = new StackPanel();
        nameCol.Children.Add(Label("Name"));
        nameCol.Children.Add(nameBox);
        nameCol.Children.Add(Hint("Like \"Everyday checking\" or \"Visa card\". Leave empty to use the type."));
        cols.Children.Add(nameCol.At(0));
        var bankCol = new StackPanel();
        bankCol.Children.Add(Label("Bank (optional)"));
        bankCol.Children.Add(bankBox);
        cols.Children.Add(bankCol.At(2));
        form.Children.Add(cols);
        form.Children.Add(balanceLabel);
        var balanceRow = new StackPanel { Orientation = Orientation.Horizontal };
        balanceRow.Children.Add(balanceBox);
        var add = PageKit.Primary("", "Add account", (_, _) => { if (TryAdd(true)) nameBox.Focus(); });
        add.Margin = new Thickness(12, 0, 0, 0);
        balanceRow.Children.Add(add);
        form.Children.Add(balanceRow);
        foreach (var box in new[] { nameBox, bankBox, balanceBox })
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; if (TryAdd(true)) nameBox.Focus(); } };
        AddError(form);
        _body.Children.Add(Card(form));

        _body.Children.Add(list);
        void RefreshList()
        {
            list.Children.Clear();
            var accounts = DataStore.Accounts.Items.Where(a => !a.IsClosed).ToList();
            if (accounts.Count == 0) return;
            list.Children.Add(PageKit.Section($"Added ({accounts.Count})", 0).Also(t => t.Margin = new Thickness(2, 4, 0, 10)));
            var rows = new StackPanel();
            for (int i = 0; i < accounts.Count; i++)
            {
                var a = accounts[i];
                string glyph = a.Type switch { AccountTypes.CreditCard => "", AccountTypes.Cash => "", AccountTypes.Investment => "", _ => "" };
                rows.Children.Add(ItemRow(glyph, a.Name, a.Institution.Length > 0 ? $"{a.Type} · {a.Institution}" : a.Type,
                    a.IsLiability ? $"{Money.Format(-a.StartingBalance)} owed" : Money.Format(a.StartingBalance),
                    () => { DataStore.Accounts.Delete(a); RefreshList(); }, i == 0));
            }
            list.Children.Add(Card(rows));
        }
        RefreshList();

        // Adds what's in the form. "quiet" (moving on): an empty form is fine; a half-filled one is added if it can be.
        bool TryAdd(bool fromButton)
        {
            string type = SelectedChip(typeChips);
            string name = nameBox.Text.Trim();
            string balanceText = balanceBox.Text.Trim();
            if (!fromButton && name.Length == 0 && balanceText.Length == 0 && bankBox.Text.Trim().Length == 0) return true;
            if (balanceText.Length > 0 && !Money.TryParse(balanceText, out _)) { ShowError("Enter the balance as a number, like 1250.00."); return false; }
            Money.TryParse(balanceText, out var balance);
            if (name.Length == 0) name = type == AccountTypes.CreditCard ? "Credit card" : type;
            if (DataStore.Accounts.Items.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                ShowError($"There's already an account called \"{name}\". Give this one another name.");
                return false;
            }
            var account = new Account { Name = name, Type = type, Institution = bankBox.Text.Trim(), BalanceDate = DateTime.Today };
            account.StartingBalance = account.IsLiability ? -Math.Abs(balance) : balance;
            DataStore.Accounts.Add(account);
            nameBox.Text = bankBox.Text = balanceBox.Text = "";
            _error.Visibility = Visibility.Collapsed;
            RefreshList();
            return true;
        }
        _pendingAccount = TryAdd;
        Dispatcher.BeginInvoke(() => nameBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        Footer("Next", Next);
    }

    // =====================================================================
    // 3. Income and budgets
    // =====================================================================

    private void BuildIncome()
    {
        Title("What do you bring home?",
            "Your take-home pay each month, after taxes. WillowWallet uses it to suggest budgets for each category; you can fine-tune any of them on the Budgets page.");

        var form = new StackPanel();
        form.Children.Add(Label("Monthly take-home pay", 0));
        var income = MoneyBox(_income, "Monthly take-home pay");
        form.Children.Add(income);
        form.Children.Add(Hint("Paid every two weeks? Multiply one paycheck by 26, then divide by 12."));
        form.Children.Add(Label("Budgets", 18));
        string[] methods = [BudgetTemplateDialog.FiftyThirtyTwenty, BudgetTemplateDialog.Starter, "Not now"];
        var explain = Hint("");
        var preview = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var chips = Chips(methods, _budgetMethod, m => { _budgetMethod = m; Refresh(); });
        form.Children.Add(chips);
        form.Children.Add(explain);
        form.Children.Add(preview);
        AddError(form);
        _body.Children.Add(Card(form));
        income.TextChanged += (_, _) => { _income = income.Text; Refresh(); };

        void Refresh()
        {
            explain.Text = _budgetMethod switch
            {
                BudgetTemplateDialog.FiftyThirtyTwenty => "50% for needs (housing, bills, groceries, getting around), 30% for wants (eating out, shopping, fun) and 20% for savings and extra debt payments.",
                BudgetTemplateDialog.Starter => "Typical starting amounts for common categories, whatever your income.",
                _ => "Skip budgets for now. You can set them up from a template on the Budgets page later.",
            };
            preview.Children.Clear();
            if (_budgetMethod == "Not now") return;
            decimal pay = Money.TryParse(_income, out var p) ? p : 0;
            if (_budgetMethod == BudgetTemplateDialog.FiftyThirtyTwenty && pay <= 0)
            {
                preview.Children.Add(Ui.Text("Enter your take-home pay to see the budgets.", muted: true));
                return;
            }
            var plan = BudgetTemplateDialog.Plan(_budgetMethod, pay, new Dictionary<string, decimal>());
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            foreach (var (category, amount) in plan.OrderByDescending(kv => kv.Value))
            {
                var row = new DockPanel { Margin = new Thickness(0, 4, 24, 4) };
                var value = Ui.Text(Money.Format(amount, cents: false), weight: FontWeights.SemiBold);
                DockPanel.SetDock(value, Dock.Right);
                row.Children.Add(value);
                row.Children.Add(Ui.Text(Categories.Display(category)));
                grid.Children.Add(row);
            }
            preview.Children.Add(grid);
            var total = Ui.Text(pay > 0 ? $"{Money.Format(plan.Values.Sum(), cents: false)} a month of {Money.Format(pay, cents: false)}" : $"{Money.Format(plan.Values.Sum(), cents: false)} a month", weight: FontWeights.SemiBold, brushKey: "Brush.Accent");
            total.Margin = new Thickness(0, 10, 0, 0);
            preview.Children.Add(total);
        }
        Refresh();
        Dispatcher.BeginInvoke(() => income.Focus(), System.Windows.Threading.DispatcherPriority.Input);

        Footer("Next", Next);
    }

    private bool SaveIncome()
    {
        if (_income.Trim().Length > 0 && !Money.TryParse(_income, out _)) { ShowError("Enter your pay as a number, like 4200."); return false; }
        if (_budgetMethod == "Not now") return true;
        decimal pay = Money.TryParse(_income, out var p) ? p : 0;
        if (_budgetMethod == BudgetTemplateDialog.FiftyThirtyTwenty && pay <= 0)
        {
            if (_income.Trim().Length == 0 && _createdBudgets.Count == 0) return true; // nothing entered: same as skipping
            ShowError("Enter your take-home pay, or choose Starter amounts or Not now.");
            return false;
        }
        ApplyBudgets(BudgetTemplateDialog.Plan(_budgetMethod, pay, new Dictionary<string, decimal>()));
        return true;
    }

    /// <summary>Creates (or, when coming back to this step, updates) the budgets for the plan.</summary>
    private void ApplyBudgets(Dictionary<string, decimal> plan)
    {
        var month = Budgeting.MonthStart(DateTime.Today);
        using (DataStore.Batch())
        {
            // Budgets this step made before but the new plan doesn't have
            foreach (var old in DataStore.Budgets.Items.Where(b => _createdBudgets.Contains(b.Category) && !plan.ContainsKey(b.Category)).ToList())
            {
                DataStore.Budgets.Delete(old);
                _createdBudgets.Remove(old.Category);
            }
            foreach (var (category, amount) in plan)
            {
                var existing = DataStore.Budgets.Items.FirstOrDefault(b => string.Equals(b.Category, category, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    DataStore.Budgets.Add(new Budget { Category = category, MonthlyLimit = amount, StartMonth = month });
                    _createdBudgets.Add(category);
                }
                else if (_createdBudgets.Contains(category))
                {
                    existing.MonthlyLimit = amount;
                    DataStore.Budgets.Update(existing);
                }
            }
        }
    }

    // =====================================================================
    // 4. Regular bills
    // =====================================================================

    private sealed class BillRow
    {
        public required string Name { get; set; }
        public required string Category { get; init; }
        public string Repeats { get; set; } = BillSchedule.Monthly;
        public bool Custom { get; init; }
        public bool On { get; set; }
        public string Amount { get; set; } = "";
        public string Day { get; set; } = "1";
        public Bill? Created { get; set; }
    }

    private static readonly (string Name, string Category, string Repeats)[] BillPresets =
    [
        ("Rent", "Housing", BillSchedule.Monthly),
        ("Electric", "Utilities", BillSchedule.Monthly),
        ("Gas / heat", "Utilities", BillSchedule.Monthly),
        ("Water & sewer", "Utilities", BillSchedule.Monthly),
        ("Internet", "Utilities", BillSchedule.Monthly),
        ("Phone", "Utilities", BillSchedule.Monthly),
        ("Car insurance", "Transportation", BillSchedule.Monthly),
        ("Home or renters insurance", "Housing", BillSchedule.Monthly),
        ("Health insurance", "Health", BillSchedule.Monthly),
        ("Streaming (Netflix, Disney+…)", "Subscriptions", BillSchedule.Monthly),
        ("Music", "Subscriptions", BillSchedule.Monthly),
        ("Gym", "Health", BillSchedule.Monthly),
        ("Childcare", "Kids", BillSchedule.Monthly),
    ];

    private void BuildBills()
    {
        Title("Bills that come around regularly",
            "Tick the ones you pay and enter about how much and the day they're due. WillowWallet reminds you before each one. Loan and credit card payments come in the next step.");
        if (_billRows.Count == 0)
            foreach (var (name, category, repeats) in BillPresets)
                _billRows.Add(new BillRow { Name = name, Category = category, Repeats = repeats });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        BillColumns(header);
        foreach (var (text, col) in new[] { ("Bill", 1), ("Amount", 2), ("Due on day", 3), ("Repeats", 4) })
            header.Children.Add(Ui.Text(text, muted: true, size: 12, weight: FontWeights.SemiBold).At(col));
        var rows = new StackPanel();
        rows.Children.Add(header);
        foreach (var row in _billRows) rows.Children.Add(BillLine(row));

        var addOther = PageKit.Link("+ Add another bill", () =>
        {
            var row = new BillRow { Name = "", Category = "Other", Custom = true, On = true };
            _billRows.Add(row);
            var line = BillLine(row);
            rows.Children.Add(line);
            Dispatcher.BeginInvoke(() => line.Children.OfType<TextBox>().FirstOrDefault()?.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        });
        addOther.HorizontalAlignment = HorizontalAlignment.Left;
        addOther.Margin = new Thickness(-8, 8, 0, 0);
        var panel = new StackPanel();
        panel.Children.Add(rows);
        panel.Children.Add(addOther);
        AddError(panel);
        _body.Children.Add(Card(panel));
        _body.Children.Add(Hint("Not sure of an amount? Use a typical month; you can change it when the bill comes. Paid on different days? Pick the usual one."));
        Footer("Next", Next);
    }

    private static void BillColumns(Grid g)
    {
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
    }

    private Grid BillLine(BillRow row)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        BillColumns(g);
        var on = new CheckBox { IsChecked = row.On, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(on, row.Custom ? "Pay this bill" : row.Name);
        g.Children.Add(on.At(0));
        FrameworkElement nameCell;
        if (row.Custom)
        {
            var nameBox = Box(row.Name, automation: "Bill name").Also(b => b.Margin = new Thickness(0, 0, 12, 0));
            nameBox.TextChanged += (_, _) => row.Name = nameBox.Text;
            nameCell = nameBox;
        }
        else nameCell = Ui.Text(row.Name, size: 14).Also(t => t.VerticalAlignment = VerticalAlignment.Center);
        g.Children.Add(nameCell.At(1));
        var amount = Box(row.Amount, automation: $"{row.Name} amount").Also(b => { b.TextAlignment = TextAlignment.Right; b.Margin = new Thickness(0, 0, 12, 0); });
        amount.TextChanged += (_, _) => { row.Amount = amount.Text; if (amount.Text.Trim().Length > 0) on.IsChecked = true; };
        g.Children.Add(amount.At(2));
        var day = Box(row.Day, automation: $"{row.Name} due day").Also(b => { b.TextAlignment = TextAlignment.Center; b.Margin = new Thickness(0, 0, 12, 0); });
        day.TextChanged += (_, _) => row.Day = day.Text;
        g.Children.Add(day.At(3));
        var repeats = new ComboBox { ItemsSource = BillSchedule.All.Where(r => r != BillSchedule.Once).ToList(), SelectedItem = row.Repeats };
        AutomationProperties.SetName(repeats, $"{row.Name} repeats");
        repeats.SelectionChanged += (_, _) => row.Repeats = repeats.SelectedItem as string ?? BillSchedule.Monthly;
        g.Children.Add(repeats.At(4));

        void Dim() { foreach (var c in new UIElement[] { amount, day, repeats }) c.Opacity = on.IsChecked == true ? 1 : 0.55; }
        on.Checked += (_, _) => { row.On = true; Dim(); if (amount.Text.Length == 0) amount.Focus(); };
        on.Unchecked += (_, _) => { row.On = false; Dim(); };
        Dim();
        return g;
    }

    /// <summary>Creates, updates or removes the bills ticked on this step.</summary>
    private bool SaveBills()
    {
        foreach (var row in _billRows.Where(r => r.On))
        {
            string label = row.Name.Trim().Length > 0 ? row.Name.Trim() : "the new bill";
            if (row.Custom && row.Name.Trim().Length == 0 && row.Amount.Trim().Length == 0) continue;
            if (row.Custom && row.Name.Trim().Length == 0) { ShowError("Give the bill you added a name."); return false; }
            if (!Money.TryParse(row.Amount, out var amount) || amount <= 0) { ShowError($"Enter how much {label} is, or untick it."); return false; }
            if (!TryDay(row.Day, out _)) { ShowError($"The due day for {label} should be a number from 1 to 31."); return false; }
        }
        string account = DataStore.AccountChoices()[0];
        using (DataStore.Batch())
        {
            foreach (var row in _billRows)
            {
                bool wanted = row.On && Money.TryParse(row.Amount, out var a) && a > 0 && row.Name.Trim().Length > 0;
                if (!wanted)
                {
                    if (row.Created != null) { DataStore.Bills.Delete(row.Created); row.Created = null; }
                    continue;
                }
                Money.TryParse(row.Amount, out var amount);
                TryDay(row.Day, out int day);
                var bill = row.Created ?? new Bill { Account = account };
                bill.Name = row.Name.Trim().Replace(" (Netflix, Disney+…)", "");
                bill.Amount = amount;
                bill.Category = row.Category;
                bill.Repeats = row.Repeats;
                bill.DueDay = day;
                bill.NextDue = NextOnDay(day);
                if (row.Created == null) { DataStore.Bills.Add(bill); row.Created = bill; }
                else DataStore.Bills.Update(bill);
            }
        }
        return true;
    }

    // =====================================================================
    // 5. Loans and cards
    // =====================================================================

    private Func<bool, bool>? _pendingDebt;

    private void BuildDebts()
    {
        Title("Loans and cards you're paying off",
            "Student loans, car loans, credit card balances, medical bills, a mortgage. Each one's monthly payment becomes a recurring bill, so marking it paid lowers what you owe, and WillowWallet shows when you'll be debt-free.");

        var list = new StackPanel();
        var form = new StackPanel();
        var typeChips = Chips(DebtTypes.All, "Student loan");
        form.Children.Add(Label("Type", 0));
        form.Children.Add(typeChips);
        var nameBox = Box(automation: "Debt name");
        var balanceBox = MoneyBox(automation: "Amount owed");
        var paymentBox = MoneyBox(automation: "Monthly payment");
        var rateBox = Box("", 120, "Interest rate").Also(b => b.TextAlignment = TextAlignment.Right);
        var dayBox = Box("1", 120, "Payment due day").Also(b => b.TextAlignment = TextAlignment.Center);
        var asBill = new CheckBox { Content = "Add the payment as a recurring monthly bill", IsChecked = true, Margin = new Thickness(0, 16, 0, 0) };

        var row1 = Ui.Columns(1, 0.06, 1);
        row1.Children.Add(new StackPanel().Also(p => { p.Children.Add(Label("Name")); p.Children.Add(nameBox); p.Children.Add(Hint("Leave empty to use the type.")); }).At(0));
        row1.Children.Add(new StackPanel().Also(p => { p.Children.Add(Label("Amount owed today")); p.Children.Add(balanceBox); }).At(2));
        form.Children.Add(row1);
        var row2 = Ui.Columns(1, 0.06, 0.62, 0.06, 0.62);
        row2.Children.Add(new StackPanel().Also(p => { p.Children.Add(Label("Monthly payment")); p.Children.Add(paymentBox); }).At(0));
        row2.Children.Add(new StackPanel().Also(p => { p.Children.Add(Label("Interest (APR %)")); p.Children.Add(rateBox); }).At(2));
        row2.Children.Add(new StackPanel().Also(p => { p.Children.Add(Label("Due on day")); p.Children.Add(dayBox); }).At(4));
        form.Children.Add(row2);
        form.Children.Add(Hint("The interest rate is on your statement; leave it empty if you don't know it."));
        form.Children.Add(asBill);
        var add = PageKit.Primary("", "Add debt", (_, _) => { if (TryAdd(true)) nameBox.Focus(); });
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 16, 0, 0);
        form.Children.Add(add);
        AddError(form);
        _body.Children.Add(Card(form));

        _body.Children.Add(list);
        void RefreshList()
        {
            list.Children.Clear();
            var debts = DataStore.Debts.Items.Where(d => !d.IsPaidOff).ToList();
            if (debts.Count == 0) return;
            list.Children.Add(PageKit.Section($"Added ({debts.Count})", 0).Also(t => t.Margin = new Thickness(2, 4, 0, 10)));
            var rows = new StackPanel();
            for (int i = 0; i < debts.Count; i++)
            {
                var d = debts[i];
                var bill = DebtLink.BillFor(d);
                string detail = d.Type + (bill != null ? $" · {Money.Format(bill.Amount)} bill on the {Ordinal(bill.DueDay > 0 ? bill.DueDay : bill.NextDue.Day)} of each month" : d.MinimumPayment > 0 ? $" · {Money.Format(d.MinimumPayment)} a month" : "");
                rows.Children.Add(ItemRow(d.Type == DebtTypes.CreditCard ? "" : "", d.Name, detail, Money.Format(d.Balance), () =>
                {
                    if (DebtLink.BillFor(d) is { } b) DataStore.Bills.Delete(b);
                    DataStore.Debts.Delete(d);
                    RefreshList();
                }, i == 0));
            }
            list.Children.Add(Card(rows));
        }
        RefreshList();

        bool TryAdd(bool fromButton)
        {
            string type = SelectedChip(typeChips);
            string name = nameBox.Text.Trim();
            if (!fromButton && name.Length == 0 && balanceBox.Text.Trim().Length == 0 && paymentBox.Text.Trim().Length == 0) return true;
            if (!Money.TryParse(balanceBox.Text, out var balance) || balance <= 0) { ShowError("Enter how much you owe, like 18420."); return false; }
            Money.TryParse(paymentBox.Text, out var payment);
            decimal rate = 0;
            if (rateBox.Text.Trim().Length > 0 && !Money.TryParsePercent(rateBox.Text, out rate)) { ShowError("Enter the interest rate as a number, like 5.5."); return false; }
            bool bill = asBill.IsChecked == true;
            if (bill && payment <= 0) { ShowError("Enter the monthly payment, or untick the bill."); return false; }
            if (bill && !TryDay(dayBox.Text, out _)) { ShowError("The due day should be a number from 1 to 31."); return false; }
            if (name.Length == 0) name = type;
            var debt = new Debt { Name = name, Type = type, Balance = balance, OriginalBalance = balance, InterestRate = rate, MinimumPayment = payment };
            DataStore.Debts.Add(debt);
            if (bill)
            {
                TryDay(dayBox.Text, out int day);
                DebtLink.CreateBill(debt, NextOnDay(day), DataStore.AccountChoices()[0]);
            }
            nameBox.Text = balanceBox.Text = paymentBox.Text = rateBox.Text = "";
            _error.Visibility = Visibility.Collapsed;
            RefreshList();
            return true;
        }
        _pendingDebt = TryAdd;
        foreach (var box in new[] { nameBox, balanceBox, paymentBox, rateBox, dayBox })
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; if (TryAdd(true)) nameBox.Focus(); } };
        Dispatcher.BeginInvoke(() => nameBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        Footer("Next", Next);
    }

    // =====================================================================
    // 6. Keep it private
    // =====================================================================

    private void BuildProtect()
    {
        Title("Keep it private",
            "Add a PIN or password and WillowWallet asks for it when it opens. Your data is encrypted on this PC, so nobody can read the files without it.");
        var panel = new StackPanel();
        if (DataLock.IsEnabled)
        {
            panel.Children.Add(Status("", "Protection is on", "WillowWallet asks for your PIN or password when it opens, and locks after a while away."));
        }
        else
        {
            panel.Children.Add(Status("", "Not protected yet", "Anyone who can sign in to this PC can open WillowWallet."));
            var on = PageKit.Primary("", "Add a PIN or password", (_, _) => { SettingsView.TurnOn(); Show(Step.Protect); });
            on.HorizontalAlignment = HorizontalAlignment.Left;
            on.Margin = new Thickness(0, 16, 0, 0);
            panel.Children.Add(on);
            panel.Children.Add(Hint("You'll get a recovery key in case you forget it. Keep it somewhere safe."));
        }
        _body.Children.Add(Card(panel));
        Footer(DataLock.IsEnabled ? "Next" : "Not now", () => Show(Step.Done), canSkip: false);
    }

    private DockPanel Status(string glyph, string title, string text)
    {
        var dock = new DockPanel();
        var tile = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 14, 0) };
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        tile.Child = Ui.Text(glyph, size: 18, brushKey: "Brush.Accent").Also(t => { t.Style = (Style)FindResource("Icon"); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; });
        DockPanel.SetDock(tile, Dock.Left);
        dock.Children.Add(tile);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Ui.Text(title, weight: FontWeights.SemiBold, size: 15));
        words.Children.Add(Ui.Text(text, muted: true).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }));
        dock.Children.Add(words);
        return dock;
    }

    // =====================================================================
    // 7. All set
    // =====================================================================

    private void BuildDone()
    {
        int accounts = DataStore.Accounts.Items.Count(a => !a.IsClosed), budgets = DataStore.Budgets.Count;
        int bills = DataStore.Bills.Items.Count(b => !b.IsDone && !b.PaysDebt), debts = DataStore.Debts.Items.Count(d => !d.IsPaidOff);
        Title("You're all set", accounts + budgets + bills + debts == 0
            ? "You can add everything as you go. The Dashboard shows a checklist to help you get started."
            : "Here's what WillowWallet knows so far. Everything can be changed from its page in the sidebar.");

        var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -12, 18) };
        foreach (var (n, one, many, glyph) in new[] { (accounts, "account", "accounts", ""), (budgets, "budget", "budgets", ""), (bills, "bill", "bills", ""), (debts, "debt", "debts", "") })
        {
            var p = new StackPanel();
            p.Children.Add(Ui.Text(glyph, size: 17, brushKey: "Brush.Accent").Also(t => { t.Style = (Style)FindResource("Icon"); t.Margin = new Thickness(0, 0, 0, 10); }));
            p.Children.Add(Ui.Text(n.ToString(), size: 28, weight: FontWeights.Bold).Also(t => t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont")));
            p.Children.Add(Ui.Text(n == 1 ? one : many, muted: true));
            tiles.Children.Add(PageKit.Card(p, padding: new Thickness(18, 16, 18, 16), bottom: 0).Also(c => c.Margin = new Thickness(0, 0, 12, 0)));
        }
        _body.Children.Add(tiles);

        _body.Children.Add(PageKit.Section("What's next", 6).Also(t => t.Margin = new Thickness(2, 6, 0, 10)));
        var next = new StackPanel();
        next.Children.Add(NextRow("", "Import your transactions", "Download a statement (CSV, OFX or QFX) from your bank's website and bring it in.", "Import…", () => { Finish("transactions"); ImportDialog.Run(); }, true));
        next.Children.Add(NextRow("", "Use it on your phone too", "Sync with WillowWallet for Android through your own WillowWallet Server.", "Set up sync", () => Finish("sync"), false));
        next.Children.Add(NextRow("", "Save for something", "A vacation, an emergency fund, a new car: track your progress toward it.", "Add a goal", () => Finish("goals"), false));
        _body.Children.Add(Card(next));

        Footer("Go to my dashboard", () => Finish(null), canSkip: false);
    }

    private Border NextRow(string glyph, string title, string text, string action, Action go, bool first)
    {
        var dock = new DockPanel { Margin = new Thickness(0, first ? 0 : 14, 0, 0) };
        var tile = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        tile.Child = Ui.Text(glyph, size: 16, brushKey: "Brush.Accent").Also(t => { t.Style = (Style)FindResource("Icon"); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; });
        DockPanel.SetDock(tile, Dock.Left);
        dock.Children.Add(tile);
        var button = new Button { Content = action, Style = (Style)FindResource("GhostButton"), MinWidth = 120, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        AutomationProperties.SetName(button, action);
        button.Click += (_, _) => go();
        DockPanel.SetDock(button, Dock.Right);
        dock.Children.Add(button);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Ui.Text(title, weight: FontWeights.SemiBold));
        words.Children.Add(Ui.Text(text, muted: true, size: 12.5).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }));
        dock.Children.Add(words);
        return new Border { Child = dock };
    }

    // =====================================================================
    // For the self-test and screenshots
    // =====================================================================

    internal string CurrentStep => _step.ToString();
    internal void GoToStep(string step) => Show(Enum.Parse<Step>(step));
    internal bool Advance() { if (!Leave()) return false; if (_step < Step.Done) Show(_step + 1); return true; }
    internal void SetIncome(string text, string method) { _income = text; _budgetMethod = method; }
    internal void TickBill(string name, string amount, string day)
    {
        if (_billRows.Count == 0)
            foreach (var (n, c, r) in BillPresets) _billRows.Add(new BillRow { Name = n, Category = c, Repeats = r });
        var row = _billRows.First(r => r.Name == name);
        row.On = true;
        row.Amount = amount;
        row.Day = day;
    }
    internal void Close() => Finish(null);
}
