using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>Add or edit a monthly budget for one spending category (or subcategory).</summary>
public sealed class BudgetDialog : DialogShell
{
    private const string EveryMonth = "Every month";

    private readonly Budget _target;
    private readonly WrapPanel _category;
    private readonly TextBox _amount;
    private readonly WrapPanel? _scope;
    private readonly CheckBox _rollover;
    private readonly bool _isNew;
    private readonly DateTime _month;

    public Budget Result => _target;

    /// <param name="month">The budget month being viewed (for "only this month" amounts).</param>
    public BudgetDialog(Budget? existing, string? category = null, DateTime? month = null)
        : base(existing == null ? "New budget" : $"{Categories.Display(existing.Category)} budget",
               existing == null
                   ? "Pick a spending category and how much you want to spend on it each month."
                   : "Change how much you want to spend, or delete this budget.",
               allowDelete: existing != null,
               saveText: existing == null ? "Create budget" : "Save")
    {
        _isNew = existing == null;
        _target = existing ?? new Budget();
        _month = Budgeting.MonthStart(month ?? MonthSwitcher.Current);

        // Only categories that don't have a budget yet (plus this one when editing).
        var taken = DataStore.Budgets.Items.Where(b => b != existing).Select(b => b.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = Categories.Spending.Where(c => !taken.Contains(c)).ToList();
        if (existing != null && !options.Contains(existing.Category, StringComparer.OrdinalIgnoreCase)) options.Insert(0, existing.Category);
        string? pick = existing?.Category ?? category ?? options.FirstOrDefault();

        _category = AddField("Category", CategoryChips(options, pick), required: true,
            hint: options.Count == 0 ? "Every category already has a budget." : "A budget for a category also covers its subcategories (unless they have their own).");

        decimal? current = existing != null ? Budgeting.LimitFor(existing, _month) : pick != null ? Categories.SuggestedLimit(pick) : null;
        _amount = AddField("Amount", AmountBox(current), required: true);

        if (existing != null)
        {
            bool hasOverride = existing.Overrides.ContainsKey(Budgeting.Key(_month));
            _scope = AddField("Use this amount for", Chips([EveryMonth, $"Only {Money.MonthName(_month)}"], hasOverride ? $"Only {Money.MonthName(_month)}" : EveryMonth),
                hint: $"The usual amount is {Money.Format(existing.MonthlyLimit, cents: false)}. A one-month amount is handy for holidays or a big purchase.");
        }

        _rollover = AddField("Rollover", Check("Roll leftover money (or overspending) into next month", _target.Rollover),
            hint: "Good for things you don't buy every month, like clothes or car repairs: unspent money builds up.");

        // While creating, picking a category fills in a suggested amount until you type your own.
        if (_isNew)
        {
            bool typed = false;
            _amount.TextChanged += (_, _) => { if (_amount.IsKeyboardFocused) typed = true; };
            foreach (var chip in _category.Children.OfType<RadioButton>())
                chip.Checked += (_, _) =>
                {
                    if (!typed && Selected(_category) is string c)
                        _amount.Text = Money.Plain(Categories.SuggestedLimit(c));
                };
        }

        Loaded += (_, _) => { _amount.Focus(); _amount.SelectAll(); };
    }

    protected override string? TrySave()
    {
        string? category = Selected(_category);
        if (category == null) return "Pick a category.";
        if (!ReadAmount(_amount, allowZero: !_isNew, out var amount))
            return "Enter a monthly amount greater than zero, like 600.";

        _target.Category = category;
        bool onlyThisMonth = _scope != null && Selected(_scope) != EveryMonth;
        string key = Budgeting.Key(_month);
        if (onlyThisMonth) _target.Overrides[key] = amount;
        else
        {
            _target.MonthlyLimit = amount;
            _target.Overrides.Remove(key);
        }
        if (_rollover.IsChecked == true && !_target.Rollover) _target.StartMonth ??= _month;
        _target.Rollover = _rollover.IsChecked == true;
        if (_isNew) _target.StartMonth = _month;
        return null;
    }

    /// <summary>Opens the dialog and saves the result. Returns true if anything changed.</summary>
    public static bool Edit(Budget? existing, string? category = null, DateTime? month = null)
    {
        var dialog = new BudgetDialog(existing, category, month);
        if (dialog.ShowDialog() != true) return false;

        if (existing == null)
        {
            DataStore.Budgets.Add(dialog.Result);
            Toaster.Show($"Budget created: {Categories.Display(dialog.Result.Category)}, {Money.Format(dialog.Result.MonthlyLimit, cents: false)} a month.");
        }
        else if (dialog.DeleteRequested)
        {
            DataStore.Budgets.Delete(existing);
            Toaster.Show($"Deleted the {Categories.Display(existing.Category)} budget.");
        }
        else
        {
            DataStore.Budgets.Update(existing);
            Toaster.Show("Budget saved.");
        }
        return true;
    }
}

/// <summary>Sets up budgets from a template: 50/30/20, zero-based from your spending, or starter amounts.</summary>
public sealed class BudgetTemplateDialog : DialogShell
{
    public const string FiftyThirtyTwenty = "50/30/20", ZeroBased = "Zero-based", Starter = "Starter amounts";

