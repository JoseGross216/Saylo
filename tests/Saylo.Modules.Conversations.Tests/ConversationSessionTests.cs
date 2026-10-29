using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Conversations.Tests;

public sealed class ConversationSessionTests
{
    private static TextTranslationRequest Request() => new("Buenos días", Language.Spanish, Language.English);
    private static AudioData Audio() => new(new byte[320], AudioFormat.Pcm16, 16000, 1);

    [Fact]
    public void OptionalResultsAreIndependentAndNeverEraseTranslatedText()
    {
        using var session = new ConversationSession();
        var message = session.Start(Request());
        var id = Guid.NewGuid();
        Assert.True(session.Apply(message, new TextTranslationCompleted(id, message.SourceText, "Good morning", Language.Spanish, Language.English)));
        Assert.Equal(MessageState.Enriching, message.State);
        Assert.True(session.Apply(message, new TranslationStageFailed(id, TranslationStage.Pronunciation, TranslationErrorCode.EngineFailure)));
        Assert.True(session.Apply(message, new SpeechSynthesisCompleted(id, Audio())));
        message.Complete();

        Assert.Equal("Good morning", message.TranslatedText);
        Assert.Equal(ResultState.Failed, message.PronunciationState);
        Assert.Equal(ResultState.Ready, message.AudioState);
        Assert.Equal(MessageState.Ready, message.State);
    }

    [Fact]
    public void DuplicateAudioCanOnlyClaimAutoplayOnce()
    {
        using var session = new ConversationSession();
        var message = session.Start(Request());
        var id = Guid.NewGuid();
        session.Apply(message, new TextTranslationCompleted(id, message.SourceText, "Good morning", Language.Spanish, Language.English));
        var audio = Audio();
        session.Apply(message, new SpeechSynthesisCompleted(id, audio));

        Assert.True(message.TryClaimAutoplay());
        Assert.False(session.Apply(message, new SpeechSynthesisCompleted(id, Audio())));
        Assert.False(message.TryClaimAutoplay());
        Assert.Same(audio, message.Audio);
    }

    [Fact]
    public void UpdatesFromAnotherOperationOrBeforeTextCannotContaminateTheMessage()
    {
        using var session = new ConversationSession();
        var message = session.Start(Request());
        Assert.False(session.Apply(message, new SpeechSynthesisCompleted(Guid.NewGuid(), Audio())));
        Assert.Null(message.OperationId);
        var id = Guid.NewGuid();
        session.Apply(message, new TextTranslationCompleted(id, message.SourceText, "Good morning", Language.Spanish, Language.English));

        Assert.False(session.Apply(message, new SpeechSynthesisCompleted(Guid.NewGuid(), Audio())));
        Assert.Null(message.Audio);
        Assert.Equal(id, message.OperationId);
    }

    [Fact]
    public void ClearingErasesRetainedReferencesAndRejectsLateResults()
    {
        using var session = new ConversationSession();
        var message = session.Start(Request());
        var id = Guid.NewGuid();
        session.Apply(message, new TextTranslationCompleted(id, message.SourceText, "Good morning", Language.Spanish, Language.English));
        session.Apply(message, new SpeechSynthesisCompleted(id, Audio()));

        session.Clear();

        Assert.Empty(session.Messages);
        Assert.Empty(message.SourceText);
        Assert.Empty(message.TranslatedText);
        Assert.Null(message.Audio);
        Assert.Null(message.Pronunciation);
        Assert.True(message.IsErased);
        Assert.False(message.TryClaimAutoplay());
        Assert.False(session.Apply(message, new SpeechSynthesisCompleted(id, Audio())));
        Assert.Empty(session.Messages);
        Assert.False(session.Start(Request()).IsErased);
    }

    [Fact]
    public void CancellationKeepsConfirmedTextButRejectsAudioAndLateEnrichment()
    {
        using var session = new ConversationSession();
        var message = session.Start(Request());
        var id = Guid.NewGuid();
        session.Apply(message, new TextTranslationCompleted(id, message.SourceText, "Good morning", Language.Spanish, Language.English));
        session.Apply(message, new SpeechSynthesisCompleted(id, Audio()));

        message.Cancel();

        Assert.Equal("Good morning", message.TranslatedText);
        Assert.Null(message.Audio);
        Assert.Equal(MessageState.Canceled, message.State);
        Assert.False(session.Apply(message, new PronunciationCompleted(id, new("IPA", PronunciationNotation.Ipa))));
    }

    [Fact]
    public void CompletedMessagesCannotReceiveLateResults()
    {
        using var session = new ConversationSession();
        var message = session.Start(Request());
        var id = Guid.NewGuid();
        session.Apply(message, new TextTranslationCompleted(id, message.SourceText, "Good morning", Language.Spanish, Language.English));
        message.Complete();

        Assert.Equal(ResultState.Unavailable, message.AudioState);
        Assert.False(session.Apply(message, new SpeechSynthesisCompleted(id, Audio())));
    }

    [Fact]
    public void TranslationFailureDoesNotProduceFictitiousResults()
    {
        using var session = new ConversationSession();
        var message = session.Start(Request());
        session.Apply(message, new TranslationStageFailed(Guid.NewGuid(), TranslationStage.TextTranslation, TranslationErrorCode.ModelUnavailable));
        message.Complete();

        Assert.Equal(MessageState.Failed, message.State);
        Assert.False(message.HasTranslation);
        Assert.Equal("Buenos días", message.SourceText);
        Assert.Null(message.Audio);
    }

    [Fact]
    public void SessionsAreIndependentAndDisposedSessionsCannotBeReused()
    {
        var first = new ConversationSession();
        var message = first.Start(Request(), MessageOrigin.ReviewedVoice);
        Assert.Equal(MessageOrigin.ReviewedVoice, message.Origin);
        first.Dispose();

        using var reopened = new ConversationSession();
        Assert.Empty(reopened.Messages);
        Assert.True(message.IsErased);
        Assert.Throws<ObjectDisposedException>(() => first.Start(Request()));
    }
}
