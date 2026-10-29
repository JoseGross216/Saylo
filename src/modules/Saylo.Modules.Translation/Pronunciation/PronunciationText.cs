using System.Text;
using System.Text.RegularExpressions;
using Saylo.Modules.Translation.TextTranslation;

namespace Saylo.Modules.Translation.Pronunciation;

internal static class PronunciationText
{
    internal const int MaximumLength = 8000;
    private static readonly Regex Tokens = new(@"[\p{L}\p{M}]+(?:['\u2019][\p{L}\p{M}]+)*|\d+|\s+|.",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
        { "mr", "mrs", "ms", "dr", "dra", "sr", "sra", "srta", "ud", "uds", "etc", "vs", "st" };
    private static readonly string[] EnglishSmall =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
    private static readonly string[] SpanishSmall =
        ["cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve", "veinte", "veintiuno", "veintidós", "veintitrés", "veinticuatro", "veinticinco", "veintiséis", "veintisiete", "veintiocho", "veintinueve"];

    internal static string[] Prepare(string text, string language, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumLength)
            throw PronunciationFailure.Create("InputBudget");
        token.ThrowIfCancellationRequested();
        try { text = text.Normalize(NormalizationForm.FormC).Replace('’', '\''); }
        catch (ArgumentException) { throw PronunciationFailure.Create("InvalidUnicode"); }
        if (ProtectedText.FindLiterals(text, new()).Count > 0)
            throw PronunciationFailure.Create("UnverifiedIdentifier");
        if (text.Any(char.IsDigit))
        {
            // Only a standalone cardinal has an explicit supported reading policy.
            var cardinal = text.Trim();
            if (cardinal.Length > 2 || cardinal.Length > 1 && cardinal[0] == '0' || cardinal.Any(value => value is < '0' or > '9'))
                throw PronunciationFailure.Create("NumericReadingUnavailable");
            var value = int.Parse(cardinal, System.Globalization.CultureInfo.InvariantCulture);
            if (language == "en")
                text = value < 20 ? EnglishSmall[value] :
                    new[] { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" }[value / 10] +
                    (value % 10 == 0 ? "" : " " + EnglishSmall[value % 10]);
            else
                text = value < 30 ? SpanishSmall[value] :
                    new[] { "", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa" }[value / 10] +
                    (value % 10 == 0 ? "" : " y " + SpanishSmall[value % 10]);
        }
        var result = new List<string>();
        var spoken = false;
        foreach (Match match in Tokens.Matches(text))
        {
            token.ThrowIfCancellationRequested();
            var value = match.Value;
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (char.IsLetter(value[0]))
            {
                if (value.Length > 80)
                    throw PronunciationFailure.Create("WordBudget");
                if (Abbreviations.Contains(value) || value.Length > 1 && value.Where(char.IsLetter).All(char.IsUpper))
                    throw PronunciationFailure.Create("UnverifiedAbbreviation");
                spoken = true;
            }
            else if (value.Length != 1 || !".,;:!?¿¡«»“”\"()[]—-".Contains(value[0]))
                throw PronunciationFailure.Create("UnsupportedSymbol");
            result.Add(value);
        }
        if (!spoken)
            throw PronunciationFailure.Create("NoWords");
        return result.ToArray();
    }
}
