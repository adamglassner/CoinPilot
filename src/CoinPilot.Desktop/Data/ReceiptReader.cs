using System.IO;
using System.Text.RegularExpressions;

namespace CoinPilot.Data;

/// <summary>What a receipt says: the store, the total and the date (any of them can be missing).</summary>
public sealed record ReceiptGuess(string? Payee, decimal? Total, DateTime? Date)
{
    public bool IsEmpty => Payee == null && Total == null && Date == null;
}

/// <summary>
/// Reads receipt photos with Windows' own text recognition (on this PC; nothing is sent anywhere), then finds the
/// store, total and date with the same rules as the phone app (mobile/lib/receipt_reader.dart).
/// </summary>
public static class ReceiptReader
{
    private static readonly Regex AmountRx = new(@"(?<![\d.,])\$?\s?(\d{1,3}(?:,\d{3})*|\d+)[.,](\d{2})(?!\d)");
    private static readonly string[] Skip = ["receipt", "welcome", "thank", "store #", "store#", "tel", "phone", "www", "http", "order", "invoice", "cashier", "register", "date", "time"];
    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    public static bool CanRead(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".heic";

    /// <summary>The text on a receipt photo, or null when it can't be read.</summary>
    public static async Task<string?> TextAsync(string path)
    {
        try
        {
            if (!CanRead(path)) return null;
            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
                         ?? Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
            if (engine == null) return null;
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap);
            return string.Join("\n", result.Lines.Select(l => l.Text));
        }
        catch
        {
            return null;
        }
    }

    public static async Task<ReceiptGuess> ReadAsync(string path)
    {
        var text = await TextAsync(path);
        return text == null ? new ReceiptGuess(null, null, null) : Parse(text, DateTime.Today);
    }

    private static List<decimal> Amounts(string line) =>
        AmountRx.Matches(line).Select(m => decimal.Parse(m.Groups[1].Value.Replace(",", "")) + decimal.Parse(m.Groups[2].Value) / 100).ToList();

    /// <summary>Finds the store, total and date in a receipt's text.</summary>
    public static ReceiptGuess Parse(string text, DateTime today)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        string? payee = null;
        foreach (var l in lines.Take(6))
        {
            string lower = l.ToLowerInvariant();
            int letters = l.Count(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
            if (letters < 3 || char.IsDigit(l[0]) || Skip.Any(lower.Contains) || Amounts(l).Count > 0) continue;
            payee = Tidy(l);
            break;
        }

        decimal? total = null;
        int best = 0;
        static int Score(string lower) => lower.Contains("grand total") || lower.Contains("amount due") || lower.Contains("balance due") || lower.Contains("total due") ? 3
            : lower.Contains("total") ? 2 : 0;
        for (int i = 0; i < lines.Count; i++)
        {
            string lower = lines[i].ToLowerInvariant();
            if (lower.Contains("subtotal") || lower.Contains("sub total") || lower.Contains("sub-total") || lower.Contains("savings") || lower.Contains("tax")) continue;
            int s = Score(lower);
            if (s == 0 || s < best) continue;
            var found = Amounts(lines[i]);
            if (found.Count == 0 && i + 1 < lines.Count) found = Amounts(lines[i + 1]);
            if (found.Count == 0) continue;
            total = found[^1];
            best = s;
        }
        if (total == null)
        {
            var all = lines.SelectMany(Amounts).ToList();
            if (all.Count > 0) total = all.Max();
        }

        DateTime? date = null;
        foreach (var l in lines)
        {
            date = FindDate(l, today);
            if (date != null) break;
        }
        return new ReceiptGuess(payee, total, date);
    }

    private static DateTime? FindDate(string line, DateTime now)
    {
        DateTime? Ok(int y, int m, int d)
        {
            if (y < 100) y += 2000;
            if (m < 1 || m > 12 || d < 1 || d > DateTime.DaysInMonth(Math.Clamp(y, 1, 9999), m)) return null;
            var dt = new DateTime(y, m, d);
            if (dt > now.Date.AddDays(1) || dt < now.Date.AddYears(-2)) return null;
            return dt;
        }
        foreach (Match m in Regex.Matches(line, @"(?<!\d)(\d{4})-(\d{1,2})-(\d{1,2})(?!\d)"))
            if (Ok(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) is DateTime d) return d;
        foreach (Match m in Regex.Matches(line, @"(?<!\d)(\d{1,2})[/-](\d{1,2})[/-](\d{2,4})(?!\d)"))
            if (Ok(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) is DateTime d) return d;
        foreach (Match m in Regex.Matches(line, @"\b([A-Za-z]{3})[a-z]*\.?\s+(\d{1,2}),?\s+(\d{4})\b"))
        {
            int mi = Array.IndexOf(Months, m.Groups[1].Value.ToLowerInvariant());
            if (mi < 0) continue;
            if (Ok(int.Parse(m.Groups[3].Value), mi + 1, int.Parse(m.Groups[2].Value)) is DateTime d) return d;
        }
        return null;
    }

    /// <summary>"KROGER #412" → "Kroger".</summary>
    private static string Tidy(string s)
    {
        string t = Regex.Replace(s, @"\s*#\s*\d+.*$", "");
        t = Regex.Replace(t, @"\s{2,}", " ").Trim();
        if (t.Length == 0) t = s.Trim();
        if (t == t.ToUpperInvariant())
            t = string.Join(' ', t.ToLowerInvariant().Split(' ').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
        return t;
    }
}
