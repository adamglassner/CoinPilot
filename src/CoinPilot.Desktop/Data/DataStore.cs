using System.IO;

namespace CoinPilot.Data;

/// <summary>
/// Everything CoinPilot saves, kept on this PC in %AppData%\CoinPilot\data\ (one JSON file per kind of record).
/// When sync arrives these files become the offline copy that syncs between devices.
/// </summary>
public static class DataStore
{
    public static readonly string DefaultFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoinPilot", "data");

    private static string? _folder;
    private static JsonStore<Transaction>? _transactions;
    private static JsonStore<Budget>? _budgets;
    private static JsonStore<Account>? _accounts;
    private static JsonStore<Debt>? _debts;
    private static JsonStore<Bill>? _bills;
    private static JsonStore<Goal>? _goals;
    private static DebtPlanSettings? _debtPlan;

    /// <summary>Where the data lives. The self-test points this at a temp folder before anything is loaded.</summary>
    public static string Folder => _folder ??= DefaultFolder;

    public static JsonStore<Transaction> Transactions => _transactions ??= Hook(new JsonStore<Transaction>(Folder, "transactions"));
    public static JsonStore<Budget> Budgets => _budgets ??= Hook(new JsonStore<Budget>(Folder, "budgets"));
    public static JsonStore<Account> Accounts => _accounts ??= Hook(new JsonStore<Account>(Folder, "accounts"));
    public static JsonStore<Debt> Debts => _debts ??= Hook(new JsonStore<Debt>(Folder, "debts"));
    public static JsonStore<Bill> Bills => _bills ??= Hook(new JsonStore<Bill>(Folder, "bills"));
    public static JsonStore<Goal> Goals => _goals ??= Hook(new JsonStore<Goal>(Folder, "goals"));

    /// <summary>Snowball/avalanche choice and extra monthly payment for the Debts payoff plan.</summary>
    public static DebtPlanSettings DebtPlan => _debtPlan ??= DebtPlanSettings.Load(Folder);

    /// <summary>Raised after anything changes. Pages listen to this to refresh.</summary>
    public static event EventHandler? Changed;

    public static void RaiseChanged() => Changed?.Invoke(null, EventArgs.Empty);

    /// <summary>Switches to another folder (used by the self-test). Reloads on next access.</summary>
    public static void UseFolder(string folder)
    {
        _folder = folder;
        _transactions = null;
        _budgets = null;
        _accounts = null;
        _debts = null;
        _bills = null;
        _goals = null;
        _debtPlan = null;
        RaiseChanged();
    }

    private static JsonStore<T> Hook<T>(JsonStore<T> store) where T : RecordBase
    {
        store.Changed += (_, _) => RaiseChanged();
        return store;
    }

    /// <summary>
    /// Account names to offer when entering a transaction or bill: open accounts first, then any other names already
    /// used, then the built-in defaults if there's nothing yet. <paramref name="current"/> is always included.
    /// </summary>
    public static List<string> AccountChoices(string? current = null)
    {
        var names = Accounts.Items.Where(a => !a.IsClosed).Select(a => a.Name).ToList();
        if (names.Count == 0) names.AddRange(Categories.Accounts);
        if (!string.IsNullOrEmpty(current) && !names.Contains(current, StringComparer.OrdinalIgnoreCase)) names.Add(current);
        return names;
    }
}

/// <summary>Saved in data\debt-plan.json next to the debts.</summary>
public sealed class DebtPlanSettings
{
    public string Strategy { get; set; } = DebtPlanner.Avalanche;
    public decimal ExtraPayment { get; set; }

    private string _path = "";

    public static DebtPlanSettings Load(string folder)
    {
        string path = Path.Combine(folder, "debt-plan.json");
        DebtPlanSettings? s = null;
        try
        {
            if (File.Exists(path)) s = System.Text.Json.JsonSerializer.Deserialize<DebtPlanSettings>(File.ReadAllText(path));
        }
        catch
        {
            // Fall back to defaults.
        }
        s ??= new DebtPlanSettings();
        s._path = path;
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, System.Text.Json.JsonSerializer.Serialize(this));
        }
        catch
        {
            // A plan preference isn't worth crashing over.
        }
        DataStore.RaiseChanged();
    }
}
