using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Globalization;

namespace Saylo.Modules.Translation.TextTranslation;

internal sealed class TranslationIntegrityPlan
{
    internal sealed record Segment(ProtectedText Protection, string Separator);
    internal IReadOnlyList<Segment> Segments { get; }
    private readonly TranslationIntegrityOptions _options;
    private readonly string _source;
    private readonly string _target;

    private TranslationIntegrityPlan(IReadOnlyList<Segment> segments, TranslationIntegrityOptions options, string source, string target)
    {
        Segments = segments;
        _options = options;
        _source = source;
        _target = target;
    }

    internal static TranslationIntegrityPlan Prepare(string text, string source, string target, TranslationIntegrityOptions options)
    {
        _ = NumericIntegrity.Read(text, options);
        var usedMarkers = new HashSet<string>(StringComparer.Ordinal);
        string NextMarker()
        {
            for (var attempt = 0; attempt < 128; attempt++)
            {
                var candidate = RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000).ToString(CultureInfo.InvariantCulture);
                if (!text.Contains(candidate, StringComparison.Ordinal) && usedMarkers.Add(candidate))
                    return candidate;
            }
            throw ProtectedText.Failure();
        }
        var segments = SafeTextSegmenter.Split(text, options).Select(segment =>
            new Segment(ProtectedText.Create(segment.Text, options, NextMarker), segment.Separator)).ToArray();
        return new(segments, options, source, target);
    }

    internal string ValidateSegment(Segment segment, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 4096)
            throw ProtectedText.Failure();
        var restored = segment.Protection.Restore(candidate.Trim(), out var restoredSpans);
        var source = segment.Protection.Source;
        var additionalLiterals = ProtectedText.FindLiterals(restored, _options)
            .Where(span => !restoredSpans.Any(protectedSpan => span.Start >= protectedSpan.Start &&
                span.Start + span.Length <= protectedSpan.Start + protectedSpan.Length))
            .Where(span => span.Value.Any(char.IsDigit) || span.Value.Contains('@') || span.Value.Contains("://", StringComparison.Ordinal) ||
                _options.ProtectedTerms.Contains(span.Value, StringComparer.Ordinal) ||
                segment.Protection.Entries.Any(entry => entry.Original == span.Value));
        if (additionalLiterals.Any())
            throw ProtectedText.Failure(IntegrityConstraint.LiteralCorrespondence);
        NumericIntegrity.Validate(source, restored, _options, restoredSpans);
        LinguisticConstraints.Validate(source, restored, _source, _target, _options, restoredSpans);
        return restored;
    }

    internal string Complete(IReadOnlyList<string> validated)
    {
        if (validated.Count != Segments.Count)
            throw ProtectedText.Failure();
        var result = new StringBuilder();
        for (var index = 0; index < validated.Count; index++)
        {
            if (result.Length + validated[index].Length + Segments[index].Separator.Length > 8000)
                throw ProtectedText.Failure();
            result.Append(validated[index]).Append(Segments[index].Separator);
        }
        return result.ToString();
    }
}

internal static class LinguisticConstraints
{
    private const string NegativeSpanish = @"\b(no(?!\.\s*\d)|nunca|jam[a\u00e1]s|sin)\b";
    private const string NegativeEnglish = @"\b(no(?!\.\s*\d)|not|never|without|cannot)\b|\b\p{L}+n['\u2019]t\b";

    internal static void Validate(string source, string translated, string from, string to, TranslationIntegrityOptions options,
        IReadOnlyList<LiteralSpan>? restoredSpans = null)
    {
        source = SemanticText(source, options, null);
        translated = SemanticText(translated, options, restoredSpans);
        bool Has(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        var sourceNegative = Has(source, from == "es" ? NegativeSpanish : NegativeEnglish);
        var targetNegative = Has(translated, to == "es" ? NegativeSpanish : NegativeEnglish);
        if (sourceNegative != targetNegative)
            throw ProtectedText.Failure(IntegrityConstraint.Negation);
        if (Has(source, from == "es" ? @"\b(nunca|jam[a\u00e1]s)\b" : @"\bnever\b") &&
            !Has(translated, to == "es" ? @"\b(nunca|jam[a\u00e1]s)\b" : @"\bnever\b"))
            throw ProtectedText.Failure(IntegrityConstraint.Negation);
        // Do not infer a third-person owner from an article or an unresolved Spanish possessive.
        if (from == "es" && Has(translated, @"\b(his|her|their)\b") &&
            !Has(source, @"\bde\s+(?:[e\u00e9]l|ella|ellos|ellas)\b"))
            throw ProtectedText.Failure(IntegrityConstraint.Referent);
        // Explicit plural commitments must not become a singular speaker's responsibility.
        // This conservative finite vocabulary is not a general subject or discourse parser.
        if (from == "es" && Has(source, @"\b(informaremos|confirmaremos|enviaremos|revisaremos|contactaremos)\b") &&
            (!Has(translated, @"\bwe\b") || Has(translated, @"\b(I|me|my|mine)\b")))
            throw ProtectedText.Failure(IntegrityConstraint.Referent);
        if (Has(source, from == "es" ? @"\bantes\b" : @"\b(before|prior\s+to)\b") &&
            !Has(translated, to == "es" ? @"\b(antes|previamente)\b" : @"\b(before|prior\s+to)\b"))
            throw ProtectedText.Failure(IntegrityConstraint.TemporalRelation);
        if (options.RequireFormalSpanish && to == "es" &&
            Has(translated, @"\b(t[u\u00fa]|tus|te|contigo|vosotros|vosotras)\b"))
            throw ProtectedText.Failure(IntegrityConstraint.FormalRegister);
        foreach (var entry in options.Glossary.Where(entry => entry.SourceLanguage == from))
        {
            if (Has(source, Term(entry.SourceTerm)) && !entry.AllowedTargets.Any(term => Has(translated, Term(term))))
                throw ProtectedText.Failure(IntegrityConstraint.Glossary);
        }
    }

    private static string Term(string text) => @"(?<!\p{L})" + Regex.Escape(text).Replace(@"\ ", @"\s+", StringComparison.Ordinal) + @"(?!\p{L})";

    private static string SemanticText(string text, TranslationIntegrityOptions options, IReadOnlyList<LiteralSpan>? restoredSpans)
    {
        var characters = text.ToCharArray();
        foreach (var span in ProtectedText.FindLiterals(text, options).Concat(restoredSpans ?? []))
            Array.Fill(characters, ' ', span.Start, span.Length);
        return new string(characters);
    }
}
