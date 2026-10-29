namespace Saylo.Modules.Translation.TextTranslation;

/// <summary>Validation constraints; glossary entries reject mismatches and never rewrite translations.</summary>
public sealed class TranslationIntegrityOptions
{
    public int MaximumSegmentLength { get; }
    public bool RequireFormalSpanish { get; }
    public IReadOnlyList<string> ProtectedTerms { get; }
    public IReadOnlyList<TranslationGlossaryEntry> Glossary { get; }

    public TranslationIntegrityOptions(int maximumSegmentLength = 240, bool requireFormalSpanish = false,
        IEnumerable<string>? protectedTerms = null, IEnumerable<TranslationGlossaryEntry>? glossary = null)
    {
        if (maximumSegmentLength is < 80 or > 240)
            throw new ArgumentOutOfRangeException(nameof(maximumSegmentLength));
        MaximumSegmentLength = maximumSegmentLength;
        RequireFormalSpanish = requireFormalSpanish;
        var terms = (protectedTerms ?? []).ToArray();
        if (terms.Length > 64 || terms.Any(term => string.IsNullOrWhiteSpace(term) || term.Length > 128))
            throw new ArgumentException("Protected terms must be bounded nonempty literals.", nameof(protectedTerms));
        ProtectedTerms = Array.AsReadOnly(terms.Distinct(StringComparer.Ordinal).ToArray());
        var entries = (glossary ?? []).ToArray();
        if (entries.Length > 64 || entries.Any(entry => entry is null))
            throw new ArgumentException("The glossary must be bounded.", nameof(glossary));
        Glossary = Array.AsReadOnly(entries);
    }
}

public sealed class TranslationGlossaryEntry
{
    public string SourceLanguage { get; }
    public string SourceTerm { get; }
    public IReadOnlyList<string> AllowedTargets { get; }

    public TranslationGlossaryEntry(string sourceLanguage, string sourceTerm, IEnumerable<string> allowedTargets)
    {
        if (sourceLanguage is not ("es" or "en"))
            throw new ArgumentException("Only es/en glossary entries are supported.", nameof(sourceLanguage));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTerm);
        ArgumentNullException.ThrowIfNull(allowedTargets);
        var targets = allowedTargets.ToArray();
        if (sourceTerm.Length > 128 || targets.Length is < 1 or > 16 ||
            targets.Any(term => string.IsNullOrWhiteSpace(term) || term.Length > 128))
            throw new ArgumentException("Glossary terms must be bounded nonempty literals.", nameof(allowedTargets));
        SourceLanguage = sourceLanguage;
        SourceTerm = sourceTerm;
        AllowedTargets = Array.AsReadOnly(targets);
    }
}
