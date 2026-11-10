using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Saylo.Desktop.Services;
using Saylo.Desktop.Services.Audio;
using Saylo.Desktop.ViewModels;
using Saylo.Modules.Conversations;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Tests;

public sealed class ChatInteractionTests
{
    [Fact]
    public async Task AutoplayRunsOnceAndRepeatReusesAudioWithoutTranslatingAgain()
    {
        var service = new ChatService { DuplicateAudio = true };
        var playback = new PlaybackStub { Hold = true };
        await using var model = Create(service, playback);
        model.TextInput.Text = "Buenos días";
        await model.TranslateCommand.ExecuteAsync(null);
        var audio = await playback.NextStart();
        var message = Assert.Single(model.Messages);

        Assert.Equal(1, playback.Plays);
        Assert.True(message.IsPlaying);
        Assert.False(model.AudioInput.IsEnabled);
        await message.StopAudioCommand.ExecuteAsync(null);
        var repeat = message.RepeatCommand.ExecuteAsync(null);
        Assert.Same(audio, await playback.NextStart());
        await message.StopAudioCommand.ExecuteAsync(null);
        await repeat;

        Assert.Equal(2, playback.Plays);
        Assert.Equal(1, service.Calls);
        Assert.Equal(1, playback.MaxConcurrent);
        Assert.True(model.AudioInput.IsEnabled);
    }

    [Fact]
    public async Task ANewTranslationStopsThePreviousVoiceBeforeStartingAnother()
    {
        var playback = new PlaybackStub { Hold = true };
        await using var model = Create(new ChatService(), playback);
        model.TextInput.Text = "Primero";
        await model.TranslateCommand.ExecuteAsync(null);
        await playback.NextStart();
        var first = model.Messages[0];

        model.TextInput.Text = "Segundo";
        await model.TranslateCommand.ExecuteAsync(null);
        await playback.NextStart();

        Assert.False(first.IsPlaying);
        Assert.True(model.Messages[1].IsPlaying);
        Assert.Equal(1, playback.MaxConcurrent);
        Assert.Equal(1, playback.Active);
    }

    [Fact]
    public async Task PlaybackFailurePreservesTextAndCanRetryWithoutAnotherTranslation()
    {
        var playback = new PlaybackStub { FailNext = true };
        var service = new ChatService();
        await using var model = Create(service, playback);
        model.TextInput.Text = "Buenos días";
        await model.TranslateCommand.ExecuteAsync(null);
        var message = Assert.Single(model.Messages);

        Assert.Equal("Translated: Buenos días", message.TranslatedText);
        Assert.Contains("No se pudo reproducir", message.AudioStatus);
        Assert.True(message.RepeatCommand.CanExecute(null));
        await message.RepeatCommand.ExecuteAsync(null);

        Assert.Equal(2, playback.Plays);
        Assert.Equal(1, service.Calls);
        Assert.Contains("disponible para repetir", message.AudioStatus);
    }

    [Fact]
    public async Task CopyUsesOnlyTheDisplayedTranslationAndReportsRecoverableErrors()
    {
        var clipboard = new ClipboardStub { FailNext = true };
        await using var model = Create(new ChatService(), new PlaybackStub(), clipboard);
        model.TextInput.Text = "  José, pedido #12\n¿Está listo?  ";
        await model.TranslateCommand.ExecuteAsync(null);
        var message = Assert.Single(model.Messages);

        await message.CopyCommand.ExecuteAsync(null);
        Assert.Contains("No se pudo copiar", message.CopyStatus);
        await message.CopyCommand.ExecuteAsync(null);

        Assert.Equal(message.TranslatedText, clipboard.Text);
        Assert.Equal("Traducción copiada", message.CopyStatus);
        Assert.Equal(2, clipboard.Calls);
    }

