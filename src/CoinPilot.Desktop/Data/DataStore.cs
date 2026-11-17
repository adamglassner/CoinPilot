using System.IO;

namespace CoinPilot.Data;

/// <summary>
/// Everything WillowWallet saves, kept on this PC in %AppData%\WillowWallet\data\ (one JSON file per kind of record,
/// plus receipts\ and taxes\ for attached files). When sync arrives these become the offline copy.
/// </summary>
public static class DataStore
{
    public static readonly string DefaultFolder = Path.Combine(CoinPilot.Services.AppPaths.Root, "data");

    private static string? _folder;
    private static JsonStore<Transaction>? _transactions;
    private static JsonStore<Budget>? _budgets;
    private static JsonStore<Account>? _accounts;
    private static JsonStore<Debt>? _debts;
    private static JsonStore<Bill>? _bills;
    private static JsonStore<Goal>? _goals;
    private static JsonStore<CategoryDef>? _categories;
    private static JsonStore<CategoryRule>? _rules;
    private static JsonStore<TaxDocument>? _taxDocuments;
    private static JsonStore<IncomeSource>? _income;
    private static DebtPlanSettings? _debtPlan;
    private static Preferences? _prefs;

    /// <summary>Where the data lives. The self-test points this at a temp folder before anything is loaded.</summary>
    public static string Folder => _folder ??= DefaultFolder;
    public static string ReceiptsFolder => Path.Combine(Folder, "receipts");
    public static string TaxesFolder => Path.Combine(Folder, "taxes");

    public static JsonStore<Transaction> Transactions => _transactions ??= Hook(new JsonStore<Transaction>(Folder, "transactions"));
    public static JsonStore<Budget> Budgets => _budgets ??= Hook(new JsonStore<Budget>(Folder, "budgets"));
    public static JsonStore<Account> Accounts => _accounts ??= Hook(new JsonStore<Account>(Folder, "accounts"));
    public static JsonStore<Debt> Debts => _debts ??= Hook(new JsonStore<Debt>(Folder, "debts"));
    public static JsonStore<Bill> Bills => _bills ??= Hook(new JsonStore<Bill>(Folder, "bills"));
    public static JsonStore<Goal> Goals => _goals ??= Hook(new JsonStore<Goal>(Folder, "goals"));
    public static JsonStore<CategoryDef> CategoryDefs => _categories ??= Hook(new JsonStore<CategoryDef>(Folder, "categories"));
    public static JsonStore<CategoryRule> Rules => _rules ??= Hook(new JsonStore<CategoryRule>(Folder, "rules"));
    public static JsonStore<TaxDocument> TaxDocuments => _taxDocuments ??= Hook(new JsonStore<TaxDocument>(Folder, "tax-documents"));
    public static JsonStore<IncomeSource> Income => _income ??= Hook(new JsonStore<IncomeSource>(Folder, "income"));

    /// <summary>Snowball/avalanche choice and extra monthly payment for the Debts payoff plan.</summary>
    public static DebtPlanSettings DebtPlan => _debtPlan ??= DebtPlanSettings.Load(Folder);

    /// <summary>Currency, budget month start day, reminders.</summary>
    public static Preferences Prefs => _prefs ??= Preferences.Load(Folder);

    /// <summary>Raised after anything changes. Pages listen to this to refresh.</summary>
    public static event EventHandler? Changed;

    private static int _batchDepth;
    private static bool _changedDuringBatch;

    public static void RaiseChanged()
    {
        if (_batchDepth > 0) { _changedDuringBatch = true; return; }
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Groups many saves (an import, a rename) so pages refresh once at the end.</summary>
    public static IDisposable Batch()
    {
        _batchDepth++;
        return new BatchScope();
    }

    private sealed class BatchScope : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            if (--_batchDepth == 0 && _changedDuringBatch)
            {
                _changedDuringBatch = false;
                Changed?.Invoke(null, EventArgs.Empty);
            }
        }
    }

    /// <summary>Every kind of record, for sync.</summary>
    public static IReadOnlyList<ISyncStore> SyncStores =>
        [Transactions, Budgets, Accounts, Debts, Bills, Goals, CategoryDefs, Rules, TaxDocuments, Income];

    public static ISyncStore? SyncStore(string name) => SyncStores.FirstOrDefault(s => s.Name == name);

    /// <summary>Settings files that sync (Preferences, DebtPlan), by name.</summary>
    public static JsonFile? SettingsFile(string name) => name switch
    {
        "preferences" => Prefs,
        "debt-plan" => DebtPlan,
        _ => null,
    };

    /// <summary>Stores a settings file that came from the server.</summary>
    public static void ApplyRemoteSettings(string name, string json)
    {
        if (name == "preferences") _prefs = JsonFile.Adopt<Preferences>(json, Folder, name);
        else if (name == "debt-plan") _debtPlan = JsonFile.Adopt<DebtPlanSettings>(json, Folder, name);
    }

    /// <summary>Nothing entered yet: no accounts, transactions, budgets, bills, debts, goals or tax documents.</summary>
    public static bool IsEmpty =>
        Accounts.Count == 0 && Transactions.Count == 0 && Budgets.Count == 0 && Bills.Count == 0 && Debts.Count == 0 && Goals.Count == 0 && TaxDocuments.Count == 0 && Income.Count == 0;

    /// <summary>
    /// Deletes all data: every record, receipt and tax document file, and the per-data preferences (dismissed suggestions,
    /// Get started). With <paramref name="syncDeletes"/> the deletes are synced (they reach the server and other devices);
    /// without, only this PC is emptied. Categories go back to the defaults. App settings (theme, currency, PIN) stay.
    /// </summary>
    public static void EraseAll(bool syncDeletes)
    {
        using (Batch())
        {
            foreach (var store in SyncStores)
                if (syncDeletes) store.DeleteAll(); else store.ClearLocal();
        }
        foreach (var folder in new[] { ReceiptsFolder, TaxesFolder })
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Prefs.WelcomeDismissed = false;
        Prefs.SetupDone = false;
        Prefs.DismissedSubscriptions.Clear();
        Prefs.Save();
        DebtPlan.ExtraPayment = 0;
        DebtPlan.Save();
        RaiseChanged();
    }

    /// <summary>Switches to another folder (the self-test, or after restoring a backup). Reloads on next access.</summary>
    public static void UseFolder(string folder)
    {
        _folder = folder;
        Reload();
    }

    /// <summary>Forgets everything loaded so it's read from disk again.</summary>
    public static void Reload()
    {
        _transactions = null;
        _budgets = null;
        _accounts = null;
        _debts = null;
        _bills = null;
        _goals = null;
        _categories = null;
        _rules = null;
        _taxDocuments = null;
        _income = null;
        _debtPlan = null;
        _prefs = null;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static JsonStore<T> Hook<T>(JsonStore<T> store) where T : RecordBase
    {
        store.Changed += (_, _) => RaiseChanged();
        return store;
    }

    /// <summary>
    /// Account names to offer when entering a transaction or bill: open accounts first, then the built-in defaults if
    /// there are none yet. <paramref name="current"/> is always included.
    /// </summary>
    public static List<string> AccountChoices(string? current = null)
    {
        var names = Accounts.Items.Where(a => !a.IsClosed).Select(a => a.Name).ToList();
        if (names.Count == 0) names.AddRange(Categories.DefaultAccounts);
        if (!string.IsNullOrEmpty(current) && !names.Contains(current, StringComparer.OrdinalIgnoreCase)) names.Add(current);
        return names;
    }
}
