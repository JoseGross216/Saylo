using System.Collections.Concurrent;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Translation.Tests;

// Fixtures verify coordination only. These are not production engines or linguistic examples.
internal sealed class TestEngines : ISpeechRecognitionEngine, ITextTranslationEngine,
    IPronunciationEngine, ISpeechSynthesisEngine
{
    internal Func<AudioData, Language, CancellationToken, Task<string>> Recognize { get; set; } =
        (_, _, _) => Task.FromResult("transcription fixture");
    internal Func<string, Language, Language, CancellationToken, Task<string>> Translate { get; set; } =
        (_, _, _, _) => Task.FromResult("translation fixture");
    internal Func<string, Language, CancellationToken, Task<PronunciationResult>> Pronounce { get; set; } =
        (_, _, _) => Task.FromResult(new PronunciationResult("phonetic fixture", PronunciationNotation.Ipa));
    internal Func<string, Language, CancellationToken, Task<AudioData>> Synthesize { get; set; } =
        (_, _, _) => Task.FromResult(ContractValidationTests.CreateAudio());

    internal ConcurrentQueue<(TranslationStage Stage, CancellationToken Token)> Calls { get; } = new();

    internal TranslationService CreateService() => new(this, this, this, this);

    public Task<string> TranscribeAsync(AudioData audio, Language sourceLanguage, CancellationToken cancellationToken)
    {
        Calls.Enqueue((TranslationStage.SpeechRecognition, cancellationToken));
        return Recognize(audio, sourceLanguage, cancellationToken);
    }

    public Task<string> TranslateAsync(string text, Language sourceLanguage, Language targetLanguage,
        CancellationToken cancellationToken)
    {
        Calls.Enqueue((TranslationStage.TextTranslation, cancellationToken));
        return Translate(text, sourceLanguage, targetLanguage, cancellationToken);
    }

    public Task<PronunciationResult> GenerateAsync(string translatedText, Language targetLanguage,
        CancellationToken cancellationToken)
    {
        Calls.Enqueue((TranslationStage.Pronunciation, cancellationToken));
        return Pronounce(translatedText, targetLanguage, cancellationToken);
    }

    public Task<AudioData> SynthesizeAsync(string translatedText, Language targetLanguage,
        CancellationToken cancellationToken)
    {
        Calls.Enqueue((TranslationStage.SpeechSynthesis, cancellationToken));
        return Synthesize(translatedText, targetLanguage, cancellationToken);
    }
}
