namespace CoinPilot.Data;

/// <summary>A kind of record the sync engine can read and update (every <see cref="JsonStore{T}"/>).</summary>
public interface ISyncStore
{
    string Name { get; }
    int Count { get; }
    IEnumerable<(string Id, DateTime UpdatedAt)> Entries();
    (string Json, DateTime UpdatedAt)? Get(string id);
    int ApplyRemote(IEnumerable<string> jsonRecords);
    int RemoveRemote(IEnumerable<string> ids);
    void ClearLocal();
    /// <summary>Deletes every record as if one by one (each delete is synced), with one save.</summary>
    void DeleteAll();
}

/// <summary>
/// Notes every add, edit and delete made on this PC so the sync engine knows what to send. Changes that arrive from
/// the server don't come through here, so they're never sent back.
/// </summary>
public static class SyncTracker
{
    /// <summary>Preferences and the debt plan sync as records in this collection ("preferences", "debt-plan").</summary>
    public const string SettingsCollection = "settings";

    /// <summary>Set by the sync engine while signed in: (collection, id, deleted).</summary>
    public static Action<string, string, bool>? OnChanged { get; set; }

    public static void Changed(string collection, string id, bool deleted) => OnChanged?.Invoke(collection, id, deleted);
}
