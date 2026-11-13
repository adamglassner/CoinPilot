using System.Diagnostics;
using System.IO;

namespace CoinPilot.Data;

/// <summary>
/// Receipts and tax documents: copies of your files kept in the data folder (encrypted when protection is on).
/// Opening one writes a temporary copy that's cleaned up when CoinPilot locks or closes.
/// </summary>
public static class FileVault
{
    private static readonly string OpenFolder = Path.Combine(Path.GetTempPath(), "CoinPilot-open");

    /// <summary>Copies a receipt into data\receipts and returns its stored file name.</summary>
    public static string StoreReceipt(string sourcePath)
    {
        Directory.CreateDirectory(DataStore.ReceiptsFolder);
        string name = Guid.NewGuid().ToString("N") + Path.GetExtension(sourcePath).ToLowerInvariant();
        DataLock.WriteBytes(Path.Combine(DataStore.ReceiptsFolder, name), File.ReadAllBytes(sourcePath));
        return name;
    }

    public static string ReceiptPath(string fileName) => Path.Combine(DataStore.ReceiptsFolder, fileName);

    public static string TaxFolder(int year) => Path.Combine(DataStore.TaxesFolder, year.ToString());

    /// <summary>Copies a tax document into data\taxes\&lt;year&gt; and returns its stored file name.</summary>
    public static string StoreTaxDocument(int year, string sourcePath)
    {
        string folder = TaxFolder(year);
        Directory.CreateDirectory(folder);
        string name = Guid.NewGuid().ToString("N") + Path.GetExtension(sourcePath).ToLowerInvariant();
        DataLock.WriteBytes(Path.Combine(folder, name), File.ReadAllBytes(sourcePath));
        return name;
    }

    public static string TaxDocumentPath(TaxDocument d) => Path.Combine(TaxFolder(d.Year), d.FileName);

    /// <summary>Moves a stored tax document to another year's folder.</summary>
    public static void MoveTaxDocument(TaxDocument d, int newYear)
    {
        if (d.Year == newYear) return;
        string from = TaxDocumentPath(d);
        Directory.CreateDirectory(TaxFolder(newYear));
        string to = Path.Combine(TaxFolder(newYear), d.FileName);
        if (File.Exists(from)) File.Move(from, to, overwrite: true);
        d.Year = newYear;
    }

    public static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* in use; harmless */ }
    }

    /// <summary>Opens a stored file in its usual app (via a temporary readable copy).</summary>
    public static void Open(string storedPath, string displayName)
    {
        string dir = Path.Combine(OpenFolder, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string safeName = string.Concat(displayName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (safeName.Length == 0) safeName = "document";
        if (Path.GetExtension(safeName).Length == 0) safeName += Path.GetExtension(storedPath);
        string temp = Path.Combine(dir, safeName);
        File.WriteAllBytes(temp, DataLock.ReadBytes(storedPath));
        Process.Start(new ProcessStartInfo(temp) { UseShellExecute = true });
    }

    /// <summary>Saves a readable copy of a stored file somewhere you choose.</summary>
    public static void SaveCopy(string storedPath, string destination) =>
        File.WriteAllBytes(destination, DataLock.ReadBytes(storedPath));

    /// <summary>Deletes the temporary copies made by <see cref="Open"/> (best effort; files still open stay until next time).</summary>
    public static void CleanUpOpenedFiles()
    {
        try
        {
            if (!Directory.Exists(OpenFolder)) return;
            foreach (var dir in Directory.EnumerateDirectories(OpenFolder))
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* still open in another app */ }
            }
        }
        catch { /* ignore */ }
    }

    public static string SizeText(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / (1024.0 * 1024):0.0} MB",
    };
}
