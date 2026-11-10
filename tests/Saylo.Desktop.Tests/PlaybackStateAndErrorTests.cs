using System.Runtime.CompilerServices;
using Saylo.Desktop.Services.Audio;
using Saylo.Desktop.Services.Translation;
using Saylo.Desktop.ViewModels;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Tests;

public sealed class PlaybackStateAndErrorTests
{
    [Fact]
    public async Task DemonstrationDoesNotTreatItsExpectedLackOfVoiceAsATranslationFailure()
    {
        await using var model = new MainViewModel(new TestAudioAdapter(),
            connection: DesktopTranslationFactory.Create(["--demo"]));
        model.TextInput.Text = "Buenos días.";
        await model.TranslateCommand.ExecuteAsync(null);
        Assert.Equal(ApplicationState.Ready, model.State);
        Assert.False(model.HasRecoverableError);
        var message = Assert.Single(model.Messages);
        Assert.Equal("Traducción de ejemplo disponible", message.Status);
        Assert.Equal("La demostración no genera audio.", message.AudioStatus);
    }

    [Fact]
    public async Task CaptureStatesAreVisibleAndReturnToReadyAfterReview()
    {
        var model = new MainViewModel(new TestAudioAdapter(), new TestRecognizer());
        var states = new List<ApplicationState>();
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.State)) states.Add(model.State);
        };
        await model.InitializeAsync();
        await model.AudioInput.StartCommand.ExecuteAsync(null);
        Assert.Equal(ApplicationState.Recording, model.State);
        await model.AudioInput.StopCommand.ExecuteAsync(null);
        Assert.Equal(ApplicationState.ReviewingTranscript, model.State);
        model.AudioInput.Transcript = "Transcripción corregida";
        model.AudioInput.AcceptTranscriptCommand.Execute(null);

        Assert.Equal("Transcripción corregida", model.TextInput.Text);
        Assert.Equal(ApplicationState.Ready, model.State);
        Assert.Contains(ApplicationState.LoadingDevices, states);
        Assert.Contains(ApplicationState.PreparingMicrophone, states);
        Assert.Contains(ApplicationState.StoppingMicrophone, states);
        Assert.Contains(ApplicationState.Transcribing, states);
        await model.DisposeAsync();
        Assert.Equal(ApplicationState.Closing, model.State);
    }

    [Fact]
    public async Task DisablingAutoplayStillAllowsManualRepeatAndOnlyAffectsFutureAudio()
    {
        var playback = new PlaybackStub { Hold = true };
        var service = new ChatService { DuplicateAudio = true };
        await using var model = new MainViewModel(new TestAudioAdapter(), translation: service, playback: playback);
        model.IsAutoplayEnabled = false;
        model.TextInput.Text = "Buenos días";
        await model.TranslateCommand.ExecuteAsync(null);
        var message = Assert.Single(model.Messages);

        Assert.Equal(0, playback.Plays);
        Assert.Equal(ApplicationState.Ready, model.State);
        Assert.False(model.StopPlaybackCommand.CanExecute(null));
        var repeat = message.RepeatCommand.ExecuteAsync(null);
        var audio = await playback.NextStart();
        Assert.Same(message.Audio, audio);
        Assert.Equal(ApplicationState.Playing, model.State);
        await model.StopPlaybackCommand.ExecuteAsync(null);
        await repeat;
        Assert.Contains("Audio detenido", message.AudioStatus);

        model.IsAutoplayEnabled = true;
        Assert.Equal(1, playback.Plays);
        model.TextInput.Text = "Segundo";
        await model.TranslateCommand.ExecuteAsync(null);
        await playback.NextStart();
        model.IsAutoplayEnabled = false;
        Assert.True(model.IsPlaying);
        Assert.Equal(2, playback.Plays);
        Assert.Equal(2, service.Calls);
        await model.StopPlaybackCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task StoppingBlocksCaptureAndReplayUntilTheOutputAdapterFinishes()
    {
        var playback = new ControlledPlayback();
        await using var model = new MainViewModel(new TestAudioAdapter(), translation: new ChatService(), playback: playback);
        await model.InitializeAsync();
        model.TextInput.Text = "Buenos días";
        await model.TranslateCommand.ExecuteAsync(null);
        await playback.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var message = Assert.Single(model.Messages);
        playback.HoldStop = true;
        var stop = model.StopPlaybackCommand.ExecuteAsync(null);
        await playback.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal(ApplicationState.StoppingPlayback, model.State);
            Assert.False(model.AudioInput.StartCommand.CanExecute(null));
            Assert.False(message.RepeatCommand.CanExecute(null));
            Assert.False(model.TranslateCommand.CanExecute(null));
        }
        finally { playback.ReleaseStop.TrySetResult(); }
        await stop;

        Assert.Equal(ApplicationState.Ready, model.State);
        Assert.True(model.AudioInput.StartCommand.CanExecute(null));
        Assert.True(message.RepeatCommand.CanExecute(null));
    }

    [Fact]
    public async Task StopFailureRemainsVisibleAndReplayRecoversWithoutTranslatingAgain()
    {
        var playback = new ControlledPlayback();
        var service = new ChatService();
        await using var model = new MainViewModel(new TestAudioAdapter(), translation: service, playback: playback);
        model.TextInput.Text = "Buenos días";
        await model.TranslateCommand.ExecuteAsync(null);
        await playback.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var message = Assert.Single(model.Messages);
        var audio = message.Audio;
        playback.FailStop = true;
        await model.StopPlaybackCommand.ExecuteAsync(null);

        Assert.Equal(ApplicationState.RecoverableError, model.State);
        Assert.Contains("No se pudo confirmar la detención", message.AudioStatus);
        Assert.DoesNotContain("private", message.AudioStatus);
        Assert.Equal("Translated: Buenos días", message.TranslatedText);
        Assert.Same(audio, message.Audio);
        Assert.True(model.ShowStopPlayback);
        Assert.Equal("Reintentar detención", model.StopPlaybackLabel);
        Assert.False(model.AudioInput.IsEnabled);

        playback.FailStop = false;
        var repeat = message.RepeatCommand.ExecuteAsync(null);
        Assert.Equal(ApplicationState.Playing, model.State);
        Assert.False(model.HasRecoverableError);
        await model.StopPlaybackCommand.ExecuteAsync(null);
        await repeat;
        Assert.Equal(2, playback.Plays);
        Assert.Equal(1, service.Calls);
        Assert.Equal(ApplicationState.Ready, model.State);
    }

    [Fact]
    public async Task RetryingStopRecoversCaptureEvenWithoutPlayingTheMessageAgain()
    {
        var playback = new ControlledPlayback();
        await using var model = new MainViewModel(new TestAudioAdapter(), translation: new ChatService(), playback: playback);
        await model.InitializeAsync();
        model.TextInput.Text = "Buenos días";
        await model.TranslateCommand.ExecuteAsync(null);
        await playback.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        playback.FailStop = true;
        await model.StopPlaybackCommand.ExecuteAsync(null);
        Assert.True(model.StopPlaybackCommand.CanExecute(null));
        Assert.False(model.AudioInput.StartCommand.CanExecute(null));

        playback.FailStop = false;
        await model.StopPlaybackCommand.ExecuteAsync(null);
        Assert.False(model.ShowStopPlayback);
        Assert.True(model.AudioInput.StartCommand.CanExecute(null));
        Assert.Equal(ApplicationState.Ready, model.State);
        Assert.Equal(1, playback.Plays);
    }

    [Fact]
    public async Task SuccessfulAdapterReturnAfterCancellationIsDisplayedAsStopped()
    {
        var playback = new ControlledPlayback { CompleteNormallyOnCancellation = true };
        await using var model = new MainViewModel(new TestAudioAdapter(), translation: new ChatService(), playback: playback);
        model.TextInput.Text = "Buenos días";
        await model.TranslateCommand.ExecuteAsync(null);
        await playback.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await model.StopPlaybackCommand.ExecuteAsync(null);

        Assert.Contains("Audio detenido", Assert.Single(model.Messages).AudioStatus);
        Assert.Equal(ApplicationState.Ready, model.State);
    }

    [Theory]
    [InlineData(TranslationErrorCode.ModelUnavailable, "Falta el modelo local de voz")]
    [InlineData(TranslationErrorCode.ModelInvalid, "modelo de voz no es válido")]
    [InlineData(TranslationErrorCode.RuntimeUnavailable, "motor de voz no pudo iniciarse")]
    [InlineData(TranslationErrorCode.InsufficientMemory, "memoria suficiente")]
    [InlineData(TranslationErrorCode.ResourceUnavailable, "recursos de voz")]
    [InlineData(TranslationErrorCode.EngineFailure, "No se pudo generar la voz")]
    public async Task SynthesisFailureKeepsTextCopyAndSessionUsable(TranslationErrorCode code, string expected)
    {
        var service = new OptionalFailureService(code);
        var clipboard = new ClipboardStub();
        await using var model = new MainViewModel(new TestAudioAdapter(), translation: service,
            playback: new PlaybackStub(), clipboard: clipboard);
        model.TextInput.Text = "Buenos días";
        await model.TranslateCommand.ExecuteAsync(null);
        var message = Assert.Single(model.Messages);

        Assert.Equal(ApplicationState.RecoverableError, model.State);
        Assert.Equal(code, message.Entry.AudioFailureCode);
        Assert.Contains(expected, message.AudioStatus);
        Assert.False(message.RepeatCommand.CanExecute(null));
        Assert.True(message.CopyCommand.CanExecute(null));
        await message.CopyCommand.ExecuteAsync(null);
        Assert.Equal("Good morning", clipboard.Text);
        Assert.True(model.TranslateCommand.CanExecute(null));

        model.RequestClearCommand.Execute(null);
        await model.ConfirmClearCommand.ExecuteAsync(null);
        Assert.Null(message.Entry.AudioFailureCode);
        Assert.Null(message.Entry.PronunciationFailureCode);
        Assert.Equal(ApplicationState.Ready, model.State);
        Assert.Empty(message.TranslatedText);
    }

    [Theory]
    [InlineData(TranslationErrorCode.ModelUnavailable, "Falta el modelo de transcripción")]
    [InlineData(TranslationErrorCode.ModelInvalid, "modelo de transcripción no es válido")]
    [InlineData(TranslationErrorCode.RuntimeUnavailable, "motor de transcripción no pudo iniciarse")]
    [InlineData(TranslationErrorCode.InsufficientMemory, "memoria suficiente")]
    [InlineData(TranslationErrorCode.ResourceUnavailable, "recursos de transcripción")]
    [InlineData(TranslationErrorCode.EngineFailure, "No se pudo transcribir")]
    public async Task TranscriptionFailureReleasesMicPreservesDraftAndAllowsAnotherRecording(TranslationErrorCode code, string expected)
    {
        var recognizer = new RecoverableRecognizer(code);
        var adapter = new TestAudioAdapter();
        await using var model = new MainViewModel(adapter, recognizer, new ChatService());
        model.TextInput.Text = "Borrador original";
        await model.InitializeAsync();
        await model.AudioInput.StartCommand.ExecuteAsync(null);
        await model.AudioInput.StopCommand.ExecuteAsync(null);

        Assert.Equal(ApplicationState.RecoverableError, model.State);
        Assert.Contains(expected, model.StatusMessage);
        Assert.DoesNotContain("private", model.StatusMessage);
        Assert.Equal("Borrador original", model.TextInput.Text);
        Assert.Empty(model.AudioInput.Transcript!);
        Assert.True(adapter.Cancels > 0);
        Assert.True(model.TranslateCommand.CanExecute(null));
        recognizer.Fail = false;
        await model.AudioInput.StartCommand.ExecuteAsync(null);
        Assert.False(model.HasRecoverableError);
        await model.AudioInput.StopCommand.ExecuteAsync(null);
        Assert.Equal(ApplicationState.ReviewingTranscript, model.State);
        Assert.Equal("Recovered transcript", model.AudioInput.Transcript);
    }

    [Fact]
    public async Task NoDeviceIsAnActionableErrorAndRefreshRecovers()
    {
        var adapter = new TestAudioAdapter { Devices = [] };
        await using var model = new MainViewModel(adapter);
        await model.InitializeAsync();
        Assert.Equal(ApplicationState.RecoverableError, model.State);
        Assert.Contains("Conecta uno", model.StatusMessage);
        adapter.Devices = [new("usb", "USB")];
        await model.AudioInput.RefreshDevicesCommand.ExecuteAsync(null);
        Assert.Equal(ApplicationState.Ready, model.State);
        Assert.True(model.AudioInput.StartCommand.CanExecute(null));
    }

    private sealed class OptionalFailureService(TranslationErrorCode code) : ITranslationService
    {
        public async IAsyncEnumerable<TranslationUpdate> TranslateTextAsync(TextTranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            var id = Guid.NewGuid();
            yield return new TextTranslationCompleted(id, request.Text, "Good morning", request.SourceLanguage, request.TargetLanguage);
            yield return new TranslationStageFailed(id, TranslationStage.SpeechSynthesis, code);
            yield return new TranslationStageFailed(id, TranslationStage.Pronunciation, TranslationErrorCode.EngineFailure);
        }
        public IAsyncEnumerable<TranslationUpdate> TranslateVoiceAsync(VoiceTranslationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecoverableRecognizer(TranslationErrorCode code) : ISpeechRecognitionEngine
    {
        internal bool Fail = true;
        public Task<string> TranscribeAsync(AudioData audio, Language sourceLanguage, CancellationToken cancellationToken = default) =>
            Fail ? Task.FromException<string>(new TranslationEngineException(TranslationStage.SpeechRecognition, code, "private provider detail")) :
                Task.FromResult("Recovered transcript");
    }

    private sealed class ControlledPlayback : IAudioPlaybackAdapter
    {
        internal bool HoldStop, FailStop, CompleteNormallyOnCancellation;
        internal int Plays;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAvailable => true;
        public async Task PlayAsync(AudioData audio, CancellationToken cancellationToken)
        {
            Plays++;
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (CompleteNormallyOnCancellation) { }
        }
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (FailStop) throw new InvalidOperationException("private output error");
            if (!HoldStop) return;
            StopEntered.TrySetResult();
            await ReleaseStop.Task.WaitAsync(cancellationToken);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
