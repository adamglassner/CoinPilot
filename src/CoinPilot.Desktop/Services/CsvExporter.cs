using System.IO;
using System.Text;
using Microsoft.Win32;

namespace CoinPilot.Services;

/// <summary>Saves rows as a CSV file that opens cleanly in Excel.</summary>
public static class CsvExporter
{
    public static void Export(string suggestedName, string[] headers, IEnumerable<string[]> rows)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export to CSV",
            FileName = $"{suggestedName}-{DateTime.Today:yyyy-MM-dd}.csv",
            Filter = "CSV file (*.csv)|*.csv",
            DefaultExt = ".csv",
        };
        if (dialog.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Escape)));
        int count = 0;
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", row.Select(Escape)));
            count++;
        }

        try
        {
            // UTF-8 with BOM so Excel shows accents and symbols correctly.
            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
            Toaster.Show($"Exported {count} row{(count == 1 ? "" : "s")} to {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            Toaster.Show($"Export failed: {ex.Message}");
        }
    }

    private static string Escape(string? value)
    {
        value ??= "";
        // Guard against spreadsheet formula injection.
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
