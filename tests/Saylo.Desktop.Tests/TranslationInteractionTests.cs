using System.Runtime.CompilerServices;
using Saylo.Desktop.ViewModels;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Tests;

public sealed class TranslationInteractionTests
{
    [Fact]
    public async Task TextIsVisibleBeforeOptionalStagesAndOptionalErrorsPreserveIt()
    {
        var service = new ProgressiveService();
        await using var model = new MainViewModel(new TestAudioAdapter(), translation: service);
        model.TextInput.Text = "  Buenos días  ";
        var run = model.TranslateCommand.ExecuteAsync(null);
        await service.TextDelivered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var result = Assert.Single(model.Messages);
        Assert.Equal("Good morning", result.TranslatedText);
        Assert.Equal("  Buenos días  ", service.Request!.Text);
        Assert.False(model.Languages.IsEnabled);
        Assert.False(model.AudioInput.StartCommand.CanExecute(null));
        model.TextInput.Text = "A new draft";
        service.Release.TrySetResult();
        await run;

        Assert.Equal("Good morning", result.TranslatedText);
        Assert.Equal("Pronunciación no disponible", result.PronunciationLabel);
        Assert.Equal("No se pudo generar la voz", result.AudioStatus);
        Assert.True(model.Languages.IsEnabled);
        Assert.Equal("A new draft", model.TextInput.Text);
    }

    [Fact]
    public async Task CancellationKeepsDeliveredTextAndPreventsLateUpdates()
    {
        var service = new ProgressiveService();
        await using var model = new MainViewModel(new TestAudioAdapter(), translation: service);
        model.TextInput.Text = "Buenos días";
        var run = model.TranslateCommand.ExecuteAsync(null);
        await service.TextDelivered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        model.TranslateCancelCommand.Execute(null);
        await run;
        service.Release.TrySetResult();

        var result = Assert.Single(model.Messages);
        Assert.Equal("Good morning", result.TranslatedText);
        Assert.Equal("Operación cancelada", result.Status);
        Assert.False(model.IsTranslating);
        Assert.True(model.TranslateCommand.CanExecute(null));
    }

    [Fact]
    public async Task ClosingClearsSessionAndCancelsPendingTranslation()
    {
        var service = new ProgressiveService();
        var model = new MainViewModel(new TestAudioAdapter(), translation: service);
        model.TextInput.Text = "Buenos días";
        var run = model.TranslateCommand.ExecuteAsync(null);
        await service.TextDelivered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await model.DisposeAsync();
        await run;

        Assert.Empty(model.Messages);
        Assert.False(model.TextInput.HasText);
        Assert.False(model.TranslateCommand.CanExecute(null));
    }

    private sealed class ProgressiveService : ITranslationService
    {
        internal TaskCompletionSource TextDelivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TextTranslationRequest? Request;
        public async IAsyncEnumerable<TranslationUpdate> TranslateTextAsync(TextTranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Request = request;
            var id = Guid.NewGuid();
            yield return new TextTranslationCompleted(id, request.Text, "Good morning", request.SourceLanguage, request.TargetLanguage);
            TextDelivered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            yield return new TranslationStageFailed(id, TranslationStage.Pronunciation, TranslationErrorCode.EngineFailure);
            yield return new TranslationStageFailed(id, TranslationStage.SpeechSynthesis, TranslationErrorCode.ModelUnavailable);
        }
        public IAsyncEnumerable<TranslationUpdate> TranslateVoiceAsync(VoiceTranslationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("Review transcription before translating.");
    }
}
