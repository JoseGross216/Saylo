using System.Globalization;
using System.Text.RegularExpressions;

namespace Saylo.Modules.Translation.TextTranslation;

internal static class NumericIntegrity
{
    internal static readonly HashSet<string> CurrencyCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "USD", "EUR", "GBP", "CAD", "AUD", "NZD", "DOP", "VES", "MXN", "COP", "ARS", "CLP", "BRL", "JPY", "CHF", "CNY"
    };

    private const string Number = @"[-+\u2212]?\d+(?:[.,]\d+)*";
    private const string Currency = @"(?:USD|EUR|GBP|CAD|AUD|NZD|DOP|VES|MXN|COP|ARS|CLP|BRL|JPY|CHF|CNY|euros?|dollars?|d[o\u00f3]lares?|[$\u20ac\u00a3])";
    private const string Month = @"(?:enero|january|febrero|february|marzo|march|abril|april|mayo|may|junio|june|julio|july|agosto|august|septiembre|september|octubre|october|noviembre|november|diciembre|december)";
    private static readonly string[][] Months =
    [
        ["enero", "january"], ["febrero", "february"], ["marzo", "march"], ["abril", "april"],
        ["mayo", "may"], ["junio", "june"], ["julio", "july"], ["agosto", "august"],
        ["septiembre", "september"], ["octubre", "october"], ["noviembre", "november"], ["diciembre", "december"]
    ];

    internal sealed record Fact(int Position, string Kind, string Value, string Unit);

    internal static IReadOnlyList<Fact> Read(string text, TranslationIntegrityOptions options, IReadOnlyList<LiteralSpan>? restoredSpans = null)
    {
        var characters = text.ToCharArray();
        foreach (var span in ProtectedText.FindLiterals(text, options))
            Array.Fill(characters, ' ', span.Start, span.Length);
        foreach (var span in restoredSpans ?? [])
            Array.Fill(characters, ' ', span.Start, span.Length);
        var input = new string(characters);
        if (Regex.IsMatch(input, @"\d[\s\u00a0]+\d", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ||
            Regex.IsMatch(input, @"\(\s*(?:" + Currency + @"\s*)?" + Number + @"\s*(?:" + Currency + @")?\s*\)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw ProtectedText.Failure(IntegrityConstraint.NumericAmbiguity);
        var used = new bool[input.Length];
        var facts = new List<Fact>();
        void Scan(string pattern, Func<Match, (string Kind, string Value, string Unit)> parse)
        {
            foreach (Match match in Regex.Matches(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                         TimeSpan.FromMilliseconds(100)))
            {
                if (Enumerable.Range(match.Index, match.Length).Any(index => used[index]))
                    continue;
                var (kind, value, unit) = parse(match);
                facts.Add(new(match.Index, kind, value, unit));
                Array.Fill(used, true, match.Index, match.Length);
            }
        }

        Scan(@"(?<!\d)(?<a>\d{1,4})[-/](?<b>\d{1,2})[-/](?<c>\d{1,4})(?!\d)", match =>
        {
            var a = int.Parse(match.Groups["a"].Value, CultureInfo.InvariantCulture);
            var b = int.Parse(match.Groups["b"].Value, CultureInfo.InvariantCulture);
            var c = int.Parse(match.Groups["c"].Value, CultureInfo.InvariantCulture);
            int year, month, day;
            if (match.Groups["a"].Length == 4)
            {
                if (match.Value.Contains('/') && b <= 12 && c <= 12)
                    throw ProtectedText.Failure(IntegrityConstraint.NumericAmbiguity);
                (year, month, day) = (a, b, c);
            }
            else
            {
                if (match.Groups["c"].Length != 4 || a <= 12 && b <= 12)
                    throw ProtectedText.Failure(IntegrityConstraint.NumericAmbiguity);
                (year, month, day) = a > 12 ? (c, b, a) : (c, a, b);
            }
            return ("date", Date(year, month, day), "");
        });
        Scan(@"(?<day>\d{1,2})(?:st|nd|rd|th)?\s+(?:de\s+)?(?<month>" + Month + @")\s+(?:de\s+)?(?<year>\d{4})\b", NamedDate);
        Scan(@"\b(?<month>" + Month + @")\s+(?<day>\d{1,2})(?:st|nd|rd|th)?(?:,)?\s+(?<year>\d{4})\b", NamedDate);
        Scan(@"(?<!\d)(?<hour>\d{1,2})\s*:\s*(?<minute>\d{2})(?:\s*(?<meridiem>[ap]\.?m\.?))?(?!\d)", Time);
        Scan(@"(?<![\p{L}\d])(?<hour>\d{1,2})\s*(?<meridiem>[ap]\.?m\.?)(?!\p{L})", Time);
        Scan(@"(?<!\d)\d+\s*[-\u2013\u2014]\s*\d+(?!\d)", _ => throw ProtectedText.Failure(IntegrityConstraint.NumericAmbiguity));
        Scan(@"(?<![\p{L}\d])(?<number>" + Number + @")\s*(?<unit>" + Currency + @")(?!\p{L})", Money);
        Scan(@"(?<![\p{L}\d])(?<prefixSign>[-+\u2212]?)(?<unit>" + Currency + @")\s*(?<number>" + Number + @")(?![\p{L}\d])", Money);
        Scan(@"(?<![\p{L}\d])(?<number>" + Number + @")\s*(?:%|percent(?:age)?|por\s+ciento)(?!\p{L})",
            match => ("number", Parse(match.Groups["number"].Value), "percent"));
        Scan(@"(?<![\p{L}\d])(?<number>" + Number + @")\s+(?<unit>units?|unidades?|kg|kilograms?|kilogramos?|hours?|horas?|days?|d[i\u00ed]as?)(?!\p{L})",
            match => ("number", Parse(match.Groups["number"].Value), Unit(match.Groups["unit"].Value)));
        Scan(@"(?<![\p{L}\d])(?<number>" + Number + @")(?:st|nd|rd|th)?(?![\p{L}\d])",
            match => ("number", Parse(match.Groups["number"].Value), ""));
        Scan(@"(?<!\p{L})(?<unit>" + Currency + @")(?!\p{L})",
            match => ("currency-marker", "", match.Groups["unit"].Value.ToUpperInvariant()));
        for (var index = 0; index < input.Length; index++)
            if (char.IsDigit(input[index]) && !used[index])
                throw ProtectedText.Failure();
        return facts.OrderBy(fact => fact.Position).ToArray();
    }

    internal static void Validate(string source, string target, TranslationIntegrityOptions options, IReadOnlyList<LiteralSpan>? restoredSpans = null)
    {
        // Value, kind, unit and occurrence order are checked; display formatting may differ.
        var before = Read(source, options).Select(fact => (fact.Kind, fact.Value, fact.Unit));
        var after = Read(target, options, restoredSpans).Select(fact => (fact.Kind, fact.Value, fact.Unit));
        if (!before.SequenceEqual(after))
            throw ProtectedText.Failure(IntegrityConstraint.NumericEquivalence);
    }

    private static (string, string, string) Money(Match match)
    {
        var rawUnit = match.Groups["unit"].Value.ToUpperInvariant();
        var unit = rawUnit switch
        {
            "EURO" or "EUROS" or "\u20ac" => "EUR",
            "$" or "DOLLAR" or "DOLLARS" or "DOLAR" or "DOLARES" or "D\u00d3LAR" or "D\u00d3LARES" => "UNSPECIFIED_DOLLAR",
            "\u00a3" => "GBP",
            _ => rawUnit
        };
        var prefixSign = match.Groups["prefixSign"].Value;
        var number = match.Groups["number"].Value;
        if (prefixSign.Length > 0 && number[0] is '-' or '+' or '\u2212')
            throw ProtectedText.Failure(IntegrityConstraint.NumericAmbiguity);
        return ("number", Parse(prefixSign + number), "currency:" + unit);
    }

    private static string Unit(string value) => value.ToLowerInvariant() switch
    {
        "unit" or "units" or "unidad" or "unidades" => "count",
        "kg" or "kilogram" or "kilograms" or "kilogramo" or "kilogramos" => "kg",
        "hour" or "hours" or "hora" or "horas" => "hour",
        _ => "day"
    };

    private static (string, string, string) NamedDate(Match match)
    {
        var month = Array.FindIndex(Months, names => names.Contains(match.Groups["month"].Value, StringComparer.OrdinalIgnoreCase)) + 1;
        return ("date", Date(int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture), month,
            int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture)), "");
    }

    private static string Date(int year, int month, int day)
    {
        try { return new DateOnly(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        catch (ArgumentOutOfRangeException) { throw ProtectedText.Failure(); }
    }

    private static (string, string, string) Time(Match match)
    {
        var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var minute = match.Groups["minute"].Success ? int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture) : 0;
        var meridiem = match.Groups["meridiem"].Value.ToLowerInvariant().Replace(".", "", StringComparison.Ordinal);
        if (minute > 59 || meridiem.Length == 0 && hour > 23 || meridiem.Length > 0 && hour is < 1 or > 12)
            throw ProtectedText.Failure();
        if (meridiem.Length > 0)
            hour = hour % 12 + (meridiem[0] == 'p' ? 12 : 0);
        return ("time", $"{hour:D2}:{minute:D2}", "");
    }

    private static string Parse(string number)
    {
        var sign = number.StartsWith('-') || number.StartsWith('\u2212') ? "-" : "";
        var unsigned = number.TrimStart('-', '+', '\u2212');
        if (unsigned.Length > 1 && unsigned[0] == '0' && !unsigned.Contains('.') && !unsigned.Contains(','))
            throw ProtectedText.Failure(IntegrityConstraint.NumericAmbiguity);
        var comma = unsigned.LastIndexOf(',');
        var dot = unsigned.LastIndexOf('.');
        var canonical = unsigned;
        if (comma >= 0 && dot >= 0)
        {
            var decimalSeparator = comma > dot ? ',' : '.';
            var groupingSeparator = decimalSeparator == ',' ? '.' : ',';
            var parts = unsigned.Split(decimalSeparator);
            if (parts.Length != 2 || !ValidGroups(parts[0], groupingSeparator) || parts[1].Length is < 1 or > 6)
                throw ProtectedText.Failure();
            canonical = parts[0].Replace(groupingSeparator.ToString(), "", StringComparison.Ordinal) + "." + parts[1];
        }
        else if (comma >= 0 || dot >= 0)
        {
            var separator = comma >= 0 ? ',' : '.';
            var parts = unsigned.Split(separator);
            if (parts.Length == 2)
            {
                if (parts[1].Length == 3 || parts[1].Length is < 1 or > 6)
                    throw ProtectedText.Failure();
                canonical = parts[0] + "." + parts[1];
            }
            else
            {
                if (!ValidGroups(unsigned, separator))
                    throw ProtectedText.Failure();
                canonical = string.Concat(parts);
            }
        }
        if (!decimal.TryParse(sign + canonical, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value))
            throw ProtectedText.Failure();
        return value.ToString("G29", CultureInfo.InvariantCulture);
    }

    private static bool ValidGroups(string text, char separator)
    {
        var parts = text.Split(separator);
        return parts[0].Length is >= 1 and <= 3 && parts.Skip(1).All(part => part.Length == 3);
    }
}
