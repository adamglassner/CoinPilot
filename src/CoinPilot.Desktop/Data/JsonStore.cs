using System.IO;
using System.Text.Json;

namespace CoinPilot.Data;

/// <summary>
/// A list of records kept in one JSON file. Writes go to a temp file first and are then
/// swapped in (keeping a .bak), so a crash mid-save never corrupts the existing data.
/// </summary>
public sealed class JsonStore<T> where T : RecordBase
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly List<T> _items;

    public JsonStore(string folder, string name)
    {
        _path = Path.Combine(folder, name + ".json");
        _items = Load(_path);
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
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_items, Options));
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
                    return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(candidate), Options) ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Fall through to the backup copy.
            }
        }
        return [];
    }
}
