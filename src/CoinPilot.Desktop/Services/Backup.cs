using System.IO;
using System.IO.Compression;
using CoinPilot.Data;

namespace CoinPilot.Services;

/// <summary>
/// Backs up the whole data folder (transactions, budgets, accounts, debts, bills, goals, categories, receipts and tax
/// documents) to a .zip, and restores one. With PIN/password protection on, the backup stays encrypted and needs the
/// same PIN/password (or recovery key) after restoring.
/// </summary>
public static class Backup
{
    public static string SuggestedName => $"CoinPilot backup {DateTime.Now:yyyy-MM-dd}.zip";

    public static void Create(string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        string folder = DataStore.Folder;
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
            string entry = Path.GetRelativePath(folder, file).Replace('\\', '/');
            zip.CreateEntryFromFile(file, entry, CompressionLevel.Optimal);
        }
    }

    /// <summary>Checks that a .zip looks like a CoinPilot backup. Returns an error message, or null when it's fine.</summary>
    public static string? Check(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            string[] known = ["transactions.json", "budgets.json", "accounts.json", "debts.json", "bills.json", "goals.json", "categories.json", "preferences.json", "tax-documents.json"];
            if (!zip.Entries.Any(e => known.Contains(e.FullName, StringComparer.OrdinalIgnoreCase)))
                return "That file isn't a CoinPilot backup.";
            if (zip.Entries.Any(e => e.FullName.Contains("..") || Path.IsPathRooted(e.FullName)))
                return "That backup contains unsafe file names.";
            return null;
        }
        catch (InvalidDataException)
        {
            return "That file isn't a valid .zip file.";
        }
    }

    /// <summary>
    /// Replaces the data folder with the backup. The current data is kept in a "data before restore" folder next to it.
    /// Returns where the old data went.
    /// </summary>
    public static string Restore(string zipPath)
    {
        string folder = DataStore.Folder;
        string parent = Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar))!;
        string keep = Path.Combine(parent, $"data before restore {DateTime.Now:yyyy-MM-dd HHmmss}");

        FileVault.CleanUpOpenedFiles();
        if (Directory.Exists(folder)) Directory.Move(folder, keep);
        Directory.CreateDirectory(folder);
        ZipFile.ExtractToDirectory(zipPath, folder);

        DataLock.Forget();
        DataStore.Reload();
        return keep;
    }

    /// <summary>Every transaction (all dates) as CSV.</summary>
    public static IEnumerable<string[]> AllTransactionRows() =>
        DataStore.Transactions.Items.OrderBy(t => t.Date).Select(t => new[]
        {
            t.Date.ToString("yyyy-MM-dd"), t.Payee, t.IsTransfer ? "Transfer" : t.IsSplit ? string.Join(" + ", t.Splits.Select(s => $"{s.Category} {s.Amount:0.00}")) : t.Category,
            t.Account, t.TransferAccount, t.Amount.ToString("0.00", Money.Us), string.Join(", ", t.Tags), t.Note,
        });

    public static readonly string[] AllTransactionHeaders = ["Date", "Payee", "Category", "Account", "Transfer to", "Amount", "Tags", "Note"];
}
