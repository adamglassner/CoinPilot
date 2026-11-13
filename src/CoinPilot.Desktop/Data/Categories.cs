namespace CoinPilot.Data;

/// <summary>
/// Spending and income categories. Until you edit them (Settings → Categories), the built-in lists are used.
/// The first edit saves the built-in list so your changes have something to build on.
/// A subcategory has a parent: budgets and reports for the parent include it.
/// </summary>
public static class Categories
{
    public static readonly string[] DefaultSpending =
    [
        "Groceries", "Dining out", "Housing", "Utilities", "Transportation", "Shopping",
        "Entertainment", "Health", "Personal care", "Kids", "Pets", "Travel", "Gifts", "Subscriptions",
        "Debt payments", "Savings", "Taxes", "Other",
    ];

    public static readonly string[] DefaultIncome = ["Paycheck", "Side income", "Refund", "Interest", "Gift received", "Other income"];

    /// <summary>Offered for transactions until you set up your own accounts on the Accounts page.</summary>
    public static readonly string[] DefaultAccounts = ["Checking", "Savings", "Credit card", "Cash"];

    /// <summary>Category used for debt payments recorded from the Debts page.</summary>
    public const string DebtPayments = "Debt payments";

    // ----- The list in use -----

    private static IEnumerable<CategoryDef> Defs()
    {
        var saved = DataStore.CategoryDefs.Items;
        if (saved.Count > 0) return saved.OrderBy(c => c.Order);
        return DefaultSpending.Select((n, i) => new CategoryDef { Name = n, Kind = CategoryKinds.Spending, Order = i })
            .Concat(DefaultIncome.Select((n, i) => new CategoryDef { Name = n, Kind = CategoryKinds.Income, Order = 100 + i }));
    }

    /// <summary>All category definitions (hidden ones included), parents followed by their subcategories.</summary>
    public static List<CategoryDef> AllDefs(string kind)
    {
        var defs = Defs().Where(c => c.Kind == kind).ToList();
        var result = new List<CategoryDef>();
        foreach (var top in defs.Where(c => c.Parent.Length == 0))
        {
            result.Add(top);
            result.AddRange(defs.Where(c => string.Equals(c.Parent, top.Name, StringComparison.OrdinalIgnoreCase)));
        }
        // Subcategories whose parent was removed.
        result.AddRange(defs.Where(c => !result.Contains(c)));
        return result;
    }

    /// <summary>Visible spending categories, parents followed by their subcategories.</summary>
    public static IReadOnlyList<string> Spending => AllDefs(CategoryKinds.Spending).Where(c => !c.IsHidden).Select(c => c.Name).ToList();

    /// <summary>Visible income categories.</summary>
    public static IReadOnlyList<string> Income => AllDefs(CategoryKinds.Income).Where(c => !c.IsHidden).Select(c => c.Name).ToList();

    private static CategoryDef? Find(string name) =>
        Defs().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    public static string ParentOf(string name) => Find(name)?.Parent ?? "";

    /// <summary>The top-level category: the parent for a subcategory, otherwise the category itself.</summary>
    public static string Root(string name)
    {
        string parent = ParentOf(name);
        return parent.Length > 0 ? parent : name;
    }

    /// <summary>"Dining out › Coffee" for a subcategory, otherwise the name.</summary>
    public static string Display(string name)
    {
        string parent = ParentOf(name);
        return parent.Length > 0 ? $"{parent} › {name}" : name;
    }

    public static IEnumerable<string> Children(string name) =>
        Defs().Where(c => string.Equals(c.Parent, name, StringComparison.OrdinalIgnoreCase)).Select(c => c.Name);

    public static bool IsIncome(string name) => Find(name)?.Kind == CategoryKinds.Income;

    // ----- Editing -----

    /// <summary>Saves the built-in list the first time categories are edited.</summary>
    public static void EnsureSaved()
    {
        if (DataStore.CategoryDefs.Items.Count > 0) return;
        DataStore.CategoryDefs.AddRange(Defs().ToList());
    }

