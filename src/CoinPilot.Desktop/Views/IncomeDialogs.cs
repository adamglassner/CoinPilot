using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>Add or edit repeating income: a paycheck, a side job, benefits…</summary>
public sealed class IncomeDialog : DialogShell
{
    private const string Same = "Same amount every time", Changes = "Changes each time";

    private readonly IncomeSource _target;
    private readonly TextBox _name;
    private readonly WrapPanel _kind;
    private readonly TextBox _amount;
    private readonly TextBlock _amountLabel;
    private readonly WrapPanel _repeats;
    private readonly DatePicker _next;
    private readonly TextBox _day2;
    private readonly WrapPanel _category;
    private readonly WrapPanel _account;
    private readonly CheckBox _autoAdd;
    private readonly TextBox _note;

    public IncomeSource Result => _target;

    public IncomeDialog(IncomeSource? existing)
        : base(existing == null ? "Add income" : "Edit income",
               existing == null ? "A paycheck or other money that comes in regularly. WillowWallet adds it on payday, or asks how much if it changes." : "Change the details, or delete it. Income already recorded stays in Transactions.",
               allowDelete: existing != null, saveText: existing == null ? "Add income" : "Save")
    {
        HeaderGlyph = "";
        _target = existing ?? new IncomeSource { NextDate = DateTime.Today.AddDays(1), Account = DataStore.AccountChoices()[0] };
        _name = AddField("Name", new TextBox { Text = _target.Name }, hint: "Like \"Paycheck · Acme Corp\", \"DoorDash\" or \"Child support\".", required: true);
        _kind = AddField("Amount", Chips([Same, Changes], _target.Varies ? Changes : Same));
        var amountRow = new StackPanel();
        _amountLabel = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        amountRow.Children.Add(_amountLabel);
        _amount = AmountBox(_target.Amount > 0 ? _target.Amount : null);
        AutomationProperties.SetName(_amount, "Amount");
        amountRow.Children.Add(_amount);
        Body.Children.Add(amountRow);
        _repeats = AddField("How often", Chips(IncomeSchedule.All, _target.Repeats));
        _next = AddField(existing == null ? "Next payday" : "Next payday", DateBox(_target.NextDate), required: true);
        _day2 = AddField("Also paid on day", new TextBox { Text = _target.Day2.ToString(), Width = 90, HorizontalAlignment = HorizontalAlignment.Left },
            hint: "Twice a month: the other day it comes, like the 1st and the 15th.");
        var categories = Categories.Income.ToList();
        if (_target.Category.Length > 0 && !categories.Contains(_target.Category, StringComparer.OrdinalIgnoreCase)) categories.Add(_target.Category);
        _category = AddField("Category", CategoryChips(categories, _target.Category));
        _account = AddField("Deposited to", Chips(DataStore.AccountChoices(_target.Account), _target.Account));
        _autoAdd = AddField("On payday", Check("Add it to Transactions automatically", _target.AutoAdd),
            hint: "Off: WillowWallet asks you to confirm each payday instead.");
        _note = AddField("Note", new TextBox { Text = _target.Note });

        foreach (var chip in _kind.Children.OfType<RadioButton>().Concat(_repeats.Children.OfType<RadioButton>()))
            chip.Checked += (_, _) => UpdateFields();
        UpdateFields();
        Loaded += (_, _) => _name.Focus();
    }

    private void UpdateFields()
    {
        bool varies = Selected(_kind) == Changes;
        _amountLabel.Text = varies ? "Typical amount (optional)" : "Amount each time";
        ShowField(_autoAdd, !varies);
        ShowField(_day2, Selected(_repeats) == IncomeSchedule.TwiceAMonth);
    }

    protected override string? TrySave()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0) return "Enter a name, like \"Paycheck\".";
        bool varies = Selected(_kind) == Changes;
        decimal amount = 0;
        if (!varies && !ReadAmount(_amount, allowZero: false, out amount)) return "Enter the amount you get each time (after taxes).";
        if (varies && _amount.Text.Trim().Length > 0 && !ReadAmount(_amount, allowZero: true, out amount)) return "Enter the typical amount as a number, or leave it empty.";
        if (_next.SelectedDate is not DateTime next) return "Pick the next payday.";
        string repeats = Selected(_repeats) ?? IncomeSchedule.EveryTwoWeeks;
        int day2 = _target.Day2;
        if (repeats == IncomeSchedule.TwiceAMonth && (!int.TryParse(_day2.Text.Trim(), out day2) || day2 is < 1 or > 31 || day2 == next.Day))
            return "Enter the other day of the month it's paid (1 to 31), different from the next payday's day.";

        _target.Name = name;
        _target.Varies = varies;
        _target.Amount = amount;
        if (_target.IsDone && (repeats != IncomeSchedule.Once || next.Date > _target.LastReceived!.Value.Date)) _target.LastReceived = null;
        _target.Repeats = repeats;
        _target.NextDate = next.Date;
        _target.Day1 = next.Day;
        _target.Day2 = day2;
        _target.Category = Selected(_category) ?? "Paycheck";
        _target.Account = Selected(_account) ?? DataStore.AccountChoices()[0];
        _target.AutoAdd = !varies && _autoAdd.IsChecked == true;
        _target.Note = _note.Text.Trim();
        return null;
    }

    public static bool Edit(IncomeSource? existing)
    {
        var dialog = new IncomeDialog(existing);
        if (dialog.ShowDialog() != true) return false;
        var s = dialog.Result;
        if (dialog.DeleteRequested && existing != null)
        {
            DataStore.Income.Delete(existing);
            Toaster.Show($"Deleted {existing.Name}. What it added to Transactions is kept.");
            return true;
        }
        if (existing == null) DataStore.Income.Add(s);
        else DataStore.Income.Update(s);
        var added = Paydays.AddDue(DateTime.Today);
        Toaster.Show((existing == null ? $"Added {s.Name}. Next payday {s.NextDate:MMM d}." : "Income saved.") +
                     (added.Count > 0 ? $" Recorded {added.Count} payday{(added.Count == 1 ? "" : "s")} that already came." : ""));
        if (Paydays.Asking(DateTime.Today).Count > 0) PaydayDialog.AskNow();
        return true;
    }
}

