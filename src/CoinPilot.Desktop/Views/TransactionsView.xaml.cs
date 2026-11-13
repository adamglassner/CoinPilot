using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>
/// The Transactions page: one budget month of spending, income and transfers; search and filters; add / edit /
/// delete; statement import and CSV export.
/// </summary>
public partial class TransactionsView : UserControl, ISearchablePage
{
    // Date, Payee, Category, Account, Amount
    private static readonly double[] ColumnStars = [0.95, 2.5, 1.5, 1.2, 1.1];

    private const string All = "All", SpendingOnly = "Spending", IncomeOnly = "Income", TransfersOnly = "Transfers";

    // Filters are remembered while the app is open.
    private static string _type = All, _category = All, _account = All, _tag = All;
    private static bool _filtersOpen;

    private List<Transaction> _shown = [];
    private bool _loadingFilters;

    public TransactionsView()
    {
        InitializeComponent();
        BuildHeader();
        Month.MonthChanged += (_, _) => Refresh();
        Loaded += (_, _) => { DataStore.Changed += OnDataChanged; Refresh(); };
        Unloaded += (_, _) => DataStore.Changed -= OnDataChanged;
        FilterBar.Visibility = _filtersOpen ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnDataChanged(object? sender, EventArgs e) => Refresh();

    private void BuildHeader()
    {
        var g = Ui.Columns(ColumnStars);
        string[] names = ["DATE", "PAYEE", "CATEGORY", "ACCOUNT", "AMOUNT"];
        for (int i = 0; i < names.Length; i++)
        {
            var t = Ui.Text(names[i], muted: true, size: 11, weight: FontWeights.SemiBold).At(i, new Thickness(10, 0, 10, 0));
            if (i == names.Length - 1) t.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(t);
        }
        HeaderRow.Children.Add(g);
    }

    // ----- Filters -----

    private static void Fill(ComboBox combo, IEnumerable<(string Value, string Label)> items, string selected)
    {
        combo.Items.Clear();
        foreach (var (value, label) in items) combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected) ?? combo.Items[0];
    }