    [Fact]
    public async Task RequestingAndDecliningClearDoesNotDeleteOrInterruptAudio()
    {
        var playback = new PlaybackStub { Hold = true };
        await using var model = Create(new ChatService(), playback);
        model.TextInput.Text = "Buenos días";
        await model.TranslateCommand.ExecuteAsync(null);
        await playback.NextStart();

        Assert.False(model.ConfirmClearCommand.CanExecute(null));
        model.RequestClearCommand.Execute(null);
        Assert.True(model.IsClearConfirmationOpen);
        Assert.Single(model.Messages);
        model.KeepConversationCommand.Execute(null);

        Assert.False(model.IsClearConfirmationOpen);
        Assert.Single(model.Messages);
        Assert.Equal(1, playback.Active);
    }

    [Fact]
    public async Task ConfirmedClearStopsAudioErasesRetainedContentAndPreservesDraft()
    {
        var playback = new PlaybackStub { Hold = true };
        await using var model = Create(new ChatService(), playback);
        model.TextInput.Text = "Buenos días";
        await model.TranslateCommand.ExecuteAsync(null);
        await playback.NextStart();
        var old = Assert.Single(model.Messages);
        model.TextInput.Text = "Un borrador nuevo";

        model.RequestClearCommand.Execute(null);
        await model.ConfirmClearCommand.ExecuteAsync(null);

        Assert.Empty(model.Messages);
        Assert.True(model.IsConversationEmpty);
        Assert.Equal(0, playback.Active);
        Assert.Empty(old.SourceText);
        Assert.Empty(old.TranslatedText);
        Assert.Null(old.Audio);
        Assert.False(old.CopyCommand.CanExecute(null));
        Assert.False(old.RepeatCommand.CanExecute(null));
        Assert.Equal("Un borrador nuevo", model.TextInput.Text);
        Assert.True(model.TranslateCommand.CanExecute(null));
    }

    [Fact]
    public async Task LateResultsAfterClearCannotRestoreMessagesOrAutoplay()
    {
        var service = new ChatService { GateAfterAudio = new(), IgnoreCancellationAtGate = true };
        var playback = new PlaybackStub { Hold = true };
        await using var model = Create(service, playback);
        model.TextInput.Text = "Buenos días";
        var translation = model.TranslateCommand.ExecuteAsync(null);
        await playback.NextStart();
        model.RequestClearCommand.Execute(null);

        var clear = model.ConfirmClearCommand.ExecuteAsync(null);
        Assert.Empty(model.Messages);
        service.GateAfterAudio.TrySetResult();
        await clear;
        await translation;

        Assert.Empty(model.Messages);
        Assert.Equal(1, playback.Plays);
        Assert.Equal(0, playback.Active);
    }

    [Fact]
    public async Task CancelingAnEnrichingRequestStopsItsVoiceAndKeepsConfirmedText()
    {
        var service = new ChatService { GateAfterAudio = new() };
        var playback = new PlaybackStub { Hold = true };
        await using var model = Create(service, playback);
        model.TextInput.Text = "Buenos días";
        var translation = model.TranslateCommand.ExecuteAsync(null);
        await playback.NextStart();

        model.TranslateCancelCommand.Execute(null);
        await translation;
        await playback.NextEnd();

        var message = Assert.Single(model.Messages);
        Assert.Equal("Translated: Buenos días", message.TranslatedText);
        Assert.Null(message.Audio);
        Assert.Equal("Operación cancelada", message.Status);
        Assert.False(message.RepeatCommand.CanExecute(null));
    }

    [Fact]
    public async Task ClosingClearsSessionAudioAndRetainedReferencesAndReopeningStartsEmpty()
    {
        var playback = new PlaybackStub { Hold = true };
        var model = Create(new ChatService(), playback);
        model.TextInput.Text = "Buenos días";
        await model.TranslateCommand.ExecuteAsync(null);
        await playback.NextStart();
        var old = Assert.Single(model.Messages);

        await model.DisposeAsync();

        Assert.Empty(model.Messages);
        Assert.Empty(old.TranslatedText);
        Assert.Null(old.Audio);
        Assert.Equal(0, playback.Active);
        Assert.True(playback.Disposed);
        await using var reopened = Create(new ChatService(), new PlaybackStub());
        Assert.Empty(reopened.Messages);
    }

