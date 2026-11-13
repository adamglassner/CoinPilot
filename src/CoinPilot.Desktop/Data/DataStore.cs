using System.IO;

namespace CoinPilot.Data;

/// <summary>
/// Everything CoinPilot saves, kept on this PC in %AppData%\CoinPilot\data\.
/// When sync arrives these files become the offline copy that syncs between devices.
/// </summary>
public static class DataStore
{
    public static readonly string DefaultFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoinPilot", "data");

    private static string? _folder;
    private static JsonStore<Transaction>? _transactions;
    private static JsonStore<Budget>? _budgets;

    /// <summary>Where the data lives. The self-test points this at a temp folder before anything is loaded.</summary>
    public static string Folder => _folder ??= DefaultFolder;

    public static JsonStore<Transaction> Transactions => _transactions ??= Hook(new JsonStore<Transaction>(Folder, "transactions"));
    public static JsonStore<Budget> Budgets => _budgets ??= Hook(new JsonStore<Budget>(Folder, "budgets"));

    /// <summary>Raised after any transaction or budget changes. Pages listen to this to refresh.</summary>
    public static event EventHandler? Changed;

    /// <summary>Switches to another folder (used by the self-test). Reloads on next access.</summary>
    public static void UseFolder(string folder)
    {
        _folder = folder;
        _transactions = null;
        _budgets = null;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static JsonStore<T> Hook<T>(JsonStore<T> store) where T : RecordBase
    {
        store.Changed += (_, _) => Changed?.Invoke(null, EventArgs.Empty);
        return store;
    }
}
