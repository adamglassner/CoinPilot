using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>Add or edit a monthly budget for one spending category.</summary>
public sealed class BudgetDialog : DialogShell
{
    private readonly Budget _target;
    private readonly WrapPanel _category;
    private readonly TextBox _amount;
    private readonly bool _isNew;

    public Budget Result => _target;

    public BudgetDialog(Budget? existing, string? category = null)
        : base(existing == null ? "New budget" : $"{existing.Category} budget",
               existing == null
                   ? "Pick a spending category and how much you want to spend on it each month."
                   : "Change how much you want to spend each month, or delete this budget.",
               allowDelete: existing != null,
               saveText: existing == null ? "Create budget" : "Save")
    {
        _isNew = existing == null;
        _target = existing ?? new Budget();

        // Only categories that don't have a budget yet (plus this one when editing).
        var taken = DataStore.Budgets.Items.Where(b => b != existing).Select(b => b.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = Categories.Spending.Where(c => !taken.Contains(c)).ToList();
        if (existing != null && !options.Contains(existing.Category, StringComparer.OrdinalIgnoreCase)) options.Insert(0, existing.Category);
        string? pick = existing?.Category ?? category ?? options.FirstOrDefault();

        _category = AddField("Category", Chips(options, pick), required: true,
            hint: options.Count == 0 ? "Every category already has a budget." : null);
        _amount = AddField("Monthly amount", AmountBox(existing?.MonthlyLimit ?? (pick != null ? Categories.SuggestedLimit(pick) : (decimal?)null)),
            hint: "The budget repeats every month. Spending in this category counts against it.", required: true);

        // While creating, picking a category fills in a suggested amount until you type your own.
        if (_isNew)
        {
            bool typed = false;
            _amount.TextChanged += (_, _) => { if (_amount.IsKeyboardFocused) typed = true; };
            foreach (var chip in _category.Children.OfType<RadioButton>())
                chip.Checked += (_, _) =>
                {
                    if (!typed && Selected(_category) is string c)
                        _amount.Text = Categories.SuggestedLimit(c).ToString("0.00", Money.Us);
                };
        }

        Loaded += (_, _) => { _amount.Focus(); _amount.SelectAll(); };
    }

    protected override string? TrySave()
    {
        string? category = Selected(_category);
        if (category == null) return "Pick a category.";
        if (!Money.TryParse(_amount.Text, out var amount) || amount == 0)
            return "Enter a monthly amount greater than zero, like 600.";

        _target.Category = category;
        _target.MonthlyLimit = amount;
        return null;
    }

    /// <summary>Opens the dialog and saves the result. Returns true if anything changed.</summary>
    public static bool Edit(Budget? existing, string? category = null)
    {
        var dialog = new BudgetDialog(existing, category);
        if (dialog.ShowDialog() != true) return false;

        if (existing == null)
        {
            DataStore.Budgets.Add(dialog.Result);
            Toaster.Show($"Budget created: {dialog.Result.Category}, {Money.Format(dialog.Result.MonthlyLimit, cents: false)} a month.");
        }
        else if (dialog.DeleteRequested)
        {
            DataStore.Budgets.Delete(existing);
            Toaster.Show($"Deleted the {existing.Category} budget.");
        }
        else
        {
            DataStore.Budgets.Update(existing);
            Toaster.Show("Budget saved.");
        }
        return true;
    }
}
