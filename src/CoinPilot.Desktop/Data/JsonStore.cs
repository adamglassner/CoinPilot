using System.IO;
using System.Text.Json;

namespace CoinPilot.Data;

/// <summary>
/// A list of records kept in one JSON file (encrypted when PIN/password protection is on). Writes go to a temp file
/// first and are then swapped in (keeping a .bak), so a crash mid-save never corrupts the existing data.
/// </summary>
public sealed class JsonStore<T> : ISyncStore where T : RecordBase
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions Compact = new();
    private readonly string _path;

    /// <summary>The file's name without .json ("transactions"); also the record type's name for sync.</summary>
    public string Name { get; }
    private readonly List<T> _items;
    /// <summary>Set when the file couldn't be read because the data is locked: never write over it.</summary>
    private readonly bool _readOnly;

    public JsonStore(string folder, string name)
    {
        Name = name;
        _path = Path.Combine(folder, name + ".json");
        _readOnly = !DataLock.IsOpen;
        _items = _readOnly ? [] : Load(_path);
    }

    public IReadOnlyList<T> Items => _items;

    /// <summary>Raised after any add, update or delete.</summary>
    public event EventHandler? Changed;

    public void Add(T item)
    {
        if (string.IsNullOrEmpty(item.Id)) item.Id = Guid.NewGuid().ToString("N");
        item.CreatedAt = item.UpdatedAt = DateTime.Now;
        _items.Add(item);
        SyncTracker.Changed(Name, item.Id, false);
        Save();
    }

    /// <summary>Adds many records with one save (imports).</summary>
    public void AddRange(IEnumerable<T> items)
    {
        var now = DateTime.Now;
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Id)) item.Id = Guid.NewGuid().ToString("N");
            item.CreatedAt = item.UpdatedAt = now;
            _items.Add(item);
            SyncTracker.Changed(Name, item.Id, false);
        }
        Save();
    }

    /// <summary>Saves changes made to several records at once.</summary>
    public void UpdateMany(IEnumerable<T> items)
    {
        var now = DateTime.Now;
        foreach (var item in items)
        {
            item.UpdatedAt = now;
            SyncTracker.Changed(Name, item.Id, false);
        }
        Save();
    }

    public void Update(T item)
    {
        item.UpdatedAt = DateTime.Now;
        SyncTracker.Changed(Name, item.Id, false);
        Save();
    }

    public void Delete(T item)
    {
        if (_items.Remove(item)) SyncTracker.Changed(Name, item.Id, true);
        Save();
    }

    // ----- Sync: records arriving from the server are stored as they are (keeping their times) and not sent back -----

    public T? Find(string id) => _items.FirstOrDefault(i => i.Id == id);

    public static string ToJson(T item) => JsonSerializer.Serialize(item, Compact);

    public IEnumerable<(string Id, DateTime UpdatedAt)> Entries() => _items.Select(i => (i.Id, i.UpdatedAt)).ToList();

    public (string Json, DateTime UpdatedAt)? Get(string id) => Find(id) is { } item ? (ToJson(item), item.UpdatedAt) : null;

    public int Count => _items.Count;

    /// <summary>Adds or replaces records from the server in one save. Returns how many changed.</summary>
    public int ApplyRemote(IEnumerable<string> jsonRecords)
    {
        int n = 0;
        foreach (var json in jsonRecords)
        {
            var item = JsonSerializer.Deserialize<T>(json, Options);
            if (item == null || string.IsNullOrEmpty(item.Id)) continue;
            int i = _items.FindIndex(x => x.Id == item.Id);
            if (i >= 0) _items[i] = item;
            else _items.Add(item);
            n++;
        }
        if (n > 0) Save();
        return n;
    }

    public int RemoveRemote(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet();
        int n = _items.RemoveAll(i => set.Contains(i.Id));
        if (n > 0) Save();
        return n;
    }

    public void DeleteAll()
    {
        foreach (var item in _items) SyncTracker.Changed(Name, item.Id, true);
        _items.Clear();
        Save();
    }

    /// <summary>Empties the store without sending deletes (switching to the server's copy of the data).</summary>
    public void ClearLocal()
    {
        _items.Clear();
        Save();
    }

    private void Save()
    {
        if (_readOnly || !DataLock.IsOpen)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            DataLock.WriteText(temp, JsonSerializer.Serialize(_items, Options));
            if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak");
            else File.Move(temp, _path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CoinPilot.Services.Toaster.Show($"Couldn't save to disk ({ex.Message}). Your change is kept until you close WillowWallet.");
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static List<T> Load(string path)
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (File.Exists(candidate))
                    return JsonSerializer.Deserialize<List<T>>(DataLock.ReadText(candidate), Options) ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Fall through to the backup copy.
            }
        }
        return [];
    }
}
