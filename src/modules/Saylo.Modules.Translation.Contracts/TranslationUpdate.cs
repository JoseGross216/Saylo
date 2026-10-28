namespace Saylo.Modules.Translation.Contracts;

public enum TranslationStage
{
    SpeechRecognition,
    TextTranslation,
    Pronunciation,
    SpeechSynthesis
}

public enum TranslationErrorCode
{
    EngineFailure,
    InvalidEngineOutput
}

/// <summary>OperationId correlates updates without retaining conversation history.</summary>
public abstract record TranslationUpdate(Guid OperationId);

public sealed record TranscriptionCompleted(Guid OperationId, string Text, Language SourceLanguage)
    : TranslationUpdate(OperationId);

public sealed record TextTranslationCompleted(Guid OperationId, string SourceText, string TranslatedText,
    Language SourceLanguage, Language TargetLanguage) : TranslationUpdate(OperationId);

public sealed record PronunciationCompleted(Guid OperationId, PronunciationResult Pronunciation)
    : TranslationUpdate(OperationId);

public sealed record SpeechSynthesisCompleted(Guid OperationId, AudioData Audio)
    : TranslationUpdate(OperationId);

// Provider exception messages are deliberately excluded: they may contain user content.
public sealed record TranslationStageFailed(Guid OperationId, TranslationStage Stage, TranslationErrorCode Code)
    : TranslationUpdate(OperationId);
