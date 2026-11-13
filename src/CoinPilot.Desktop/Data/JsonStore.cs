using System.IO;
using System.Text.Json;

namespace CoinPilot.Data;

/// <summary>
/// A list of records kept in one JSON file (encrypted when PIN/password protection is on). Writes go to a temp file
/// first and are then swapped in (keeping a .bak), so a crash mid-save never corrupts the existing data.
/// </summary>
public sealed class JsonStore<T> where T : RecordBase
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly List<T> _items;
    /// <summary>Set when the file couldn't be read because the data is locked: never write over it.</summary>
    private readonly bool _readOnly;

    public JsonStore(string folder, string name)
    {
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
        }
        Save();
    }

    /// <summary>Saves changes made to several records at once.</summary>
    public void UpdateMany(IEnumerable<T> items)
    {
        var now = DateTime.Now;
        foreach (var item in items) item.UpdatedAt = now;
        Save();
    }

    public void Update(T item)
    {
        item.UpdatedAt = DateTime.Now;
        Save();
    }

    public void Delete(T item)
    {
        _items.Remove(item);
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
            CoinPilot.Services.Toaster.Show($"Couldn't save to disk ({ex.Message}). Your change is kept until you close CoinPilot.");
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
