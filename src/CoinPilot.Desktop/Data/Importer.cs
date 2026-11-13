using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CoinPilot.Data;

/// <summary>One transaction read from a bank statement, before it's saved.</summary>
public sealed class ImportedRow
{
    public DateTime Date { get; set; }
    public string Payee { get; set; } = "";
    /// <summary>Negative = money out, positive = money in.</summary>
    public decimal Amount { get; set; }
    public string Category { get; set; } = "";
    public string Note { get; set; } = "";
    public string ImportId { get; set; } = "";
    public bool IsDuplicate { get; set; }
}

/// <summary>Which CSV column holds what (-1 = none).</summary>
public sealed class CsvMapping
{
    public int Date { get; set; } = -1;
    public int Payee { get; set; } = -1;
    public int Amount { get; set; } = -1;
    public int Debit { get; set; } = -1;
    public int Credit { get; set; } = -1;
    public int Category { get; set; } = -1;
    public int Note { get; set; } = -1;
    /// <summary>The file shows spending as positive numbers (common for credit cards).</summary>
    public bool FlipSign { get; set; }

    public bool IsComplete => Date >= 0 && Payee >= 0 && (Amount >= 0 || Debit >= 0 || Credit >= 0);
}

/// <summary>A statement file after reading: OFX/QFX rows, or CSV headers and cells waiting for a column mapping.</summary>
public sealed class Statement
{
    public string Format { get; init; } = "";
    public List<ImportedRow> Rows { get; set; } = [];
    public string[] Headers { get; init; } = [];
    public List<string[]> Cells { get; init; } = [];
    /// <summary>Balance reported by an OFX/QFX file, and its date.</summary>
    public decimal? LedgerBalance { get; init; }
    public DateTime? LedgerDate { get; init; }
}

