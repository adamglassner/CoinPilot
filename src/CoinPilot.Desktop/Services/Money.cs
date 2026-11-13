using System.Globalization;

namespace CoinPilot.Services;

/// <summary>Formats and reads dollar amounts.</summary>
public static class Money
{
    public static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    /// <summary>$1,284.50 (or $1,285 with cents: false). Negative values get a real minus sign.</summary>
    public static string Format(decimal value, bool cents = true)
    {
        string s = Math.Abs(value).ToString(cents ? "$#,0.00" : "$#,0", Us);
        return value < 0 ? "−" + s : s;
    }

    /// <summary>+$2,050.00 for money in, −$86.42 for money out.</summary>
    public static string Signed(decimal amount) => (amount > 0 ? "+" : amount < 0 ? "−" : "") + Format(Math.Abs(amount));

    /// <summary>Reads "86.42", "$1,284.50", " 12 ". Returns false for anything else (letters, empty, more than 2 decimals).</summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim().Replace("$", "").Replace(",", "").Replace(" ", "");
        if (t.StartsWith('-') || t.StartsWith('−')) return false;
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) return false;
        if (decimal.Round(value, 2) != value) return false;
        return value < 100_000_000m;
    }

    public static string MonthName(DateTime month) => month.ToString("MMMM yyyy", Us);
}