/// <summary>
/// Payday: for income that changes (or that you confirm each time), asks how much came in and adds it to Transactions.
/// Shown when WillowWallet opens on or after a payday, and from the Income page.
/// </summary>
public sealed class PaydayDialog : DialogShell
{
    private sealed record Row(IncomeSource Source, CheckBox Received, TextBox Amount, DateTime Date);
    private readonly List<Row> _rows = [];

    public int Recorded { get; private set; }

    public PaydayDialog(IReadOnlyList<IncomeSource> due)
        : base(due.Count == 1 ? $"Payday: {due[0].Name}" : "Payday",
               due.Count == 1
                   ? (due[0].Varies ? "How much came in? It's added to Transactions." : "Did it come in? It's added to Transactions.")
                   : "Enter what came in. Each one is added to Transactions; untick any that haven't arrived yet.",
               allowDelete: false, saveText: "Add to Transactions", width: 600)
    {
        HeaderGlyph = "";
        var list = new StackPanel();
        foreach (var s in due)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            var check = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(check, $"{s.Name} came in");
            Grid.SetColumn(check, 0);
            row.Children.Add(check);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = s.Name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            var typical = IncomeSchedule.Typical(s);
            var detail = new TextBlock
            {
                Text = $"{(s.NextDate.Date == DateTime.Today ? "Today" : s.NextDate.ToString("ddd, MMM d", Money.Us))} · into {(s.Account.Length > 0 ? s.Account : DataStore.AccountChoices()[0])}" +
                       (s.Varies && typical > 0 ? $" · usually about {Money.Format(typical)}" : ""),
                FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
            };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            words.Children.Add(detail);
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            var amount = AmountBox(s.Varies ? null : s.Amount);
            amount.Width = 160;
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            AutomationProperties.SetName(amount, $"{s.Name} amount");
            Grid.SetColumn(amount, 2);
            row.Children.Add(amount);
            check.Checked += (_, _) => amount.IsEnabled = true;
            check.Unchecked += (_, _) => amount.IsEnabled = false;
            list.Children.Add(row);
            _rows.Add(new Row(s, check, amount, s.NextDate.Date > DateTime.Today ? DateTime.Today : s.NextDate.Date));
        }
        Body.Children.Add(list);
        var hint = new TextBlock { Text = "Not here yet? Untick it and WillowWallet asks again next time it opens.", FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        Body.Children.Add(hint);
        Loaded += (_, _) => _rows.FirstOrDefault(r => r.Source.Varies)?.Amount.Focus();
    }

    protected override string? TrySave()
    {
        var ticked = _rows.Where(r => r.Received.IsChecked == true).ToList();
        foreach (var r in ticked)
            if (!ReadAmount(r.Amount, allowZero: false, out _)) return $"Enter how much {r.Source.Name} was, or untick it.";
        using (DataStore.Batch())
        {
            foreach (var r in ticked)
            {
                ReadAmount(r.Amount, allowZero: false, out var amount);
                Paydays.Receive(r.Source, amount, r.Date);
                Recorded++;
            }
        }
        return null;
    }

    private static bool _open;

    /// <summary>Asks about every payday that has come (once at a time). Returns how many were recorded.</summary>
    public static int AskNow()
    {
        var due = Paydays.Asking(DateTime.Today);
        if (_open || due.Count == 0 || !DataLock.IsOpen) return 0;
        _open = true;
        try
        {
            var dialog = new PaydayDialog(due);
            if (MainWindow.Instance is { IsVisible: true } w) dialog.Owner = w;
            if (dialog.ShowDialog() != true) return 0;
            Toaster.Show(dialog.Recorded == 0 ? "Nothing recorded." :
                dialog.Recorded == 1 ? "Income added to Transactions." : $"{dialog.Recorded} paydays added to Transactions.");
            return dialog.Recorded;
        }
        finally { _open = false; }
    }
}
