namespace Saylo.Modules.Translation.Contracts;

// Implementations process locally, honor cancellation and must not block the caller thread.
// Audio and text are session data and must not be persisted or sent to remote APIs.
public interface ISpeechRecognitionEngine
{
    Task<string> TranscribeAsync(AudioData audio, Language sourceLanguage, CancellationToken cancellationToken = default);
}

public interface ITextTranslationEngine
{
    Task<string> TranslateAsync(string text, Language sourceLanguage, Language targetLanguage,
        CancellationToken cancellationToken = default);
}

public interface IPronunciationEngine
{
    Task<PronunciationResult> GenerateAsync(string translatedText, Language targetLanguage,
        CancellationToken cancellationToken = default);
}

public interface ISpeechSynthesisEngine
{
    Task<AudioData> SynthesizeAsync(string translatedText, Language targetLanguage,
        CancellationToken cancellationToken = default);
}
