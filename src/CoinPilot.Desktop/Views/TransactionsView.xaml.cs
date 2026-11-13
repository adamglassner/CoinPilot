using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>The Transactions page: one month of spending and income, searchable, with add / edit / delete and CSV export.</summary>
public partial class TransactionsView : UserControl
{
    // Date, Payee, Category, Account, Amount
    private static readonly double[] ColumnStars = [0.9, 2.4, 1.5, 1.1, 1.1];

    private List<Transaction> _shown = [];

    public TransactionsView()
    {
        InitializeComponent();
        BuildHeader();
        Month.MonthChanged += (_, _) => Refresh();
        Loaded += (_, _) => { DataStore.Changed += OnDataChanged; Refresh(); };
        Unloaded += (_, _) => DataStore.Changed -= OnDataChanged;
        Refresh();
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

    /// <summary>Rebuilds totals and the list for the current month and search.</summary>
    public void Refresh()
    {
        var summary = Budgeting.Summarize(MonthSwitcher.Current);
        SpentText.Text = Money.Format(summary.Spent);
        IncomeText.Text = Money.Format(summary.Income);
        NetText.Text = Money.Format(summary.Net);
        NetText.SetResourceReference(TextBlock.ForegroundProperty, summary.Net < 0 ? "Brush.Danger" : "Brush.Text");
        CountText.Text = summary.Transactions.Count.ToString("N0", Money.Us);

        string q = SearchBox.Text.Trim();
        _shown = summary.Transactions
            .Where(t => q.Length == 0 || Matches(t, q))
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .ToList();

        Rows.Children.Clear();
        foreach (var t in _shown) Rows.Children.Add(BuildRow(t));

        HeaderRow.Visibility = _shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ExportButton.IsEnabled = _shown.Count > 0;
        EmptyHost.Content = _shown.Count > 0 ? null
            : q.Length > 0
                ? Ui.EmptyState("", "No matches", $"Nothing in {Money.MonthName(MonthSwitcher.Current)} matches \"{q}\".")
                : Ui.EmptyState("", $"No transactions in {Money.MonthName(MonthSwitcher.Current)}",
                    "Add what you spend and earn and CoinPilot totals it up and tracks it against your budgets.",
                    "Add transaction", Add_Click);
    }

    private static bool Matches(Transaction t, string q) =>
        t.Payee.Contains(q, StringComparison.OrdinalIgnoreCase)
        || t.Category.Contains(q, StringComparison.OrdinalIgnoreCase)
        || t.Account.Contains(q, StringComparison.OrdinalIgnoreCase)
        || t.Note.Contains(q, StringComparison.OrdinalIgnoreCase)
        || Money.Format(t.Size).Contains(q, StringComparison.OrdinalIgnoreCase);

    private Button BuildRow(Transaction t)
    {
        var g = Ui.Columns(ColumnStars);
        var cell = new Thickness(10, 12, 10, 12);

        g.Children.Add(Ui.Text(t.Date.ToString("ddd, MMM d", Money.Us), muted: true).At(0, cell));

        var payee = new StackPanel { VerticalAlignment = VerticalAlignment.Center }.At(1, cell);
        payee.Children.Add(Ui.Text(t.Payee, weight: FontWeights.SemiBold));
        if (t.Note.Length > 0) payee.Children.Add(Ui.Text(t.Note.Replace('\n', ' '), muted: true, size: 12));
        g.Children.Add(payee);

        g.Children.Add(Ui.Chip(t.Category).At(2, cell));
        g.Children.Add(Ui.Text(t.Account, muted: true).At(3, cell));

        var amount = Ui.Text(Money.Signed(t.Amount), weight: FontWeights.SemiBold, brushKey: t.IsIncome ? "Brush.Positive" : "Brush.Text").At(4, cell);
        amount.HorizontalAlignment = HorizontalAlignment.Right;
        g.Children.Add(amount);

        var row = new Button { Content = g, Style = (Style)FindResource("RowButton"), ToolTip = "Edit or delete" };
        System.Windows.Automation.AutomationProperties.SetName(row, $"{t.Payee}, {Money.Signed(t.Amount)}, {t.Date:MMM d}");
        row.Click += (_, _) => TransactionDialog.Edit(t);
        return row;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        // New transactions default to today, or the 1st of the month being viewed if that's another month.
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
            ["Date", "Payee", "Category", "Account", "Amount", "Note"],
            _shown.Select(t => new[]
            {
                t.Date.ToString("yyyy-MM-dd"), t.Payee, t.Category, t.Account,
                t.Amount.ToString("0.00", Money.Us), t.Note,
            }));

    private void Import_Click(object sender, RoutedEventArgs e) => Toaster.Show("Importing bank statements is coming soon.");
}
