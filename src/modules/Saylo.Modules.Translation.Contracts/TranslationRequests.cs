namespace Saylo.Modules.Translation.Contracts;

public sealed record TextTranslationRequest
{
    public string Text { get; }
    public Language SourceLanguage { get; }
    public Language TargetLanguage { get; }

    public TextTranslationRequest(string text, Language sourceLanguage, Language targetLanguage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        RequestValidation.ValidateLanguages(sourceLanguage, targetLanguage);
        Text = text;
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
    }
}

public sealed record VoiceTranslationRequest
{
    public AudioData Audio { get; }
    public Language SourceLanguage { get; }
    public Language TargetLanguage { get; }

    public VoiceTranslationRequest(AudioData audio, Language sourceLanguage, Language targetLanguage)
    {
        ArgumentNullException.ThrowIfNull(audio);
        RequestValidation.ValidateLanguages(sourceLanguage, targetLanguage);
        Audio = audio;
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
    }
}

internal static class RequestValidation
{
    internal static void ValidateLanguages(Language sourceLanguage, Language targetLanguage)
    {
        ArgumentNullException.ThrowIfNull(sourceLanguage);
        ArgumentNullException.ThrowIfNull(targetLanguage);
        if (sourceLanguage == targetLanguage)
            throw new ArgumentException("Source and target languages must differ.", nameof(targetLanguage));
    }
}
