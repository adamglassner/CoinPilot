using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>Add or edit one transaction: spending or income, amount, payee, category, account, date and a note.</summary>
public sealed class TransactionDialog : DialogShell
{
    private readonly Transaction _target;
    private readonly WrapPanel _kind;
    private readonly TextBox _amount;
    private readonly TextBox _payee;
    private readonly WrapPanel _category;
    private readonly WrapPanel _account;
    private readonly DatePicker _date;
    private readonly TextBox _note;
    private readonly Dictionary<bool, string?> _lastCategory = [];

    public Transaction Result => _target;

    /// <param name="existing">The transaction to edit, or null to add a new one.</param>
    /// <param name="defaultDate">Date for a new transaction (the month being viewed).</param>
    public TransactionDialog(Transaction? existing, DateTime? defaultDate = null)
        : base(existing == null ? "Add transaction" : "Edit transaction",
               existing == null ? "Record money you spent or received." : "Change the details, or delete it.",
               allowDelete: existing != null,
               saveText: existing == null ? "Add" : "Save")
    {
        _target = existing ?? new Transaction { Date = defaultDate ?? DateTime.Today, Account = Categories.Accounts[0] };
        bool income = existing?.IsIncome ?? false;
        _currentIncome = income;

        _kind = AddField("Type", Chips(["Spending", "Income"], income ? "Income" : "Spending"));
        _amount = AddField("Amount", AmountBox(existing?.Size), required: true);
        _payee = AddField("Payee", new TextBox { Text = _target.Payee }, hint: "Who you paid or who paid you, like \"Fresh Thyme Market\" or \"Paycheck\".", required: true);
        _category = AddField("Category", Chips(CategoriesFor(income, _target.Category), _target.Category), required: true);
        _account = AddField("Account", Chips(AccountsFor(_target.Account), _target.Account));
        _date = AddField("Date", new DatePicker { SelectedDate = _target.Date, Width = 200, HorizontalAlignment = HorizontalAlignment.Left }, required: true);
        _note = AddField("Note", new TextBox { Text = _target.Note, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 64, VerticalContentAlignment = VerticalAlignment.Top });

        // Switching Spending/Income swaps the category list (and remembers each side's pick).
        foreach (var chip in _kind.Children.OfType<RadioButton>())
            chip.Checked += (_, _) => SwitchKind(Selected(_kind) == "Income");

        Loaded += (_, _) => { _amount.Focus(); _amount.SelectAll(); };
    }

    private bool _currentIncome;

    private void SwitchKind(bool income)
    {
        if (income == _currentIncome) return;
        _lastCategory[_currentIncome] = Selected(_category);
        _currentIncome = income;
        string? pick = _lastCategory.GetValueOrDefault(income);
        SetChips(_category, CategoriesFor(income, pick), pick);
    }

    /// <summary>The built-in list, plus the record's own category if it isn't in it (so nothing is lost on edit).</summary>
    private static IEnumerable<string> CategoriesFor(bool income, string? current)
    {
        var list = (income ? Categories.Income : Categories.Spending).ToList();
        if (!string.IsNullOrEmpty(current) && !list.Contains(current, StringComparer.OrdinalIgnoreCase)) list.Add(current);
        return list;
    }

    private static IEnumerable<string> AccountsFor(string? current)
    {
        var list = Categories.Accounts.ToList();
        if (!string.IsNullOrEmpty(current) && !list.Contains(current, StringComparer.OrdinalIgnoreCase)) list.Add(current);
        return list;
    }

    protected override string? TrySave()
    {
        if (!Money.TryParse(_amount.Text, out var amount) || amount == 0)
            return "Enter an amount greater than zero, like 86.42.";
        if (string.IsNullOrWhiteSpace(_payee.Text))
            return "Enter who you paid or who paid you.";
        string? category = Selected(_category);
        if (category == null) return "Pick a category.";
        if (_date.SelectedDate is not DateTime date) return "Pick a date.";

        bool income = Selected(_kind) == "Income";
        _target.Amount = income ? amount : -amount;
        _target.Payee = _payee.Text.Trim();
        _target.Category = category;
        _target.Account = Selected(_account) ?? Categories.Accounts[0];
        _target.Date = date.Date;
        _target.Note = _note.Text.Trim();
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
            Toaster.Show($"Added {Money.Signed(dialog.Result.Amount)} · {dialog.Result.Payee}.");
        }
        else if (dialog.DeleteRequested)
        {
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
