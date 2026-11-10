using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Services.Translation;

/// <summary>Explicit demo fixtures, never a fallback for a failed real translation.</summary>
internal sealed class DemonstrationEngines : ISpeechRecognitionEngine, ITextTranslationEngine, IPronunciationEngine, ISpeechSynthesisEngine
{
    internal static IReadOnlyList<TranslationExample> Examples { get; } = Array.AsReadOnly(new[]
    {
        new TranslationExample(Language.Spanish, "Buenos días.", "Good morning.", "Gud mór-ning."),
        new TranslationExample(Language.Spanish, "Gracias.", "Thank you.", "Zánk iu."),
        new TranslationExample(Language.Spanish, "¿Cómo puedo ayudarle?", "How can I help you?", "Jáu kan ai jelp iu?"),
        new TranslationExample(Language.Spanish, "Voy a revisar la información.", "I will check the information.", "Ai uil chek di in-for-méi-shon."),
        new TranslationExample(Language.English, "Good morning.", "Buenos días.", "Bué-nos dí-as."),
        new TranslationExample(Language.English, "Thank you.", "Gracias.", "Grá-sias."),
        new TranslationExample(Language.English, "How can I help you?", "¿Cómo puedo ayudarle?", "Có-mo pué-do a-yu-dár-le?"),
        new TranslationExample(Language.English, "I will check the information.", "Voy a revisar la información.", "Voi a re-vi-sár la in-for-ma-sión.")
    });

    public async Task<string> TranslateAsync(string text, Language sourceLanguage, Language targetLanguage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return Examples.FirstOrDefault(example => example.SourceLanguage == sourceLanguage && example.TargetLanguage == targetLanguage &&
            string.Equals(example.SourceText, text.Trim().Normalize(), StringComparison.OrdinalIgnoreCase))?.TranslatedText
            ?? throw new TranslationEngineException(TranslationStage.TextTranslation, TranslationErrorCode.EngineFailure,
                "The demo contains only its listed fixtures.");
    }

    public Task<PronunciationResult> GenerateAsync(string text, Language targetLanguage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var example = Examples.FirstOrDefault(example => example.TargetLanguage == targetLanguage && example.TranslatedText == text);
        return example is null ? Task.FromException<PronunciationResult>(Unavailable(TranslationStage.Pronunciation)) :
            Task.FromResult(new PronunciationResult(example.Pronunciation, PronunciationNotation.Approximate));
    }

    public Task<string> TranscribeAsync(AudioData audio, Language sourceLanguage, CancellationToken cancellationToken = default) =>
        Task.FromException<string>(Unavailable(TranslationStage.SpeechRecognition));
    public Task<AudioData> SynthesizeAsync(string text, Language targetLanguage, CancellationToken cancellationToken = default) =>
        Task.FromException<AudioData>(Unavailable(TranslationStage.SpeechSynthesis));
    private static TranslationEngineException Unavailable(TranslationStage stage) =>
        new(stage, TranslationErrorCode.ModelUnavailable, "The demonstration does not provide this capability.");
}
