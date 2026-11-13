using System.IO;
using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>
/// Add or edit one transaction: spending, income or a transfer between accounts; amount, payee, category (or a split
/// across several), account, date, tags, a note and an optional receipt.
/// </summary>
public sealed class TransactionDialog : DialogShell
{
    private const string Spending = "Spending", Income = "Income", Transfer = "Transfer";

    private readonly Transaction _target;
    private readonly bool _isNew;
    private readonly WrapPanel _kind;
    private readonly TextBox _amount;
    private readonly TextBox _payee;
    private readonly WrapPanel _category;
    private readonly Button _splitToggle;
    private readonly StackPanel _splitPanel = new();
    private readonly StackPanel _splitRows = new();
    private readonly TextBlock _splitRemaining = new() { FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0) };
    private readonly WrapPanel _account;
    private readonly WrapPanel _toAccount;
    private readonly DatePicker _date;
    private readonly TextBox _tags;
    private readonly TextBox _note;
    private readonly CheckBox _remember;
    private readonly StackPanel _receiptPanel = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<string, string?> _lastCategory = [];

    private string _currentKind;
    private bool _categoryTouched;
    private bool _splitMode;
    private string _receipt;
    private string? _newReceiptSource;
    private bool _removeReceipt;

    public Transaction Result => _target;

    /// <param name="existing">The transaction to edit, or null to add a new one.</param>
    /// <param name="defaultDate">Date for a new transaction.</param>
    public TransactionDialog(Transaction? existing, DateTime? defaultDate = null)
        : base(existing == null ? "Add transaction" : "Edit transaction",
               existing == null ? "Record money you spent, received or moved between accounts." : "Change the details, or delete it.",
               allowDelete: existing != null,
               saveText: existing == null ? "Add" : "Save", width: 620)
    {
        _isNew = existing == null;
        var accounts = DataStore.AccountChoices();
        _target = existing ?? new Transaction { Date = defaultDate ?? DateTime.Today, Account = accounts[0] };
        _currentKind = existing == null ? Spending : existing.IsTransfer ? Transfer : existing.IsIncome ? Income : Spending;
        _receipt = _target.Receipt;
        _categoryTouched = existing != null;

        _kind = AddField("Type", Chips([Spending, Income, Transfer], _currentKind));
        _amount = AddField("Amount", AmountBox(existing?.Size), required: true);
        _payee = AddField("Payee", new TextBox { Text = _target.Payee },
            hint: "Who you paid or who paid you. CoinPilot remembers the category you use for each payee.", required: true);

        _category = AddField("Category", CategoryChips(CategoriesFor(_currentKind, _target.Category), _target.IsSplit ? null : _target.Category), required: true);
        _splitToggle = new Button { Style = (Style)FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, -6, 0, 10) };
        _splitToggle.Click += (_, _) => SetSplitMode(!_splitMode);
        Body.Children.Add(_splitToggle);
        BuildSplitPanel();

        _remember = Check("", false);
        _remember.Margin = new Thickness(0, -4, 0, 12);
        Body.Children.Add(_remember);

        _account = AddField(_currentKind == Transfer ? "From account" : "Account", Chips(DataStore.AccountChoices(_target.Account), _target.Account));
        string to = existing?.TransferAccount is { Length: > 0 } t ? t : accounts.FirstOrDefault(a => !string.Equals(a, _target.Account, StringComparison.OrdinalIgnoreCase)) ?? accounts[0];
        _toAccount = AddField("To account", Chips(DataStore.AccountChoices(to), to));
        _date = AddField("Date", DateBox(_target.Date), required: true);
        _tags = AddField("Tags", new TextBox { Text = string.Join(", ", _target.Tags) },
            hint: "Optional, separated by commas, like \"vacation, work\". Search and filters can use them.");
        _note = AddField("Note", new TextBox { Text = _target.Note, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 60, VerticalContentAlignment = VerticalAlignment.Top });
        AddField("Receipt", _receiptPanel);
        BuildReceiptRow();

        foreach (var chip in _kind.Children.OfType<RadioButton>())
            chip.Checked += (_, _) => SwitchKind(Selected(_kind) ?? Spending);
        HookCategoryChips();
        _payee.LostKeyboardFocus += (_, _) => SuggestCategory();
        _payee.TextChanged += (_, _) => UpdateRemember();

        if (existing?.IsSplit == true) SetSplitMode(true, loadExisting: true);
        else SetSplitMode(false);
        ApplyKindVisibility();
        UpdateRemember();
        Loaded += (_, _) => { _amount.Focus(); _amount.SelectAll(); };
    }

    /// <summary>Opens the dialog as a transfer between accounts (Accounts → Transfer money).</summary>
    public void PresetTransfer()
    {
        foreach (var chip in _kind.Children.OfType<RadioButton>())
            if (chip.Tag as string == Transfer) chip.IsChecked = true;
    }

    // ----- Spending / income / transfer -----

    private static IEnumerable<string> CategoriesFor(string kind, string? current)
    {
        var list = (kind == Income ? Categories.Income : Categories.Spending).ToList();
        if (!string.IsNullOrEmpty(current) && kind != Transfer && !list.Contains(current, StringComparer.OrdinalIgnoreCase)) list.Add(current);
        return list;
    }

    private void SwitchKind(string kind)
    {
        if (kind == _currentKind) return;
        if (_currentKind != Transfer) _lastCategory[_currentKind] = Selected(_category);
        _currentKind = kind;
        if (kind != Transfer)
        {
            string? pick = _lastCategory.GetValueOrDefault(kind);
            SetCategoryChips(_category, CategoriesFor(kind, pick), pick);
            HookCategoryChips();
            if (pick == null) { _categoryTouched = false; SuggestCategory(); }
        }
        if (kind != Spending && _splitMode) SetSplitMode(false);
        ApplyKindVisibility();
        UpdateRemember();
    }

    private void ApplyKindVisibility()
    {
        bool transfer = _currentKind == Transfer;
        ShowField(_category, !transfer && !_splitMode);
        _splitToggle.Visibility = _currentKind == Spending ? Visibility.Visible : Visibility.Collapsed;
        _splitPanel.Visibility = _splitMode && _currentKind == Spending ? Visibility.Visible : Visibility.Collapsed;
        ShowField(_toAccount, transfer);
        if (LogicalTreeHelper.GetParent(_account) is StackPanel p && p.Children[0] is TextBlock label)
        {
            label.Inlines.Clear();
            label.Inlines.Add(transfer ? "From account" : "Account");
        }
        if (LogicalTreeHelper.GetParent(_payee) is StackPanel pp && pp.Children[0] is TextBlock payeeLabel)
        {
            payeeLabel.Inlines.Clear();
            payeeLabel.Inlines.Add(transfer ? "Description" : "Payee");
            if (!transfer)
            {
                var star = new System.Windows.Documents.Run(" *");
                star.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Danger");
                payeeLabel.Inlines.Add(star);
            }
        }
    }

    private void HookCategoryChips()
    {
        foreach (var chip in _category.Children.OfType<RadioButton>())
        {
            chip.PreviewMouseLeftButtonDown += (_, _) => _categoryTouched = true;
            chip.PreviewKeyDown += (_, _) => _categoryTouched = true;
            chip.Checked += (_, _) => UpdateRemember();
        }
    }

    /// <summary>Picks the category from your rules or your last transaction with this payee, unless you already chose one.</summary>
    private void SuggestCategory()
    {
        if (_categoryTouched || _currentKind == Transfer || _splitMode) return;
        var suggestion = Categories.Suggest(_payee.Text, _currentKind == Income);
        if (suggestion == null) return;
        if (!_category.Children.OfType<RadioButton>().Any(r => string.Equals(r.Tag as string, suggestion, StringComparison.OrdinalIgnoreCase)))
        {
            SetCategoryChips(_category, CategoriesFor(_currentKind, suggestion), suggestion);
            HookCategoryChips();
        }
        foreach (var chip in _category.Children.OfType<RadioButton>())
            if (string.Equals(chip.Tag as string, suggestion, StringComparison.OrdinalIgnoreCase)) chip.IsChecked = true;
    }

    private void UpdateRemember()
    {
        string payee = _payee.Text.Trim();
        string? category = Selected(_category);
        bool hasRule = DataStore.Rules.Items.Any(r => string.Equals(r.Contains, payee, StringComparison.OrdinalIgnoreCase));
        bool show = _currentKind != Transfer && !_splitMode && payee.Length >= 2 && category != null && !hasRule;
        _remember.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _remember.Content = show ? $"Always use \"{Categories.Display(category!)}\" for payees containing \"{payee}\"" : "";
    }

    // ----- Splits -----

    private void BuildSplitPanel()
    {
        var head = new TextBlock { Text = "Split across categories", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        _splitPanel.Children.Add(head);
        _splitPanel.Children.Add(_splitRows);
        var add = new Button { Content = "+ Add a category", Style = (Style)FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 4, 0, 0) };
        add.Click += (_, _) => { AddSplitRow(null, null); UpdateSplitRemaining(); };
        _splitPanel.Children.Add(add);
        _splitPanel.Children.Add(_splitRemaining);
        _splitPanel.Margin = new Thickness(0, 0, 0, 14);
        Body.Children.Insert(Body.Children.IndexOf(_splitToggle), _splitPanel);
    }

    private void SetSplitMode(bool on, bool loadExisting = false)
    {
        _splitMode = on;
        _splitToggle.Content = on ? "Don't split" : "Split across categories…";
        if (on && _splitRows.Children.Count == 0)
        {
            if (loadExisting) foreach (var s in _target.Splits) AddSplitRow(s.Category, s.Amount);
            else
            {
                AddSplitRow(Selected(_category), Money.TryParse(_amount.Text, out var a) ? a : null);
                AddSplitRow(null, null);
            }
        }
        UpdateSplitRemaining();
        ApplyKindVisibility();
        UpdateRemember();
    }

    private void AddSplitRow(string? category, decimal? amount)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var combo = new ComboBox { Margin = new Thickness(0, 0, 8, 0) };
        foreach (var c in Categories.Spending) combo.Items.Add(new ComboBoxItem { Content = Categories.Display(c), Tag = c });
        if (category != null)
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, category, StringComparison.OrdinalIgnoreCase));
        System.Windows.Automation.AutomationProperties.SetName(combo, "Split category");
        grid.Children.Add(combo);

        var box = new TextBox { Text = amount is decimal a && a != 0 ? Money.Plain(a) : "" };
        System.Windows.Automation.AutomationProperties.SetName(box, "Split amount");
        box.TextChanged += (_, _) => UpdateSplitRemaining();
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);

        var remove = new Button { Content = "", Style = (Style)FindResource("IconButton"), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Remove" };
        System.Windows.Automation.AutomationProperties.SetName(remove, "Remove split");
        remove.Click += (_, _) => { _splitRows.Children.Remove(grid); UpdateSplitRemaining(); };
        Grid.SetColumn(remove, 2);
        grid.Children.Add(remove);

        _splitRows.Children.Add(grid);
    }

    private List<(string? Category, decimal? Amount)> ReadSplits() =>
        _splitRows.Children.OfType<Grid>().Select(g =>
        {
            var combo = g.Children.OfType<ComboBox>().First();
            var box = g.Children.OfType<TextBox>().First();
            string? cat = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
            decimal? amt = string.IsNullOrWhiteSpace(box.Text) ? 0 : Money.TryParse(box.Text, out var a) ? a : null;
            return (cat, amt);
        }).ToList();

    private void UpdateSplitRemaining()
    {
        if (!_splitMode) return;
        decimal total = Money.TryParse(_amount.Text, out var t) ? t : 0;
        decimal used = ReadSplits().Sum(s => s.Amount ?? 0);
        decimal left = total - used;
        _splitRemaining.Text = left == 0 ? "Splits add up to the total." : left > 0 ? $"{Money.Format(left)} left to assign" : $"{Money.Format(-left)} more than the total";
        _splitRemaining.SetResourceReference(TextBlock.ForegroundProperty, left == 0 ? "Brush.Positive" : "Brush.Warn");
    }

    // ----- Receipt -----

    private void BuildReceiptRow()
    {
        _receiptPanel.Children.Clear();
        bool has = (_receipt.Length > 0 && !_removeReceipt) || _newReceiptSource != null;
        if (has)
        {
            string name = _newReceiptSource != null ? Path.GetFileName(_newReceiptSource) : "Receipt attached";
            var icon = new TextBlock { Text = "\uE723", Style = (Style)FindResource("Icon"), Margin = new Thickness(0, 0, 8, 0) };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            _receiptPanel.Children.Add(icon);
            var label = Ui.Text(name, size: 13);
            label.Margin = new Thickness(0, 0, 12, 0);
            _receiptPanel.Children.Add(label);
            if (_newReceiptSource == null)
            {
                var open = new Button { Content = "Open", Style = (Style)FindResource("LinkButton") };
                open.Click += (_, _) => OpenReceipt(_target);
                _receiptPanel.Children.Add(open);
            }
            var remove = new Button { Content = "Remove", Style = (Style)FindResource("LinkButton") };
            remove.Click += (_, _) => { _newReceiptSource = null; _removeReceipt = true; BuildReceiptRow(); };
            _receiptPanel.Children.Add(remove);
        }
        else
        {
            var attach = new Button { Content = "Attach a photo or PDF…", Style = (Style)FindResource("GhostButton") };
            attach.Click += (_, _) =>
            {
                var pick = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Attach a receipt",
                    Filter = "Receipts (*.jpg;*.jpeg;*.png;*.heic;*.pdf)|*.jpg;*.jpeg;*.png;*.heic;*.pdf|All files (*.*)|*.*",
                };
                if (pick.ShowDialog(this) == true)
                {
                    _newReceiptSource = pick.FileName;
                    BuildReceiptRow();
                }
            };
            _receiptPanel.Children.Add(attach);
        }
    }

    public static void OpenReceipt(Transaction t)
    {
        try { FileVault.Open(FileVault.ReceiptPath(t.Receipt), $"Receipt {t.Payee} {t.Date:yyyy-MM-dd}"); }
        catch (Exception ex) { Toaster.Show($"Couldn't open the receipt: {ex.Message}"); }
    }

    // ----- Save -----

    protected override string? TrySave()
    {
        if (!ReadAmount(_amount, allowZero: false, out var amount)) return "Enter an amount greater than zero, like 86.42.";
        string payee = _payee.Text.Trim();
        if (_date.SelectedDate is not DateTime date) return "Pick a date.";
        string account = Selected(_account) ?? DataStore.AccountChoices()[0];
        var tags = _tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        string? category = null;
        List<Split> splits = [];
        string transferTo = "";
        switch (_currentKind)
        {
            case Transfer:
                transferTo = Selected(_toAccount) ?? "";
                if (transferTo.Length == 0) return "Pick the account the money went to.";
                if (string.Equals(transferTo, account, StringComparison.OrdinalIgnoreCase)) return "A transfer needs two different accounts.";
                if (payee.Length == 0) payee = $"Transfer to {transferTo}";
                category = "Transfer";
                break;
            default:
                if (payee.Length == 0) return "Enter who you paid or who paid you.";
                if (_splitMode)
                {
                    var rows = ReadSplits().Where(s => s.Category != null || (s.Amount ?? 0) != 0).ToList();
                    if (rows.Count < 2) return "A split needs at least two categories.";
                    if (rows.Any(s => s.Category == null)) return "Pick a category for every split.";
                    if (rows.Any(s => s.Amount is null or <= 0m)) return "Enter an amount for every split.";
                    if (rows.Sum(s => s.Amount!.Value) != amount) return $"The splits add up to {Money.Format(rows.Sum(s => s.Amount!.Value))}, but the total is {Money.Format(amount)}.";
                    splits = rows.Select(s => new Split { Category = s.Category!, Amount = s.Amount!.Value }).ToList();
                    category = "Split";
                }
                else
                {
                    category = Selected(_category);
                    if (category == null) return "Pick a category.";
                }
                break;
        }

        // Receipt file changes.
        try
        {
            if (_newReceiptSource != null)
            {
                if (_receipt.Length > 0) FileVault.Delete(FileVault.ReceiptPath(_receipt));
                _receipt = FileVault.StoreReceipt(_newReceiptSource);
            }
            else if (_removeReceipt && _receipt.Length > 0)
            {
                FileVault.Delete(FileVault.ReceiptPath(_receipt));
                _receipt = "";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't attach the receipt: {ex.Message}";
        }

        _target.Amount = _currentKind == Income ? amount : -amount;
        _target.Payee = payee;
        _target.Category = category!;
        _target.Splits = splits;
        _target.TransferAccount = transferTo;
        _target.Account = account;
        _target.Date = date.Date;
        _target.Tags = tags;
        _target.Note = _note.Text.Trim();
        _target.Receipt = _receipt;

        if (_remember.IsVisible && _remember.IsChecked == true && category != null)
            DataStore.Rules.Add(new CategoryRule { Contains = payee, Category = category });
        return null;
    }

    /// <summary>Opens the dialog and saves the result. Returns true if anything changed.</summary>
    public static bool Edit(Transaction? existing, DateTime? defaultDate = null)
    {
        var dialog = new TransactionDialog(existing, defaultDate);
        if (dialog.ShowDialog() != true) return false;

        if (existing == null)
        {
            DataStore.Transactions.Add(dialog.Result);
            var t = dialog.Result;
            Toaster.Show(t.IsTransfer ? $"Moved {Money.Format(t.Size)} from {t.Account} to {t.TransferAccount}." : $"Added {Money.Signed(t.Amount)} · {t.Payee}.");
        }
        else if (dialog.DeleteRequested)
        {
            if (existing.Receipt.Length > 0) FileVault.Delete(FileVault.ReceiptPath(existing.Receipt));
            DataStore.Transactions.Delete(existing);
            Toaster.Show($"Deleted {existing.Payee}.");
        }
        else
        {
            DataStore.Transactions.Update(existing);
            Toaster.Show("Transaction saved.");
        }
        return true;
    }
}
