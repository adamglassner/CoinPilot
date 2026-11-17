using System.Globalization;

namespace CoinPilot.Services;

/// <summary>A simple calculator: + − × ÷, percent, sign, and chained sums ("12 + 30 + 8 ="). The same as the Mac and Linux one.</summary>
public sealed class CalcEngine
{
    public string Display { get; private set; } = "0";
    /// <summary>"120 + 35 =" above the display.</summary>
    public string History { get; private set; } = "";
    private decimal? _acc;
    private string? _op;
    private bool _fresh = true;

    public const string DivideByZero = "Can't divide by zero";

    public static string Fmt(decimal v)
    {
        v = Math.Round(v, 10, MidpointRounding.AwayFromZero);
        return v.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    public bool IsError => !decimal.TryParse(Display, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
    private decimal Value => decimal.TryParse(Display, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public void Digit(string d)
    {
        if (IsError) Clear();
        if (_fresh)
        {
            Display = d == "." ? "0." : d;
            _fresh = false;
            return;
        }
        if (d == "." && Display.Contains('.')) return;
        if (Display.Replace("-", "").Replace(".", "").Length >= 15) return;
        Display = Display == "0" && d != "." ? d : Display + d;
    }

    private static decimal? Apply(decimal a, string op, decimal b) => op switch
    {
        "+" => a + b,
        "−" => a - b,
        "×" => a * b,
        _ => b == 0 ? null : a / b,
    };

    private void Show(decimal? v) => Display = v is decimal d ? Fmt(d) : DivideByZero;

    public void Op(string o)
    {
        if (IsError) return;
        if (_op != null && !_fresh)
        {
            var r = Apply(_acc!.Value, _op, Value);
            Show(r);
            if (r == null) { _acc = null; _op = null; History = ""; _fresh = true; return; }
            _acc = r;
        }
        else if (_op == null) _acc = Value;
        _op = o;
        History = $"{Fmt(_acc!.Value)} {o}";
        _fresh = true;
    }

    public void Evaluate()
    {
        if (_op == null || IsError) return;
        var b = Value;
        History = $"{Fmt(_acc!.Value)} {_op} {Fmt(b)} =";
        Show(Apply(_acc.Value, _op, b));
        _acc = null;
        _op = null;
        _fresh = true;
    }

    /// <summary>50 + 10% = 55; on its own, 10% = 0.1.</summary>
    public void Percent()
    {
        if (IsError) return;
        Display = Fmt(_acc != null && (_op == "+" || _op == "−") ? _acc.Value * Value / 100 : Value / 100);
        _fresh = false;
    }

    public void Negate()
    {
        if (IsError || Display == "0") return;
        Display = Display.StartsWith('-') ? Display[1..] : "-" + Display;
    }

    public void Back()
    {
        if (_fresh || IsError) return;
        Display = Display.Length <= 1 || (Display.Length == 2 && Display.StartsWith('-')) ? "0" : Display[..^1];
    }

    public void Clear()
    {
        Display = "0";
        History = "";
        _acc = null;
        _op = null;
        _fresh = true;
    }
}
