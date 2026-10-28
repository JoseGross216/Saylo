namespace Saylo.Modules.Translation.Contracts;

public enum PronunciationNotation
{
    Approximate = 1,
    Ipa = 2
}

/// <summary>
/// Pronunciation supplied by a validated phonetic engine, never inferred by the coordinator.
/// Approximate notation must be presented as approximate by consumers.
/// </summary>
public sealed record PronunciationResult
{
    public string Text { get; }
    public PronunciationNotation Notation { get; }

    public PronunciationResult(string text, PronunciationNotation notation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!Enum.IsDefined(notation))
            throw new ArgumentOutOfRangeException(nameof(notation));
        Text = text;
        Notation = notation;
    }
}
