using System.IO;
using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>
/// Imports a bank or card statement (CSV, OFX or QFX) into one account: pick the account, check the columns (CSV),
/// preview what will be added, and skip what's already there.
/// </summary>
public sealed class ImportDialog : DialogShell
{
    private readonly Statement _statement;
    private readonly WrapPanel _account;
    private readonly CsvMapping _map;
    private readonly StackPanel _preview = new();
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
    private readonly CheckBox? _updateBalance;
    private List<ImportedRow> _rows = [];
    private int _skipped;

    public int Imported { get; private set; }

    public ImportDialog(string path, Statement statement)
        : base("Import transactions", $"{Path.GetFileName(path)} · {statement.Format} file", allowDelete: false, saveText: "Import", width: 760)
    {
        _statement = statement;
        _map = Importer.GuessMapping(statement.Headers);

        var accounts = DataStore.AccountChoices();
        _account = AddField("Import into", Chips(accounts, accounts[0]), hint: "The account this statement is for. New transactions are added to it.", required: true);
        foreach (var chip in _account.Children.OfType<RadioButton>()) chip.Checked += (_, _) => Refresh();

        if (statement.Format == "CSV") BuildMappingFields();

        if (statement.LedgerBalance is decimal bal && statement.LedgerDate is DateTime asOf)
        {
            _updateBalance = AddField("Balance", Check($"Set the account's balance to {Money.Format(bal)} as of {asOf:MMM d, yyyy} (from the statement)", true),
                hint: "Only for accounts set up on the Accounts page.");
        }

        Body.Children.Add(_summary);
        var previewCard = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(12, 6, 12, 6), Child = _preview };
        Body.Children.Add(previewCard);
        Refresh();
    }

    // ----- CSV column mapping -----

    private void BuildMappingFields()
    {
        var grid = new Grid();
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        void Add(int index, string label, int value, Action<int> set, bool optional)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 10) };
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
            var combo = new ComboBox();
            combo.Items.Add(new ComboBoxItem { Content = optional ? "(none)" : "Choose a column", Tag = -1 });
            for (int i = 0; i < _statement.Headers.Length; i++)
                combo.Items.Add(new ComboBoxItem { Content = _statement.Headers[i].Length > 0 ? _statement.Headers[i] : $"Column {i + 1}", Tag = i });
            combo.SelectedIndex = value + 1;
            combo.SelectionChanged += (_, _) => { set((int)((ComboBoxItem)combo.SelectedItem).Tag); Refresh(); };
            System.Windows.Automation.AutomationProperties.SetName(combo, label);
            panel.Children.Add(combo);
            Grid.SetColumn(panel, index % 3);
            Grid.SetRow(panel, index / 3);
            grid.Children.Add(panel);
        }

        Add(0, "Date", _map.Date, v => _map.Date = v, false);
        Add(1, "Payee / description", _map.Payee, v => _map.Payee = v, false);
        Add(2, "Amount", _map.Amount, v => _map.Amount = v, true);
        Add(3, "Or: money out (debit)", _map.Debit, v => _map.Debit = v, true);
        Add(4, "Or: money in (credit)", _map.Credit, v => _map.Credit = v, true);
        Add(5, "Category (optional)", _map.Category, v => _map.Category = v, true);
        AddField("Columns", grid, hint: "WillowWallet guessed these from the file's headings. Use Amount, or the two money out / money in columns.");

        var flip = Check("Spending shows as positive numbers in this file (common for credit cards)", _map.FlipSign);
        flip.Checked += (_, _) => { _map.FlipSign = true; Refresh(); };
        flip.Unchecked += (_, _) => { _map.FlipSign = false; Refresh(); };
        AddField("Signs", flip);
    }

    // ----- Preview -----

    private void Refresh()
    {
        string account = Selected(_account) ?? "";
        _rows = _statement.Format == "CSV" ? Importer.Apply(_statement, _map, out _skipped) : _statement.Rows.ToList();
        if (_statement.Format != "CSV") _skipped = 0;
        Importer.MarkDuplicates(_rows, account);

        int fresh = _rows.Count(r => !r.IsDuplicate), dupes = _rows.Count - fresh;
        if (_statement.Format == "CSV" && !_map.IsComplete)
            _summary.Text = "Choose the Date, Payee and Amount columns to see what will be imported.";
        else
        {
            var parts = new List<string> { fresh == 1 ? "1 new transaction will be added" : $"{fresh} new transactions will be added" };
            if (dupes > 0) parts.Add(dupes == 1 ? "1 is already in WillowWallet and will be skipped" : $"{dupes} are already in WillowWallet and will be skipped");
            if (_skipped > 0) parts.Add(_skipped == 1 ? "1 row couldn't be read" : $"{_skipped} rows couldn't be read");
            _summary.Text = string.Join(" · ", parts) + ".";
        }

        _preview.Children.Clear();
        var cols = new[] { 1.0, 2.6, 1.6, 1.1 };
        var head = Ui.Columns(cols);
        string[] names = ["DATE", "PAYEE", "CATEGORY", "AMOUNT"];
        for (int i = 0; i < names.Length; i++)
        {
            var t = Ui.Text(names[i], muted: true, size: 11, weight: FontWeights.SemiBold).At(i, new Thickness(6, 6, 6, 6));
            if (i == 3) t.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(t);
        }
        _preview.Children.Add(head);

        foreach (var r in _rows.OrderByDescending(r => r.Date).Take(10))
        {
            var g = Ui.Columns(cols);
            g.Opacity = r.IsDuplicate ? 0.45 : 1;
            var cell = new Thickness(6, 7, 6, 7);
            g.Children.Add(Ui.Text(r.Date.ToString("MMM d, yyyy", Money.Us), muted: true, size: 12.5).At(0, cell));
            g.Children.Add(Ui.Text(r.IsDuplicate ? $"{r.Payee}  (already added)" : r.Payee, size: 12.5).At(1, cell));
            g.Children.Add(Ui.Text(Categories.Display(Importer.CategoryFor(r)), muted: true, size: 12.5).At(2, cell));
            var amount = Ui.Text(Money.Signed(r.Amount), size: 12.5, weight: FontWeights.SemiBold, brushKey: r.Amount > 0 ? "Brush.Positive" : "Brush.Text").At(3, cell);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(amount);
            _preview.Children.Add(g);
        }
        if (_rows.Count > 10)
        {
            var more = Ui.Text($"…and {_rows.Count - 10} more", muted: true, size: 12);
            more.Margin = new Thickness(6, 4, 0, 6);
            _preview.Children.Add(more);
        }
        if (_rows.Count == 0)
        {
            var none = Ui.Text("Nothing to show yet.", muted: true);
            none.Margin = new Thickness(6, 8, 0, 8);
            _preview.Children.Add(none);
        }
    }

    protected override string? TrySave()
    {
        string? account = Selected(_account);
        if (account == null) return "Pick the account to import into.";
        if (_statement.Format == "CSV" && !_map.IsComplete) return "Choose the Date, Payee and Amount columns first.";
        if (_rows.Count(r => !r.IsDuplicate) == 0 && _updateBalance?.IsChecked != true) return "There's nothing new to import from this file.";

        using (DataStore.Batch())
        {
            Imported = Importer.Commit(_rows, account);
            if (_updateBalance?.IsChecked == true && _statement.LedgerBalance is decimal bal && _statement.LedgerDate is DateTime asOf)
            {
                var acc = DataStore.Accounts.Items.FirstOrDefault(a => string.Equals(a.Name, account, StringComparison.OrdinalIgnoreCase));
                if (acc != null)
                {
                    acc.StartingBalance = bal;
                    acc.BalanceDate = asOf.Date;
                    DataStore.Accounts.Update(acc);
                }
            }
        }
        return null;
    }

    /// <summary>Asks for a statement file and imports it. Returns true if anything was added.</summary>
    public static bool Run()
    {
        var pick = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a bank or card statement",
            Filter = "Bank statements (*.csv;*.ofx;*.qfx)|*.csv;*.ofx;*.qfx|CSV files (*.csv)|*.csv|OFX / QFX files (*.ofx;*.qfx)|*.ofx;*.qfx|All files (*.*)|*.*",
        };
        if (pick.ShowDialog() != true) return false;

        Statement statement;
        try
        {
            statement = Importer.Read(pick.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Toaster.Show($"Couldn't read that file: {ex.Message}");
            return false;
        }
        if (statement.Format == "CSV" ? statement.Cells.Count == 0 : statement.Rows.Count == 0 && statement.LedgerBalance == null)
        {
            Toaster.Show("That file doesn't have any transactions WillowWallet can read.");
            return false;
        }

        var dialog = new ImportDialog(pick.FileName, statement);
        if (dialog.ShowDialog() != true) return false;
        Toaster.Show(dialog.Imported == 1 ? "Imported 1 transaction." : $"Imported {dialog.Imported} transactions.");
        return true;
    }
}