    private void LoadFilters()
    {
        _loadingFilters = true;
        Fill(TypeFilter, new[] { All, SpendingOnly, IncomeOnly, TransfersOnly }.Select(x => (x, x == All ? "All types" : x)), _type);
        var categories = Categories.Spending.Concat(Categories.Income).Select(c => (c, Categories.Display(c)));
        Fill(CategoryFilter, new[] { (All, "All categories") }.Concat(categories), _category);
        var accounts = DataStore.AccountChoices()
            .Concat(DataStore.Transactions.Items.Select(t => t.Account))
            .Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a);
        Fill(AccountFilter, new[] { (All, "All accounts") }.Concat(accounts.Select(a => (a, a))), _account);
        var tags = DataStore.Transactions.Items.SelectMany(t => t.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t);
        Fill(TagFilter, new[] { (All, "All tags") }.Concat(tags.Select(t => (t, t))), _tag);
        _loadingFilters = false;
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingFilters) return;
        _type = (TypeFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? All;
        _category = (CategoryFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? All;
        _account = (AccountFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? All;
        _tag = (TagFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? All;
        Refresh();
    }

    private void Filters_Click(object sender, RoutedEventArgs e)
    {
        _filtersOpen = !_filtersOpen;
        FilterBar.Visibility = _filtersOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _type = _category = _account = _tag = All;
        Refresh();
    }

    private static int ActiveFilters => new[] { _type, _category, _account, _tag }.Count(f => f != All);

    private static bool PassesFilters(Transaction t)
    {
        if (_type == SpendingOnly && !t.IsSpending) return false;
        if (_type == IncomeOnly && !t.IsIncome) return false;
        if (_type == TransfersOnly && !t.IsTransfer) return false;
        if (_category != All && !t.Parts().Any(p => string.Equals(p.Category, _category, StringComparison.OrdinalIgnoreCase)
                                                     || string.Equals(Categories.ParentOf(p.Category), _category, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (_account != All && !string.Equals(t.Account, _account, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(t.TransferAccount, _account, StringComparison.OrdinalIgnoreCase)) return false;
        if (_tag != All && !t.Tags.Contains(_tag, StringComparer.OrdinalIgnoreCase)) return false;
        return true;
    }

    // ----- List -----

    /// <summary>Rebuilds totals and the list for the current month, search and filters.</summary>
    public void Refresh()
    {
        LoadFilters();
        int active = ActiveFilters;
        FilterLabel.Text = active == 0 ? "Filters" : $"Filters ({active})";

        var summary = Budgeting.Summarize(MonthSwitcher.Current);
        string q = SearchBox.Text.Trim();
        _shown = summary.Transactions
            .Where(t => (q.Length == 0 || Matches(t, q)) && PassesFilters(t))
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .ToList();

        bool narrowed = q.Length > 0 || active > 0;
        var source = narrowed ? _shown : summary.Transactions.ToList();
        decimal spent = source.Where(t => t.IsSpending).Sum(t => -t.Amount);
        decimal income = source.Where(t => t.IsIncome).Sum(t => t.Amount);
        SpentText.Text = Money.Format(spent);
        IncomeText.Text = Money.Format(income);
        NetText.Text = Money.Format(income - spent);
        NetText.SetResourceReference(TextBlock.ForegroundProperty, income - spent < 0 ? "Brush.Danger" : "Brush.Text");
        CountLabel.Text = narrowed ? "Matching transactions" : "Transactions";
        CountText.Text = source.Count.ToString("N0", Money.Us);

        Rows.Children.Clear();
        bool first = true;
        foreach (var t in _shown)
        {
            Rows.Children.Add(BuildRow(t, first));
            first = false;
        }

        HeaderRow.Visibility = _shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ExportButton.IsEnabled = _shown.Count > 0;
        EmptyHost.Content = _shown.Count > 0 ? null
            : narrowed
                ? Ui.EmptyState("", "No matches", $"Nothing in {Money.MonthName(MonthSwitcher.Current)} matches your search or filters.")
                : Ui.EmptyState("", $"No transactions in {Money.MonthName(MonthSwitcher.Current)}",
                    "Add what you spend and earn, or import a statement from your bank, and CoinPilot totals it up and tracks it against your budgets.",
                    "Add transaction", Add_Click);
    }

    private static bool Matches(Transaction t, string q) =>
        t.Payee.Contains(q, StringComparison.OrdinalIgnoreCase)
        || t.Parts().Any(p => Categories.Display(p.Category).Contains(q, StringComparison.OrdinalIgnoreCase))
        || t.Account.Contains(q, StringComparison.OrdinalIgnoreCase)
        || t.TransferAccount.Contains(q, StringComparison.OrdinalIgnoreCase)
        || t.Note.Contains(q, StringComparison.OrdinalIgnoreCase)
        || t.Tags.Any(tag => tag.Contains(q, StringComparison.OrdinalIgnoreCase))
        || Money.Format(t.Size).Contains(q, StringComparison.OrdinalIgnoreCase)
        || t.Size.ToString("0.00", Money.Us).Contains(q, StringComparison.OrdinalIgnoreCase);

    private Button BuildRow(Transaction t, bool firstRow)
    {
        var g = Ui.Columns(ColumnStars);
        var cell = new Thickness(10, 12, 10, 12);

        g.Children.Add(Ui.Text(t.Date.ToString("ddd, MMM d", Money.Us), muted: true).At(0, cell));

        var payee = new StackPanel { VerticalAlignment = VerticalAlignment.Center }.At(1, cell);
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        if (t.IsTransfer)
        {
            var icon = new TextBlock { Text = "", Style = (Style)FindResource("Icon"), FontSize = 12, Margin = new Thickness(0, 0, 7, 0) };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            nameLine.Children.Add(icon);
        }
        nameLine.Children.Add(Ui.Text(t.Payee, weight: FontWeights.SemiBold));
        if (t.Receipt.Length > 0)
        {
            var clip = new TextBlock { Text = "", Style = (Style)FindResource("Icon"), FontSize = 12, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Has a receipt" };
            clip.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            nameLine.Children.Add(clip);
        }
        payee.Children.Add(nameLine);
        string detail = t.IsTransfer ? $"{t.Account} → {t.TransferAccount}" : t.Note.Replace('\n', ' ');
        if (detail.Length > 0) payee.Children.Add(Ui.Text(detail, muted: true, size: 12));
        if (t.Tags.Count > 0)
        {
            var tags = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var tag in t.Tags.Take(4))
                tags.Children.Add(PageKit.Pill("#" + tag, "Brush.TextMuted").Also(p => p.Margin = new Thickness(0, 0, 5, 0)));
            payee.Children.Add(tags);
        }
        g.Children.Add(payee);

        var chip = Ui.Chip(t.IsSplit ? t.CategoryLabel : t.IsTransfer ? "Transfer" : Categories.Display(t.Category)).At(2, cell);
        if (t.IsSplit) chip.ToolTip = string.Join("\n", t.Splits.Select(s => $"{Categories.Display(s.Category)}: {Money.Format(s.Amount)}"));
        g.Children.Add(chip);
        g.Children.Add(Ui.Text(t.Account, muted: true).At(3, cell));

        var amount = Ui.Text(t.IsTransfer ? Money.Format(t.Size) : Money.Signed(t.Amount), weight: FontWeights.SemiBold,
            brushKey: t.IsIncome ? "Brush.Positive" : t.IsTransfer ? "Brush.TextMuted" : "Brush.Text").At(4, cell);
        amount.HorizontalAlignment = HorizontalAlignment.Right;
        g.Children.Add(amount);

        return PageKit.Row(g, $"{t.Payee}, {Money.Signed(t.Amount)}, {t.Date:MMM d}", () => TransactionDialog.Edit(t), firstRow);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        // New transactions default to today, or the first day of the month being viewed if that's another month.
        var month = MonthSwitcher.Current;
        var date = Budgeting.InMonth(DateTime.Today, month) ? DateTime.Today : month;
        TransactionDialog.Edit(null, date);
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export($"coinpilot-transactions-{MonthSwitcher.Current:yyyy-MM}",
            ["Date", "Payee", "Category", "Account", "Transfer to", "Amount", "Tags", "Note"],
            _shown.Select(t => new[]
            {
                t.Date.ToString("yyyy-MM-dd"), t.Payee,
                t.IsSplit ? string.Join(" + ", t.Splits.Select(s => $"{s.Category} {s.Amount:0.00}")) : t.IsTransfer ? "Transfer" : t.Category,
                t.Account, t.TransferAccount, t.Amount.ToString("0.00", Money.Us), string.Join(", ", t.Tags), t.Note,
            }));

    private void Import_Click(object sender, RoutedEventArgs e) => ImportDialog.Run();
}
