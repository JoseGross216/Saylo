using Saylo.Desktop.ViewModels;
using Saylo.Modules.Conversations;
using Saylo.Modules.Translation;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Tests;

/// <summary>RF-026: actual desktop orchestration and translation pipeline, controlled local engines.</summary>
public sealed class EmployeeAttentionWorkflowTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AnEmployeeCanPrepareReadHearRepeatCopyAndClearAnAttention(bool fromVoice, bool englishSource)
    {
        var source = englishSource ? Language.English : Language.Spanish;
        var target = englishSource ? Language.Spanish : Language.English;
        var original = englishSource ? "I will review your request." : "Voy a revisar su solicitud.";
        var translated = englishSource ? "Voy a revisar su solicitud." : "I will review your request.";
        var engines = new AttentionEngines(original, translated);
        var capture = new TestAudioAdapter();
        var output = new PlaybackStub { Hold = true };
        var clipboard = new ClipboardStub();
        var service = new TranslationService(engines, engines, engines, engines);
        await using var model = new MainViewModel(capture, engines, service, playback: output, clipboard: clipboard);
        await model.InitializeAsync();
        if (englishSource) model.Languages.SwapCommand.Execute(null);

        if (fromVoice)
        {
            await model.AudioInput.StartCommand.ExecuteAsync(null);
            await model.AudioInput.StopCommand.ExecuteAsync(null);
            Assert.Equal(ApplicationState.ReviewingTranscript, model.State);
            Assert.Empty(model.Messages);
            Assert.Equal(0, engines.Translations);
            Assert.True(capture.Cancels > 0);
            Assert.Equal(source, engines.RecognizedLanguage);
            // Confirm an explicit correction instead of forwarding recognition automatically.
            Assert.NotEqual(original, model.AudioInput.Transcript);
            model.AudioInput.Transcript = original;
            model.AudioInput.AcceptTranscriptCommand.Execute(null);
        }
        else model.TextInput.Text = original;

        var translation = model.TranslateCommand.ExecuteAsync(null);
        try
        {
            await engines.SynthesisRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var message = Assert.Single(model.Messages);
            Assert.Equal(original, message.SourceText);
            Assert.Equal(translated, message.TranslatedText);
            Assert.Equal(target, engines.SynthesizedLanguage);
            Assert.Equal(fromVoice ? MessageOrigin.ReviewedVoice : MessageOrigin.Text, message.Entry.Origin);
            Assert.Equal(ApplicationState.PreparingResults, model.State);
            Assert.Equal(0, output.Plays);
            Assert.True(message.CopyCommand.CanExecute(null));
            Assert.False(message.RepeatCommand.CanExecute(null));

            // RF-026: no Play command is issued; the completed synthesis starts the first voice.
            engines.ReleaseSynthesis.TrySetResult();
            var firstAudio = await output.NextStart();
            Assert.Same(message.Audio, firstAudio);
            Assert.Equal(1, output.Plays);
            Assert.True(message.IsPlaying);
            Assert.Equal(translated, message.TranslatedText);
            Assert.Empty(message.PronunciationText);
            Assert.False(translation.IsCompleted);
            Assert.False(model.AudioInput.StartCommand.CanExecute(null));
            await model.StopPlaybackCommand.ExecuteAsync(null);

            engines.ReleasePronunciation.TrySetResult();
            await translation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ApplicationState.Ready, model.State);
            Assert.Equal("Guía aproximada de prueba", message.PronunciationText);
            Assert.Equal(1, output.Plays);
            await message.CopyCommand.ExecuteAsync(null);
            Assert.Equal(translated, clipboard.Text);

            var replay = message.RepeatCommand.ExecuteAsync(null);
            Assert.Same(firstAudio, await output.NextStart());
            await model.StopPlaybackCommand.ExecuteAsync(null);
            await replay.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, output.Plays);
            Assert.Equal(1, engines.Translations);
            Assert.Equal(1, engines.Syntheses);
            Assert.Equal(1, engines.Pronunciations);
            Assert.Equal(1, output.MaxConcurrent);

            model.TextInput.Text = "Borrador de la siguiente atención";
            model.RequestClearCommand.Execute(null);
            model.KeepConversationCommand.Execute(null);
            Assert.Single(model.Messages);
            model.RequestClearCommand.Execute(null);
            await model.ConfirmClearCommand.ExecuteAsync(null);
            Assert.Empty(model.Messages);
            Assert.Empty(message.SourceText);
            Assert.Empty(message.TranslatedText);
            Assert.Null(message.Audio);
            Assert.Equal("Borrador de la siguiente atención", model.TextInput.Text);
            await model.DisposeAsync();
            Assert.Empty(model.TextInput.Text!);
        }
        finally
        {
            engines.ReleaseSynthesis.TrySetResult();
            engines.ReleasePronunciation.TrySetResult();
            model.CancelActiveTranslationCommand.Execute(null);
            await translation.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await using var reopened = new MainViewModel(new TestAudioAdapter(), translation: service);
        Assert.Empty(reopened.Messages);
        Assert.Empty(reopened.TextInput.Text!);
    }

    private sealed class AttentionEngines(string original, string translated) :
        ISpeechRecognitionEngine, ITextTranslationEngine, ISpeechSynthesisEngine, IPronunciationEngine
    {
        internal int Translations, Syntheses, Pronunciations;
        internal Language? RecognizedLanguage, SynthesizedLanguage;
        internal TaskCompletionSource SynthesisRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSynthesis { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleasePronunciation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> TranscribeAsync(AudioData audio, Language sourceLanguage, CancellationToken cancellationToken = default)
        {
            RecognizedLanguage = sourceLanguage;
            return Task.FromResult(original + " [corregir]");
        }
        public Task<string> TranslateAsync(string text, Language sourceLanguage, Language targetLanguage,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(original, text);
            Translations++;
            return Task.FromResult(translated);
        }
        public async Task<AudioData> SynthesizeAsync(string translatedText, Language targetLanguage,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(translated, translatedText);
            Syntheses++;
            SynthesizedLanguage = targetLanguage;
            SynthesisRequested.TrySetResult();
            await ReleaseSynthesis.Task.WaitAsync(cancellationToken);
            return new(new byte[320], AudioFormat.Pcm16, 16000, 1);
        }
        public async Task<PronunciationResult> GenerateAsync(string translatedText, Language targetLanguage,
            CancellationToken cancellationToken = default)
        {
            Pronunciations++;
            await ReleasePronunciation.Task.WaitAsync(cancellationToken);
            return new("Guía aproximada de prueba", PronunciationNotation.Approximate);
        }
    }
}
