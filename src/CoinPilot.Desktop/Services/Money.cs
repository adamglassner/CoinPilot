using System.Globalization;
using CoinPilot.Data;

namespace CoinPilot.Services;

/// <summary>Formats and reads money in the currency chosen in Settings, plus a few date helpers.</summary>
public static class Money
{
    /// <summary>English (US) formatting, used for dates and file formats.</summary>
    public static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public sealed record CurrencyOption(string Culture, string Label);

    public static readonly IReadOnlyList<CurrencyOption> Currencies =
    [
        new("en-US", "US dollar ($)"),
        new("en-CA", "Canadian dollar ($)"),
        new("en-GB", "British pound (£)"),
        new("en-IE", "Euro (€)"),
        new("en-AU", "Australian dollar ($)"),
        new("en-NZ", "New Zealand dollar ($)"),
        new("de-CH", "Swiss franc (CHF)"),
        new("en-IN", "Indian rupee (₹)"),
        new("es-MX", "Mexican peso ($)"),
        new("ja-JP", "Japanese yen (¥)"),
        new("en-ZA", "South African rand (R)"),
    ];

    private static string? _cultureName;
    private static CultureInfo _culture = Us;

    /// <summary>Culture for the chosen currency.</summary>
    public static CultureInfo Culture
    {
        get
        {
            string name = DataStore.Prefs.Currency;
            if (name != _cultureName)
            {
                _cultureName = name;
                try { _culture = CultureInfo.GetCultureInfo(name); }
                catch (CultureNotFoundException) { _culture = Us; }
            }
            return _culture;
        }
    }

    private static int Decimals => Culture.NumberFormat.CurrencyDecimalDigits;

    /// <summary>$1,284.50 (or $1,285 with cents: false). Negative values get a real minus sign.</summary>
    public static string Format(decimal value, bool cents = true)
    {
        int digits = cents ? Decimals : 0;
        string s = Math.Round(Math.Abs(value), digits).ToString("C" + digits, Culture);
        return value < 0 && s.Any(char.IsDigit) && Math.Round(Math.Abs(value), digits) != 0 ? "−" + s : s;
    }

    /// <summary>+$2,050.00 for money in, −$86.42 for money out.</summary>
    public static string Signed(decimal amount) => (amount > 0 ? "+" : amount < 0 ? "−" : "") + Format(Math.Abs(amount));

    /// <summary>A plain number for edit boxes: "1284.50" (or "1284,50" where a comma is the decimal mark).</summary>
    public static string Plain(decimal value) => value.ToString(Decimals == 0 ? "0" : "0.00", Culture);

    /// <summary>
    /// Reads "86.42", "$1,284.50", "1.284,50 €", " 12 ". Returns false for anything else (letters, empty, negative,
    /// more decimals than the currency has).
    /// </summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var nf = Culture.NumberFormat;
        string t = text.Trim().Replace(nf.CurrencySymbol, "").Replace("$", "").Replace("€", "").Replace("£", "")
            .Replace("¥", "").Replace("₹", "").Replace(" ", "").Replace(" ", "").Replace(" ", "");
        if (t.Length == 0 || t.StartsWith('-') || t.StartsWith('−')) return false;

        var styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands;
        bool ok = decimal.TryParse(t, styles, Culture, out value);
        // Also accept a plain "12.50" where the currency normally writes "12,50".
        if (!ok || (nf.NumberDecimalSeparator == "," && t.Count(c => c == '.') == 1 && !t.Contains(',') && t.Split('.')[1].Length <= 2))
            ok = decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        if (!ok) return false;
        if (decimal.Round(value, Math.Max(Decimals, 0)) != value && decimal.Round(value, 2) != value) return false;
        return value < 100_000_000m;
    }

    /// <summary>Reads an interest rate like "22.99" or "6.5%" (0 to 100, up to 3 decimals). Empty means 0.</summary>
    public static bool TryParsePercent(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;
        string t = text.Trim().TrimEnd('%').Trim().Replace(',', '.');
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) return false;
        return value <= 100 && decimal.Round(value, 3) == value;
    }

    public static string Percent(decimal rate) => rate.ToString("0.##", Us) + "%";

    /// <summary>"November 2026", or "Nov 15 – Dec 14, 2026" when budget months start mid-month.</summary>
    public static string MonthName(DateTime month)
    {
        var start = Budgeting.MonthStart(month);
        if (Budgeting.StartDay == 1) return start.ToString("MMMM yyyy", Us);
        var end = Budgeting.MonthEnd(start);
        return $"{start.ToString("MMM d", Us)} – {end.ToString("MMM d, yyyy", Us)}";
    }

    /// <summary>Short label for charts: "Nov" (or "Nov 15" when months start mid-month).</summary>
    public static string MonthShort(DateTime month, bool withYear = false)
    {
        var start = Budgeting.MonthStart(month);
        if (Budgeting.StartDay == 1) return start.ToString(withYear ? "MMM yy" : "MMM", Us);
        return start.ToString(withYear ? "MMM d yy" : "MMM d", Us);
    }
}
