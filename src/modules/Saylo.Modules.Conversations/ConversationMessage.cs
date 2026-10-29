using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Conversations;

public enum MessageOrigin { Text, ReviewedVoice, Mixed }
public enum MessageState { Translating, Enriching, Ready, Failed, Canceled }
public enum ResultState { Pending, Ready, Failed, Unavailable }

public sealed class ConversationMessage
{
    internal ConversationMessage(TextTranslationRequest request, MessageOrigin origin)
    {
        SourceText = request.Text;
        SourceLanguage = request.SourceLanguage;
        TargetLanguage = request.TargetLanguage;
        Origin = origin;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public Guid? OperationId { get; private set; }
    public Language SourceLanguage { get; }
    public Language TargetLanguage { get; }
    public MessageOrigin Origin { get; }
    public string SourceText { get; private set; }
    public string TranslatedText { get; private set; } = string.Empty;
    public PronunciationResult? Pronunciation { get; private set; }
    public AudioData? Audio { get; private set; }
    public MessageState State { get; private set; } = MessageState.Translating;
    public ResultState PronunciationState { get; private set; } = ResultState.Pending;
    public ResultState AudioState { get; private set; } = ResultState.Pending;
    public bool HasTranslation => !string.IsNullOrWhiteSpace(TranslatedText);
    public bool IsErased { get; private set; }
    public bool AutoplayClaimed { get; private set; }

    internal bool Apply(TranslationUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (IsErased || State is MessageState.Canceled or MessageState.Failed or MessageState.Ready ||
            (OperationId is { } id && id != update.OperationId)) return false;

        switch (update)
        {
            case TextTranslationCompleted text when !HasTranslation &&
                text.SourceLanguage == SourceLanguage && text.TargetLanguage == TargetLanguage &&
                text.SourceText == SourceText && !string.IsNullOrWhiteSpace(text.TranslatedText):
                TranslatedText = text.TranslatedText;
                State = MessageState.Enriching;
                break;
            case PronunciationCompleted pronunciation when HasTranslation && PronunciationState == ResultState.Pending:
                Pronunciation = pronunciation.Pronunciation;
                PronunciationState = ResultState.Ready;
                break;
            case SpeechSynthesisCompleted speech when HasTranslation && AudioState == ResultState.Pending:
                Audio = speech.Audio;
                AudioState = ResultState.Ready;
                break;
            case TranslationStageFailed error when HasTranslation && error.Stage == TranslationStage.Pronunciation
                && PronunciationState == ResultState.Pending:
                PronunciationState = ResultState.Failed;
                break;
            case TranslationStageFailed error when HasTranslation && error.Stage == TranslationStage.SpeechSynthesis
                && AudioState == ResultState.Pending:
                AudioState = ResultState.Failed;
                break;
            case TranslationStageFailed error when !HasTranslation && error.Stage == TranslationStage.TextTranslation:
                State = MessageState.Failed;
                FinishPending();
                break;
            default: return false;
        }
        OperationId ??= update.OperationId;
        return true;
    }

    public bool TryClaimAutoplay()
    {
        if (IsErased || State == MessageState.Canceled || Audio is null || AutoplayClaimed) return false;
        AutoplayClaimed = true;
        return true;
    }

    public void Complete()
    {
        if (IsErased || State is MessageState.Canceled or MessageState.Failed) return;
        State = HasTranslation ? MessageState.Ready : MessageState.Failed;
        FinishPending();
    }

    public void Cancel()
    {
        if (IsErased) return;
        State = MessageState.Canceled;
        Audio = null;
        AudioState = ResultState.Unavailable;
        FinishPending();
    }

    public void Fail()
    {
        if (IsErased) return;
        State = MessageState.Failed;
        FinishPending();
    }

    private void FinishPending()
    {
        if (PronunciationState == ResultState.Pending) PronunciationState = ResultState.Unavailable;
        if (AudioState == ResultState.Pending) AudioState = ResultState.Unavailable;
    }

    internal void Erase()
    {
        IsErased = true;
        SourceText = TranslatedText = string.Empty;
        Pronunciation = null;
        Audio = null;
        State = MessageState.Canceled;
        PronunciationState = AudioState = ResultState.Unavailable;
    }
}
