using System.IO;
using System.Text.Json;

namespace CoinPilot.Data;

/// <summary>A single settings object kept in one JSON file in the data folder.</summary>
public abstract class JsonFile
{
    private string _path = "";

    /// <summary>When it was last changed on any device (newest wins when syncing).</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>The file's name without .json ("preferences", "debt-plan"); also its id for sync.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string Name => Path.GetFileNameWithoutExtension(_path);

    protected static T Load<T>(string folder, string name) where T : JsonFile, new()
    {
        string path = Path.Combine(folder, name + ".json");
        T? value = null;
        try
        {
            if (File.Exists(path)) value = JsonSerializer.Deserialize<T>(DataLock.ReadText(path));
        }
        catch
        {
            // Unreadable file: fall back to defaults.
        }
        value ??= new T();
        value._path = path;
        return value;
    }

    public void Save()
    {
        if (!DataLock.IsOpen) return;
        UpdatedAt = DateTime.Now;
        SyncTracker.Changed(SyncTracker.SettingsCollection, Name, false);
        Write();
    }

    public string ToJson() => JsonSerializer.Serialize(this, GetType());

    /// <summary>A copy from the server: saved in place of this file without being sent back.</summary>
    internal static T Adopt<T>(string json, string folder, string name) where T : JsonFile, new()
    {
        var value = JsonSerializer.Deserialize<T>(json) ?? new T();
        value._path = Path.Combine(folder, name + ".json");
        value.Write();
        return value;
    }

    /// <summary>Saves without marking it for sync (a copy that came from the server).</summary>
    internal void Write()
    {
        if (!DataLock.IsOpen) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            DataLock.WriteText(_path, JsonSerializer.Serialize(this, GetType(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Preferences aren't worth crashing over.
        }
        DataStore.RaiseChanged();
    }
}

/// <summary>Budget preferences that travel with your data (data\preferences.json).</summary>
public sealed class Preferences : JsonFile
{
    /// <summary>Culture used to format money, e.g. "en-US" (US dollar), "en-GB" (pound), "en-IE" (euro).</summary>
    public string Currency { get; set; } = "en-US";
    /// <summary>Day of the month your budget month starts (1–28). Use your payday if you budget paycheck to paycheck.</summary>
    public int MonthStartDay { get; set; } = 1;
    public bool RemindersOn { get; set; } = true;
    /// <summary>How many days before a due date to remind you.</summary>
    public int ReminderDays { get; set; } = 3;
    /// <summary>Payees you said aren't subscriptions (so they stop being suggested).</summary>
    public List<string> DismissedSubscriptions { get; set; } = [];
    /// <summary>"Subscriptions you could cut" skipped: "key|2026-11" for one month, "key|forever" for good.</summary>
    public List<string> SkippedCuts { get; set; } = [];
    public bool WelcomeDismissed { get; set; }
    /// <summary>Get started (the first-run setup) was finished or skipped.</summary>
    public bool SetupDone { get; set; }

    /// <summary>Forecast: how far ahead ("Month", "30", "60" or "90" days).</summary>
    public string ForecastHorizon { get; set; } = "Month";
    /// <summary>Forecast: the balance to aim for (0 = just don't run out).</summary>
    public decimal ForecastTarget { get; set; }
    /// <summary>Forecast: start from savings accounts too.</summary>
    public bool ForecastIncludeSavings { get; set; }
    /// <summary>Shared household: the people (two or more turns it on).</summary>
    public List<string> HouseholdPeople { get; set; } = [];
    /// <summary>The first person's share of shared spending (50 = half), when there are two.</summary>
    public int HouseholdShare { get; set; } = 50;
    [System.Text.Json.Serialization.JsonIgnore] public bool Household => HouseholdPeople.Count >= 2;

    /// <summary>Spending alerts (budgets running low, bill price changes, unusual charges, a coming dip).</summary>
    public bool AlertsOn { get; set; } = true;
    /// <summary>Warn when a budget is this % used (50–99).</summary>
    public int AlertPercent { get; set; } = 80;
    /// <summary>Alerts dismissed on any device (their keys).</summary>
    public List<string> DismissedAlerts { get; set; } = [];
    /// <summary>Forecast: take each month's goal and sinking fund savings out.</summary>
    public bool ForecastCountSavings { get; set; } = true;

    public static Preferences Load(string folder) => Load<Preferences>(folder, "preferences");
}

/// <summary>Saved in data\debt-plan.json next to the debts.</summary>
public sealed class DebtPlanSettings : JsonFile
{
    public string Strategy { get; set; } = DebtPlanner.Avalanche;
    public decimal ExtraPayment { get; set; }

    public static DebtPlanSettings Load(string folder) => Load<DebtPlanSettings>(folder, "debt-plan");
}

/// <summary>A tax document kept in data\taxes\&lt;year&gt;\.</summary>
public sealed class TaxDocument : RecordBase
{
    public int Year { get; set; } = DateTime.Today.Year - 1;
    public string Title { get; set; } = "";
    public string Kind { get; set; } = TaxDocumentKinds.Other;
    /// <summary>File name inside the year's folder.</summary>
    public string FileName { get; set; } = "";
    /// <summary>The name the file had when you added it.</summary>
    public string OriginalName { get; set; } = "";
    public long Size { get; set; }
    public string Note { get; set; } = "";
}

public static class TaxDocumentKinds
{
    public const string Other = "Other";
    public static readonly string[] All =
        ["W-2", "1099", "1098", "1095", "Tax return", "Receipt", "Charitable donation", "Property tax", "Estimated payment", "Letter from IRS/state", Other];
}
