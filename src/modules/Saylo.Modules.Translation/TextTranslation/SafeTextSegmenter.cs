using System.Text.RegularExpressions;

namespace Saylo.Modules.Translation.TextTranslation;

internal sealed record SourceSegment(string Text, string Separator);

internal static class SafeTextSegmenter
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "sr", "sra", "srta", "dr", "dra", "mr", "mrs", "ms", "prof", "etc", "ud", "uds",
        "e.g", "i.e", "p.ej", "a.m", "p.m", "no", "núm", "num"
    };

    internal static IReadOnlyList<SourceSegment> Split(string text, TranslationIntegrityOptions options)
    {
        var protectedSpans = ProtectedText.FindLiterals(text, options);
        var segments = new List<SourceSegment>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (protectedSpans.Any(span => index >= span.Start && index < span.Start + span.Length))
                continue;
            var character = text[index];
            if (character is not ('.' or '!' or '?' or '\r' or '\n'))
                continue;
            if (character == '.' && IsInternalPeriod(text, index))
                continue;
            var end = index + 1;
            while (end < text.Length && text[end] is '"' or '\'' or '\u00bb' or '\u201d' or ')' or ']' or '.' or '!' or '?')
                end++;
            if (character is not ('\r' or '\n') && end < text.Length && !char.IsWhiteSpace(text[end]))
                continue;
            if (character is '\r' or '\n')
                end = index;
            var separatorEnd = end;
            while (separatorEnd < text.Length && char.IsWhiteSpace(text[separatorEnd]))
                separatorEnd++;
            var part = text[start..end];
            var separator = text[end..separatorEnd];
            Add(segments, part, separator, options);
            start = separatorEnd;
            index = separatorEnd - 1;
        }
        if (start < text.Length)
            Add(segments, text[start..], "", options);
        if (segments.Count is < 1 or > 64)
            throw ProtectedText.Failure();
        return segments;
    }

    private static void Add(List<SourceSegment> segments, string text, string separator, TranslationIntegrityOptions options)
    {
        if (text.Length > options.MaximumSegmentLength)
            throw ProtectedText.Failure();
        // Whitespace stays outside inference and retains its exact place in the result.
        if (string.IsNullOrWhiteSpace(text))
        {
            if (segments.Count == 0)
                segments.Add(new("", text + separator));
            else
                segments[^1] = segments[^1] with { Separator = segments[^1].Separator + text + separator };
            return;
        }
        var leading = text.Length - text.TrimStart().Length;
        if (leading > 0)
        {
            Add(segments, "", text[..leading], options);
            text = text[leading..];
        }
        var trailing = text.Length - text.TrimEnd().Length;
        if (trailing > 0)
        {
            separator = text[^trailing..] + separator;
            text = text[..^trailing];
        }
        segments.Add(new(text, separator));
    }

    private static bool IsInternalPeriod(string text, int index)
    {
        if (index > 0 && index + 1 < text.Length && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]))
            return true;
        var prefix = text[..index];
        var word = Regex.Match(prefix, @"[\p{L}.]+$").Value;
        return Abbreviations.Contains(word) || Regex.IsMatch(word, @"^(?:[A-Za-z]\.)+[A-Za-z]$") || word.Length == 1 && char.IsUpper(word[0]) ||
            index + 2 < text.Length && char.IsLetter(text[index + 1]) && text[index + 2] == '.';
    }
}