/// <summary>Reads bank and card statements: CSV (any layout), OFX and QFX.</summary>
public static class Importer
{
    public static Statement Read(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".ofx" or ".qfx" || text.Contains("<OFX", StringComparison.OrdinalIgnoreCase) || text.Contains("<STMTTRN>", StringComparison.OrdinalIgnoreCase))
            return ReadOfx(text, ext == ".qfx" ? "QFX" : "OFX");
        return ReadCsv(text);
    }

    // ----- OFX / QFX -----

    public static Statement ReadOfx(string text, string format = "OFX")
    {
        var rows = new List<ImportedRow>();
        foreach (Match m in Regex.Matches(text, @"<STMTTRN>(.*?)(</STMTTRN>|(?=<STMTTRN>)|</BANKTRANLIST>)", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string block = m.Groups[1].Value;
            if (!TryOfxDate(Tag(block, "DTPOSTED"), out var date)) continue;
            if (!decimal.TryParse(Tag(block, "TRNAMT").Replace(",", "."), NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)) continue;
            string name = Tag(block, "NAME");
            string memo = Tag(block, "MEMO");
            rows.Add(new ImportedRow
            {
                Date = date,
                Amount = amount,
                Payee = name.Length > 0 ? name : memo,
                Note = name.Length > 0 && memo.Length > 0 && !string.Equals(memo, name, StringComparison.OrdinalIgnoreCase) ? memo : "",
                ImportId = Tag(block, "FITID"),
            });
        }

        decimal? balance = null;
        DateTime? balanceDate = null;
        var ledger = Regex.Match(text, @"<LEDGERBAL>(.*?)(</LEDGERBAL>|<AVAILBAL>|</STMTRS>|</CCSTMTRS>)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (ledger.Success
            && decimal.TryParse(Tag(ledger.Groups[1].Value, "BALAMT").Replace(",", "."), NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var bal)
            && TryOfxDate(Tag(ledger.Groups[1].Value, "DTASOF"), out var asOf))
        {
            balance = bal;
            balanceDate = asOf;
        }
        return new Statement { Format = format, Rows = rows, LedgerBalance = balance, LedgerDate = balanceDate };
    }

    private static string Tag(string block, string name)
    {
        var m = Regex.Match(block, $@"<{name}>\s*([^<\r\n]*)", RegexOptions.IgnoreCase);
        return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Trim()) : "";
    }

    private static bool TryOfxDate(string s, out DateTime date)
    {
        date = default;
        if (s.Length < 8) return false;
        return DateTime.TryParseExact(s[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    // ----- CSV -----

    public static Statement ReadCsv(string text)
    {
        var lines = ParseCsv(text);
        // Some banks put a few lines of account info before the real header: use the first line that looks like one.
        int headerIndex = lines.FindIndex(l => l.Length >= 3 && l.Any(c => Regex.IsMatch(c, "date", RegexOptions.IgnoreCase)));
        if (headerIndex < 0) headerIndex = 0;
        string[] header = lines.Count > headerIndex ? lines[headerIndex] : [];
        var cells = lines.Skip(headerIndex + 1).Where(l => l.Any(c => c.Trim().Length > 0)).ToList();
        return new Statement { Format = "CSV", Headers = header.Select(h => h.Trim()).ToArray(), Cells = cells };
    }

    /// <summary>Splits CSV text into rows of cells. Handles quotes, and comma, semicolon or tab separators.</summary>
    public static List<string[]> ParseCsv(string text)
    {
        text = text.TrimStart('﻿');
        string firstLine = text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        char sep = new[] { ',', ';', '\t' }.OrderByDescending(c => firstLine.Count(ch => ch == c)).First();

        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString());
                cell.Clear();
                if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
                row = [];
            }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row.ToArray());
        }
        return rows;
    }

    /// <summary>Guesses the columns from common header names (Date, Description, Amount, Debit/Credit, Category…).</summary>
    public static CsvMapping GuessMapping(string[] headers)
    {
        int Find(params string[] names)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                string h = headers[i].Trim().ToLowerInvariant();
                if (names.Any(n => h == n)) return i;
            }
            for (int i = 0; i < headers.Length; i++)
            {
                string h = headers[i].Trim().ToLowerInvariant();
                if (names.Any(n => h.Contains(n))) return i;
            }
            return -1;
        }

        var map = new CsvMapping
        {
            Date = Find("date", "transaction date", "posted date", "posting date", "trans date"),
            Payee = Find("payee", "description", "merchant", "name", "details", "memo"),
            Amount = Find("amount", "transaction amount", "amt"),
            Debit = Find("debit", "withdrawal", "withdrawals", "money out", "paid out"),
            Credit = Find("credit", "deposit", "deposits", "money in", "paid in"),
            Category = Find("category"),
            Note = -1,
        };
        if (map.Amount >= 0 && (map.Amount == map.Debit || map.Amount == map.Credit)) { map.Debit = -1; map.Credit = -1; }
        if (map.Amount >= 0) { map.Debit = -1; map.Credit = -1; }
        int memo = Find("memo", "note", "notes", "reference");
        if (memo >= 0 && memo != map.Payee) map.Note = memo;
        return map;
    }

    /// <summary>Turns CSV cells into rows using the mapping. Rows without a readable date or amount are counted in <paramref name="skipped"/>.</summary>
    public static List<ImportedRow> Apply(Statement s, CsvMapping map, out int skipped)
    {
        skipped = 0;
        var rows = new List<ImportedRow>();
        if (!map.IsComplete) return rows;
        string Cell(string[] r, int i) => i >= 0 && i < r.Length ? r[i].Trim() : "";

        foreach (var r in s.Cells)
        {
            if (!TryParseDate(Cell(r, map.Date), out var date)) { skipped++; continue; }
            decimal? amount = null;
            if (map.Amount >= 0) amount = ParseAmount(Cell(r, map.Amount));
            else
            {
                var debit = ParseAmount(Cell(r, map.Debit));
                var credit = ParseAmount(Cell(r, map.Credit));
                if (debit is null && credit is null) amount = null;
                else amount = (credit is decimal c ? Math.Abs(c) : 0) - (debit is decimal d ? Math.Abs(d) : 0);
            }
            if (amount is not decimal a || a == 0) { skipped++; continue; }
            if (map.FlipSign) a = -a;
            rows.Add(new ImportedRow
            {
                Date = date,
                Amount = a,
                Payee = Cell(r, map.Payee),
                Category = Cell(r, map.Category),
                Note = Cell(r, map.Note),
            });
        }
        return rows;
    }

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "MM/dd/yy", "M-d-yyyy", "MM-dd-yyyy",
        "yyyyMMdd", "MMM d, yyyy", "MMM dd, yyyy", "d MMM yyyy", "dd MMM yyyy", "MMMM d, yyyy",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "M/d/yyyy h:mm:ss tt",
    ];

    private static readonly string[] DayFirstFormats = ["d/M/yyyy", "dd/MM/yyyy", "d.M.yyyy", "dd.MM.yyyy", "d/M/yy", "dd.MM.yy"];

    /// <summary>Reads common statement date formats. US month/day first, then day/month when the day is over 12.</summary>
    public static bool TryParseDate(string s, out DateTime date)
    {
        s = s.Trim().Trim('"');
        if (DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)) { date = date.Date; return true; }
        if (DateTime.TryParseExact(s, DayFirstFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)) { date = date.Date; return true; }
        return false;
    }

    /// <summary>
    /// Reads "-12.34", "$1,234.56", "(12.34)", "12.34-", "12.34 DR", "1.234,56". Null when it isn't a number.
    /// </summary>
    public static decimal? ParseAmount(string s)
    {
        s = s.Trim().Trim('"');
        if (s.Length == 0) return null;
        bool negative = false;
        if (s.StartsWith('(') && s.EndsWith(')')) { negative = true; s = s[1..^1]; }
        if (s.EndsWith("DR", StringComparison.OrdinalIgnoreCase)) { negative = true; s = s[..^2]; }
        if (s.EndsWith("CR", StringComparison.OrdinalIgnoreCase)) s = s[..^2];
        s = s.Trim();
        if (s.EndsWith('-')) { negative = true; s = s[..^1]; }
        s = new string(s.Where(c => char.IsDigit(c) || c is '.' or ',' or '-' or '−').ToArray()).Replace('−', '-');
        if (s.StartsWith('-')) { negative = !negative; s = s[1..]; }
        if (s.Length == 0) return null;

        // Decide the decimal separator from the last one used.
        int lastDot = s.LastIndexOf('.'), lastComma = s.LastIndexOf(',');
        if (lastComma > lastDot && s.Length - lastComma - 1 is 1 or 2) s = s.Replace(".", "").Replace(',', '.');
        else s = s.Replace(",", "");
        if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return null;
        return negative ? -value : value;
    }

    // ----- Duplicates and saving -----

    /// <summary>Marks rows already in WillowWallet (same bank id, or same date, amount and payee in that account).</summary>
    public static void MarkDuplicates(List<ImportedRow> rows, string account)
    {
        var existing = DataStore.Transactions.Items.Where(t => string.Equals(t.Account, account, StringComparison.OrdinalIgnoreCase)).ToList();
        var ids = existing.Where(t => t.ImportId.Length > 0).Select(t => t.ImportId).ToHashSet();
        var keys = existing.Select(t => Key(t.Date, t.Amount, t.Payee)).ToHashSet();
        foreach (var r in rows)
            r.IsDuplicate = (r.ImportId.Length > 0 && ids.Contains(r.ImportId)) || keys.Contains(Key(r.Date, r.Amount, r.Payee));
    }

    private static string Key(DateTime date, decimal amount, string payee) =>
        $"{date:yyyy-MM-dd}|{amount:0.00}|{SubscriptionFinder.Normalize(payee)}";

    /// <summary>The category a row will get: its own (if it matches one of yours), your rules/history, or Other.</summary>
    public static string CategoryFor(ImportedRow r)
    {
        bool income = r.Amount > 0;
        var known = income ? Categories.Income : Categories.Spending;
        string? own = known.FirstOrDefault(c => string.Equals(c, r.Category, StringComparison.OrdinalIgnoreCase));
        return own ?? Categories.Suggest(r.Payee, income) ?? (income ? "Other income" : "Other");
    }

    /// <summary>Saves the new (non-duplicate) rows to an account. Returns how many were added.</summary>
    public static int Commit(IEnumerable<ImportedRow> rows, string account)
    {
        var add = rows.Where(r => !r.IsDuplicate).Select(r => new Transaction
        {
            Date = r.Date,
            Payee = r.Payee.Length > 0 ? r.Payee : "(no description)",
            Amount = r.Amount,
            Account = account,
            Category = CategoryFor(r),
            Note = r.Note,
            ImportId = r.ImportId,
        }).ToList();
        if (add.Count > 0) DataStore.Transactions.AddRange(add);
        return add.Count;
    }
}