    [Theory]
    [InlineData(false, MessageOrigin.ReviewedVoice)]
    [InlineData(true, MessageOrigin.Mixed)]
    public async Task ReviewedTranscriptionReachesTranslationWithTheCorrectOrigin(bool hasDraft, MessageOrigin expected)
    {
        var service = new ChatService();
        await using var model = new MainViewModel(new TestAudioAdapter(), new TestRecognizer(), service,
            playback: new PlaybackStub(), clipboard: new ClipboardStub());
        await model.InitializeAsync();
        if (hasDraft) model.TextInput.Text = "Mi borrador";
        await model.AudioInput.StartCommand.ExecuteAsync(null);
        await model.AudioInput.StopCommand.ExecuteAsync(null);
        model.AudioInput.Transcript = "Frase corregida";
        model.AudioInput.AcceptTranscriptCommand.Execute(null);
        var reviewedText = model.TextInput.Text;

        await model.TranslateCommand.ExecuteAsync(null);

        Assert.Equal(reviewedText, Assert.Single(model.Messages).SourceText);
        Assert.Equal(expected, model.Messages[0].Entry.Origin);
        Assert.Contains("Frase corregida", model.Messages[0].TranslatedText);
    }

    private static MainViewModel Create(ChatService service, PlaybackStub playback, ClipboardStub? clipboard = null) =>
        new(new TestAudioAdapter(), translation: service, playback: playback, clipboard: clipboard ?? new ClipboardStub());
}

internal sealed class ClipboardStub : IClipboardService
{
    public bool IsAvailable => true;
    internal string? Text;
    internal bool FailNext;
    internal int Calls;
    public Task SetTextAsync(string text)
    {
        Calls++;
        if (FailNext) { FailNext = false; throw new InvalidOperationException("private provider detail"); }
        Text = text;
        return Task.CompletedTask;
    }
}

internal sealed class PlaybackStub : IAudioPlaybackAdapter
{
    private readonly Channel<AudioData> _starts = Channel.CreateUnbounded<AudioData>();
    private readonly Channel<bool> _ends = Channel.CreateUnbounded<bool>();
    internal bool Hold, FailNext, Disposed;
    internal int Plays, Active, MaxConcurrent;
    public bool IsAvailable => !Disposed;
    public async Task PlayAsync(AudioData audio, CancellationToken cancellationToken)
    {
        Plays++;
        var active = Interlocked.Increment(ref Active);
        MaxConcurrent = Math.Max(active, MaxConcurrent);
        _starts.Writer.TryWrite(audio);
        try
        {
            if (FailNext) { FailNext = false; throw new InvalidOperationException("private output detail"); }
            if (Hold) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref Active);
            _ends.Writer.TryWrite(true);
        }
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    internal Task<AudioData> NextStart() => _starts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    internal Task<bool> NextEnd() => _ends.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
}

internal sealed class ChatService : ITranslationService
{
    internal bool DuplicateAudio, IgnoreCancellationAtGate;
    internal int Calls;
    internal TaskCompletionSource? GateAfterAudio;
    public async IAsyncEnumerable<TranslationUpdate> TranslateTextAsync(TextTranslationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls++;
        var id = Guid.NewGuid();
        yield return new TextTranslationCompleted(id, request.Text, "Translated: " + request.Text, request.SourceLanguage, request.TargetLanguage);
        var audio = new AudioData(new byte[320], AudioFormat.Pcm16, 16000, 1);
        yield return new SpeechSynthesisCompleted(id, audio);
        if (DuplicateAudio) yield return new SpeechSynthesisCompleted(id, audio);
        if (GateAfterAudio is not null)
        {
            if (IgnoreCancellationAtGate) await GateAfterAudio.Task;
            else await GateAfterAudio.Task.WaitAsync(cancellationToken);
        }
        yield return new PronunciationCompleted(id, new("Validated IPA fixture", PronunciationNotation.Ipa));
    }
    public IAsyncEnumerable<TranslationUpdate> TranslateVoiceAsync(VoiceTranslationRequest request,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