    private static readonly string[] Needs = ["Housing", "Utilities", "Groceries", "Transportation", "Health", "Kids", "Pets", "Personal care", "Debt payments", "Taxes"];
    private static readonly string[] Wants = ["Dining out", "Entertainment", "Shopping", "Travel", "Gifts", "Subscriptions"];
    private static readonly string[] SavingsGroup = ["Savings"];

    private readonly TextBox _income;
    private readonly WrapPanel _template;
    private readonly WrapPanel _mode;
    private readonly StackPanel _preview = new();
    private readonly TextBlock _explain = new() { TextWrapping = System.Windows.TextWrapping.Wrap, Margin = new System.Windows.Thickness(0, 0, 0, 10) };
    private Dictionary<string, decimal> _plan = [];

    public BudgetTemplateDialog()
        : base("Set up budgets from a template", "Pick a method; CoinPilot works out amounts for each category. You can change any of them afterwards.",
               allowDelete: false, saveText: "Create budgets", width: 640)
    {
        decimal income = Budgeting.AverageIncome();
        _income = AddField("Monthly income (after tax)", AmountBox(income > 0 ? income : null), required: true,
            hint: income > 0 ? "Your average income over the last few months. Change it if needed." : "What you take home each month.");
        _template = AddField("Method", Chips([FiftyThirtyTwenty, ZeroBased, Starter], FiftyThirtyTwenty));
        Body.Children.Add(_explain);
        _mode = AddField("Existing budgets", Chips(["Keep them, add the rest", "Replace them"], "Keep them, add the rest"));
        Body.Children.Add(new System.Windows.Controls.Border { Style = (System.Windows.Style)FindResource("Card"), Padding = new System.Windows.Thickness(14, 8, 14, 8), Child = _preview });

        _income.TextChanged += (_, _) => Refresh();
        foreach (var chip in _template.Children.OfType<RadioButton>().Concat(_mode.Children.OfType<RadioButton>()))
            chip.Checked += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Works out the amounts for a template. Public so the self-test can check the math.</summary>
    public static Dictionary<string, decimal> Plan(string template, decimal income, IReadOnlyDictionary<string, decimal> averageSpending)
    {
        var plan = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var visible = Categories.Spending.ToHashSet(StringComparer.OrdinalIgnoreCase);
        decimal Round5(decimal v) => Math.Max(5, Math.Round(v / 5) * 5);

        if (template == Starter)
        {
            foreach (var c in new[] { "Housing", "Groceries", "Utilities", "Transportation", "Dining out", "Shopping", "Entertainment", "Health", "Subscriptions", "Savings" })
                if (visible.Contains(c)) plan[c] = Categories.SuggestedLimit(c);
            return plan;
        }

        if (template == ZeroBased)
        {
            foreach (var (c, avg) in averageSpending)
                if (avg > 0 && visible.Contains(c) && Categories.ParentOf(c).Length == 0) plan[c] = Round5(Math.Ceiling(avg));
            decimal left = income - plan.Values.Sum();
            if (left > 0 && visible.Contains("Savings")) plan["Savings"] = plan.GetValueOrDefault("Savings") + Math.Floor(left / 5) * 5;
            return plan;
        }

        // 50/30/20: needs, wants, savings; within each group by your past spending (or the starter amounts).
        void Group(string[] cats, decimal share)
        {
            var present = cats.Where(visible.Contains).ToList();
            if (present.Count == 0) return;
            var weights = present.ToDictionary(c => c, c => averageSpending.GetValueOrDefault(c) is var s && s > 0 ? s : 0m);
            if (weights.Values.Sum() == 0)
                foreach (var c in present.Where(c => c is not ("Kids" or "Pets" or "Taxes"))) weights[c] = Categories.SuggestedLimit(c);
            decimal total = weights.Values.Sum();
            if (total == 0) return;
            foreach (var (c, w) in weights.Where(kv => kv.Value > 0)) plan[c] = Round5(income * share * w / total);
        }
        Group(Needs, 0.50m);
        Group(Wants, 0.30m);
        Group(SavingsGroup, 0.20m);
        return plan;
    }

    private void Refresh()
    {
        string template = Selected(_template) ?? FiftyThirtyTwenty;
        _explain.Text = template switch
        {
            FiftyThirtyTwenty => "50% of income for needs (housing, bills, groceries, transport), 30% for wants (dining out, shopping, fun) and 20% for savings. Split within each group by what you've spent recently.",
            ZeroBased => "Every dollar gets a job: each category gets what you've spent on it recently, and anything left over goes to Savings.",
            _ => "Typical starting amounts for common categories. Adjust them to fit your life.",
        };
        _explain.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

        decimal income = Money.TryParse(_income.Text, out var i) ? i : 0;
        _plan = Plan(template, income, Budgeting.AverageSpending());
        bool replace = Selected(_mode) == "Replace them";
        var existing = DataStore.Budgets.Items.Select(b => b.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);

        _preview.Children.Clear();
        if (_plan.Count == 0)
        {
            _preview.Children.Add(Ui.Text(template == ZeroBased ? "There's no spending from the last few months to base budgets on yet. Try 50/30/20 or Starter amounts." : "Enter your monthly income.", muted: true)
                .Also(t => { t.TextWrapping = System.Windows.TextWrapping.Wrap; t.TextTrimming = System.Windows.TextTrimming.None; t.Margin = new System.Windows.Thickness(4, 8, 4, 8); }));
            return;
        }
        foreach (var (category, amount) in _plan.OrderByDescending(kv => kv.Value))
        {
            var row = new DockPanel { Margin = new System.Windows.Thickness(4, 5, 4, 5) };
            bool skip = !replace && existing.Contains(category);
            var value = Ui.Text(skip ? "already budgeted" : Money.Format(amount, cents: false), muted: skip, weight: skip ? System.Windows.FontWeights.Normal : System.Windows.FontWeights.SemiBold);
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(value);
            row.Children.Add(Ui.Text(Categories.Display(category), muted: skip));
            _preview.Children.Add(row);
        }
        var total = Ui.Text($"Total budgeted: {Money.Format(_plan.Values.Sum(), cents: false)} of {Money.Format(income, cents: false)} income", weight: System.Windows.FontWeights.SemiBold);
        total.Margin = new System.Windows.Thickness(4, 10, 4, 6);
        _preview.Children.Add(total);
    }

    protected override string? TrySave()
    {
        if (_plan.Count == 0) return "There's nothing to create yet.";
        bool replace = Selected(_mode) == "Replace them";
        var month = Budgeting.MonthStart(DateTime.Today);
        using (DataStore.Batch())
        {
            foreach (var (category, amount) in _plan)
            {
                var existing = DataStore.Budgets.Items.FirstOrDefault(b => string.Equals(b.Category, category, StringComparison.OrdinalIgnoreCase));
                if (existing == null) DataStore.Budgets.Add(new Budget { Category = category, MonthlyLimit = amount, StartMonth = month });
                else if (replace)
                {
                    existing.MonthlyLimit = amount;
                    DataStore.Budgets.Update(existing);
                }
            }
        }
        return null;
    }
}