    public static void Add(string name, string kind, string parent)
    {
        EnsureSaved();
        int order = DataStore.CategoryDefs.Items.Count == 0 ? 0 : DataStore.CategoryDefs.Items.Max(c => c.Order) + 1;
        DataStore.CategoryDefs.Add(new CategoryDef { Name = name.Trim(), Kind = kind, Parent = parent, Order = order });
    }

    /// <summary>Renames a category everywhere: transactions, splits, budgets, bills, rules and subcategories.</summary>
    public static void Rename(string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || string.Equals(oldName, newName, StringComparison.Ordinal)) return;
        EnsureSaved();
        bool Is(string s) => string.Equals(s, oldName, StringComparison.OrdinalIgnoreCase);

        using var _ = DataStore.Batch();
        foreach (var c in DataStore.CategoryDefs.Items)
        {
            if (Is(c.Name)) c.Name = newName;
            if (Is(c.Parent)) c.Parent = newName;
        }
        DataStore.CategoryDefs.UpdateMany([]);

        var txs = DataStore.Transactions.Items.Where(t => Is(t.Category) || t.Splits.Any(s => Is(s.Category))).ToList();
        foreach (var t in txs)
        {
            if (Is(t.Category)) t.Category = newName;
            foreach (var s in t.Splits.Where(s => Is(s.Category))) s.Category = newName;
        }
        if (txs.Count > 0) DataStore.Transactions.UpdateMany(txs);

        var budgets = DataStore.Budgets.Items.Where(b => Is(b.Category)).ToList();
        foreach (var b in budgets) b.Category = newName;
        if (budgets.Count > 0) DataStore.Budgets.UpdateMany(budgets);

        var bills = DataStore.Bills.Items.Where(b => Is(b.Category)).ToList();
        foreach (var b in bills) b.Category = newName;
        if (bills.Count > 0) DataStore.Bills.UpdateMany(bills);

        var rules = DataStore.Rules.Items.Where(r => Is(r.Category)).ToList();
        foreach (var r in rules) r.Category = newName;
        if (rules.Count > 0) DataStore.Rules.UpdateMany(rules);
    }

    public static bool IsUsed(string name) =>
        DataStore.Transactions.Items.Any(t => string.Equals(t.Category, name, StringComparison.OrdinalIgnoreCase)
                                              || t.Splits.Any(s => string.Equals(s.Category, name, StringComparison.OrdinalIgnoreCase)))
        || DataStore.Budgets.Items.Any(b => string.Equals(b.Category, name, StringComparison.OrdinalIgnoreCase))
        || DataStore.Bills.Items.Any(b => string.Equals(b.Category, name, StringComparison.OrdinalIgnoreCase));

    // ----- Automatic categories -----

    /// <summary>
    /// Suggests a category for a payee: your rules first (longest match wins), then the category you used the last
    /// time you paid this payee. Null when there's nothing to go on.
    /// </summary>
    public static string? Suggest(string payee, bool income)
    {
        payee = payee.Trim();
        if (payee.Length < 2) return null;

        var rule = DataStore.Rules.Items
            .Where(r => r.Contains.Length > 0 && payee.Contains(r.Contains, StringComparison.OrdinalIgnoreCase)
                        && IsIncome(r.Category) == income)
            .OrderByDescending(r => r.Contains.Length)
            .FirstOrDefault();
        if (rule != null) return rule.Category;

        var last = DataStore.Transactions.Items
            .Where(t => !t.IsTransfer && !t.IsSplit && t.Category.Length > 0 && (t.Amount > 0) == income
                        && string.Equals(t.Payee.Trim(), payee, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .FirstOrDefault();
        return last?.Category;
    }

    /// <summary>A suggested starting amount when someone budgets a category for the first time.</summary>
    public static decimal SuggestedLimit(string category) => category switch
    {
        "Housing" => 1500,
        "Groceries" => 600,
        "Utilities" => 250,
        "Transportation" => 300,
        "Dining out" => 250,
        "Shopping" => 200,
        "Entertainment" => 150,
        "Health" => 100,
        "Subscriptions" => 60,
        "Debt payments" => 300,
        "Savings" => 200,
        _ => 100,
    };
}
