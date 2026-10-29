using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Translation.TextTranslation;

internal sealed record LiteralSpan(int Start, int Length, string Value);
internal enum IntegrityConstraint { UnverifiedInput, MarkerCorrespondence, LiteralCorrespondence, NumericEquivalence, NumericAmbiguity, Negation, Referent, TemporalRelation, FormalRegister, Glossary, Segmentation, OutputBudget }

internal sealed class ProtectedText
{
    private const string TechnicalTerms = @"(?i)\b(?:SQL|VPN|HTTP|HTTPS|API|DNS|CPU|GPU|USB|JSON|XML|ASCII|UUID|GUID|IP|TCP|UDP|FTP|SSH)\b";
    internal string Source { get; }
    internal string Masked { get; }
    internal IReadOnlyList<(string Marker, string Original)> Entries { get; }

    private ProtectedText(string source, string masked, List<(string Marker, string Original)> entries)
    {
        Source = source;
        Masked = masked;
        Entries = entries.AsReadOnly();
    }

    internal static ProtectedText Create(string text, TranslationIntegrityOptions options, Func<string>? nextMarker = null)
    {
        nextMarker ??= () => RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var spans = FindLiterals(text, options);
        if (spans.Count > 64)
            throw Failure();
        var result = new StringBuilder();
        var entries = new List<(string Marker, string Original)>();
        var offset = 0;
        foreach (var span in spans)
        {
            string? marker = null;
            for (var attempt = 0; attempt < 128; attempt++)
            {
                var candidate = nextMarker();
                if (!Regex.IsMatch(candidate, @"^[1-9][0-9]{8}$", RegexOptions.CultureInvariant))
                    throw Failure();
                if (!text.Contains(candidate, StringComparison.Ordinal) && entries.All(entry => entry.Marker != candidate))
                {
                    marker = candidate;
                    break;
                }
            }
            if (marker is null)
                throw Failure();
            result.Append(text.AsSpan(offset, span.Start - offset)).Append(marker);
            entries.Add((marker, span.Value));
            offset = span.Start + span.Length;
        }
        result.Append(text.AsSpan(offset));
        if (result.Length > ArgosTranslationOptions.MaximumTextLength)
            throw Failure();
        return new(text, result.ToString(), entries);
    }

    internal string Restore(string translated) => Restore(translated, out _);

    internal string Restore(string translated, out IReadOnlyList<LiteralSpan> restoredSpans)
    {
        // Validate every occurrence and its order before restoring any literal.
        var positions = new List<(int Start, int Length, string Original)>();
        foreach (var (marker, original) in Entries)
        {
            var matches = Regex.Matches(translated, @"(?<![\p{L}\p{N}])" + marker + @"(?![\p{L}\p{N}])",
                RegexOptions.CultureInvariant);
            if (matches.Count != 1)
                throw Failure(IntegrityConstraint.MarkerCorrespondence);
            positions.Add((matches[0].Index, marker.Length, original));
        }
        if (!positions.Select(item => item.Start).SequenceEqual(positions.Select(item => item.Start).Order()))
            throw Failure(IntegrityConstraint.MarkerCorrespondence);
        var result = new StringBuilder();
        var spans = new List<LiteralSpan>();
        var offset = 0;
        foreach (var item in positions)
        {
            result.Append(translated.AsSpan(offset, item.Start - offset));
            spans.Add(new(result.Length, item.Original.Length, item.Original));
            result.Append(item.Original);
            offset = item.Start + item.Length;
        }
        result.Append(translated.AsSpan(offset));
        restoredSpans = spans.AsReadOnly();
        return result.ToString();
    }

    internal static IReadOnlyList<LiteralSpan> FindLiterals(string text, TranslationIntegrityOptions options)
    {
        var candidates = new List<LiteralSpan>();
        void Add(string pattern, bool trimPunctuation = false)
        {
            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                var value = match.Value;
                if (trimPunctuation)
                {
                    value = value.TrimEnd('.', ',', ';', ':', '!', '?');
                    while (value.EndsWith(')') && value.Count(character => character == ')') > value.Count(character => character == '('))
                        value = value[..^1];
                }
                if (value.Length > 0)
                    candidates.Add(new(match.Index, value.Length, value));
            }
        }

        Add(@"[\p{L}\p{N}._%+\-]+@(?:[\p{L}\p{N}\-]+\.)+[\p{L}]{2,}");
        Add(@"(?i)\b(?:https?://|www\.)[^\s<>""\u201c\u201d]+", trimPunctuation: true);
        Add(@"(?<![\p{L}\p{N}])(?:\+\d{1,3}[\s.-]?(?:\(\d{2,4}\)[\s.-]?)?\d(?:[\s.-]?\d){6,12}|\(\d{2,4}\)[\s.-]?\d{3}[\s.-]?\d{4}|\d{2,4}[- .]\d{3}[- .]\d{4})(?![\p{L}\p{N}])");
        foreach (Match reference in Regex.Matches(text,
            @"(?i)\b(?:pedido|order|c[o\u00f3]digo|code|tracking(?: number)?|seguimiento|referencia|reference|telefono|phone)\s+(?<identifier>\d+)\b"))
        {
            var group = reference.Groups["identifier"];
            candidates.Add(new(group.Index, group.Length, group.Value));
        }
        Add(@"\b(?=[\p{L}\p{N}_-]*\p{L})(?=[\p{L}\p{N}_-]*\d)[\p{L}\p{N}_]+(?:[-_][\p{L}\p{N}_]+)*\b");
        Add(TechnicalTerms);
        foreach (var term in options.ProtectedTerms)
            Add(@"(?<![\p{L}\p{N}])" + Regex.Escape(term) + @"(?![\p{L}\p{N}])");

        var spans = new List<LiteralSpan>();
        foreach (var item in candidates.OrderBy(item => item.Start).ThenByDescending(item => item.Length))
        {
            if (NumericIntegrity.CurrencyCodes.Contains(item.Value) ||
                Regex.IsMatch(item.Value, @"(?i)^\d+(st|nd|rd|th)$"))
                continue;
            if (spans.Any(span => item.Start < span.Start + span.Length && span.Start < item.Start + item.Length))
                continue;
            spans.Add(item);
        }
        return spans;
    }

    internal static TranslationEngineException Failure(IntegrityConstraint constraint = IntegrityConstraint.UnverifiedInput)
    {
        var failure = new TranslationEngineException(TranslationStage.TextTranslation, TranslationErrorCode.IntegrityValidationFailed,
            "Local translation failed an integrity constraint; review or clarify the source message.");
        failure.Data["IntegrityConstraint"] = constraint.ToString();
        return failure;
    }
}
