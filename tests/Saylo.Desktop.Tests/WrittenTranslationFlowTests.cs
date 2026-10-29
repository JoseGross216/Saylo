using System.Runtime.CompilerServices;
using Saylo.Desktop.Services.Translation;
using Saylo.Desktop.ViewModels;
using Saylo.Modules.Conversations;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Tests;

public sealed class WrittenTranslationFlowTests
{
    public static IEnumerable<object[]> Examples()
    {
        var connection = DesktopTranslationFactory.CreateDemonstration();
        return connection.Examples.Select(example => new object[] { example });
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task DemonstrationPresentsTheOriginalTranslationAndMatchingTargetGuide(TranslationExample example)
    {
        await using var model = Create(DesktopTranslationFactory.Create(new[] { "--demo" }, _ => null));
        model.Languages.SourceLanguage = model.Languages.AvailableLanguages.Single(option => option.Language == example.SourceLanguage);
        model.TextInput.Text = "  " + example.SourceText + "  ";

        await model.TranslateCommand.ExecuteAsync(null);

        var message = Assert.Single(model.Messages);
        Assert.Equal(model.TextInput.Text, message.SourceText);
        Assert.Equal(example.TranslatedText, message.TranslatedText);
        Assert.Equal(example.Pronunciation, message.PronunciationText);
        Assert.Equal(example.TargetLanguage, message.Entry.TargetLanguage);
        Assert.Contains(example.TargetLanguage == Language.Spanish ? "Español" : "Inglés", message.TranslationLabel);
        Assert.Contains("de ejemplo", message.PronunciationLabel);
        Assert.Equal(MessageState.Ready, message.Entry.State);
        Assert.True(model.IsDemonstration);
        Assert.Null(message.Audio);
    }

    [Fact]
    public async Task DemoDoesNotInventResultsForAnUnknownPhrase()
    {
        await using var model = Create(DesktopTranslationFactory.CreateDemonstration());
        model.TextInput.Text = "Una frase fuera del catálogo de prueba.";

        await model.TranslateCommand.ExecuteAsync(null);

        var message = Assert.Single(model.Messages);
        Assert.Equal(MessageState.Failed, message.Entry.State);
        Assert.False(message.HasTranslation);
        Assert.Empty(message.PronunciationText);
        Assert.Contains("ejemplos de demostración", message.Status);
        Assert.False(message.ShowRetry);
        Assert.Equal(model.TextInput.Text, message.SourceText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public async Task EmptyInputIsRejectedWithoutCreatingAMessage(string text)
    {
        await using var model = Create(DesktopTranslationFactory.CreateDemonstration());
        model.TextInput.Text = text;

        Assert.False(model.TranslateCommand.CanExecute(null));
        await model.TranslateCommand.ExecuteAsync(null);
        Assert.Empty(model.Messages);
        Assert.False(model.TextInput.IsValid);
    }

    [Fact]
    public async Task BackendLengthLimitIsVisibleWithoutTruncatingTheDraft()
    {
        await using var model = Create(DesktopTranslationFactory.CreateDemonstration());
        model.TextInput.Text = new string('a', 1001);

        Assert.False(model.TextInput.IsValid);
        Assert.True(model.TextInput.HasValidationError);
        Assert.Contains("1000", model.TextInput.ValidationMessage);
        Assert.Equal("1001 / 1000 caracteres", model.TextInput.CharacterCountLabel);
        Assert.False(model.TranslateCommand.CanExecute(null));
        Assert.Throws<ArgumentException>(() => model.TextInput.CreateRequest(Language.Spanish, Language.English));
        Assert.Equal(1001, model.TextInput.Text.Length);
        model.TextInput.Text = new string('a', 1000);
        Assert.True(model.TranslateCommand.CanExecute(null));
        model.TextInput.Text = new string('a', 500);
        Assert.True(model.TranslateCommand.CanExecute(null));
    }

    [Fact]
    public async Task SwappingLanguagesUpdatesExamplesAndPlaceholderAndPreservesDraft()
    {
        await using var model = Create(DesktopTranslationFactory.CreateDemonstration());
        model.TextInput.Text = "Mi borrador";
        model.Languages.SwapCommand.Execute(null);

        Assert.Equal("Mi borrador", model.TextInput.Text);
        Assert.Contains("English", model.TextInput.Placeholder);
        Assert.All(model.AvailableExamples, example => Assert.Equal(Language.English, example.SourceLanguage));
        Assert.Equal(Language.English, model.SelectedExample!.SourceLanguage);
        model.LoadExampleCommand.Execute(null);
        Assert.Equal(model.SelectedExample.SourceText, model.TextInput.Text);
        Assert.Empty(model.Messages);
    }

    [Theory]
    [InlineData(TranslationErrorCode.ModelUnavailable, "Faltan modelos")]
    [InlineData(TranslationErrorCode.ModelInvalid, "no es válido")]
    [InlineData(TranslationErrorCode.RuntimeUnavailable, "no pudo iniciarse")]
    [InlineData(TranslationErrorCode.InsufficientMemory, "memoria suficiente")]
    [InlineData(TranslationErrorCode.ResourceUnavailable, "recurso local")]
    public async Task TypedResourceErrorsAreRecoverableAndNeverLoseTheOriginal(TranslationErrorCode code, string expected)
    {
        var service = new RetryService { FirstError = code };
        await using var model = Create(Connection(service));
        model.TextInput.Text = "  Original confidencial  ";
        await model.TranslateCommand.ExecuteAsync(null);
        var first = Assert.Single(model.Messages);

        Assert.Contains(expected, first.Status);
        Assert.Equal(code, first.Entry.FailureCode);
        Assert.Equal(model.TextInput.Text, first.SourceText);
        Assert.False(first.HasTranslation);
        Assert.True(first.RetryTranslationCommand.CanExecute(null));
        Assert.True(model.Languages.IsEnabled);
    }

    [Fact]
    public async Task RetryUsesTheFailedMessagesLanguagesAndTextNotTheNewDraft()
    {
        var service = new RetryService();
        await using var model = Create(Connection(service));
        model.TextInput.Text = "Original español";
        await model.TranslateCommand.ExecuteAsync(null);
        var first = Assert.Single(model.Messages);
        model.Languages.SwapCommand.Execute(null);
        model.TextInput.Text = "A separate English draft";

        await first.RetryTranslationCommand.ExecuteAsync(null);

        Assert.Equal(2, model.Messages.Count);
        Assert.Equal("Original español", service.Requests[1].Text);
        Assert.Equal(Language.Spanish, service.Requests[1].SourceLanguage);
        Assert.Equal(Language.English, service.Requests[1].TargetLanguage);
        Assert.Equal("Translated original", model.Messages[1].TranslatedText);
        Assert.Equal("A separate English draft", model.TextInput.Text);
        Assert.Equal(Language.English, model.Languages.SourceLanguage.Language);
    }

    [Fact]
    public async Task GlobalCancelAlsoCancelsAnActiveRetry()
    {
        var service = new RetryService { HoldRetry = true };
        await using var model = Create(Connection(service));
        model.TextInput.Text = "Original";
        await model.TranslateCommand.ExecuteAsync(null);
        var retry = model.Messages[0].RetryTranslationCommand.ExecuteAsync(null);
        await service.RetryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(model.CancelActiveTranslationCommand.CanExecute(null));
        Assert.False(model.Languages.IsEnabled);
        model.CancelActiveTranslationCommand.Execute(null);
        await retry;

        Assert.Equal(MessageState.Canceled, model.Messages[1].Entry.State);
        Assert.Equal("Original", model.TextInput.Text);
        Assert.False(model.IsTranslating);
        Assert.True(model.Languages.IsEnabled);
    }

    [Fact]
    public async Task ClearingCancelsRetryAndInvalidatesItsMessages()
    {
        var service = new RetryService { HoldRetry = true };
        await using var model = Create(Connection(service));
        model.TextInput.Text = "Original";
        await model.TranslateCommand.ExecuteAsync(null);
        var first = model.Messages[0];
        var retry = first.RetryTranslationCommand.ExecuteAsync(null);
        await service.RetryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        model.RequestClearCommand.Execute(null);
        await model.ConfirmClearCommand.ExecuteAsync(null);
        await retry;

        Assert.Empty(model.Messages);
        Assert.Empty(first.SourceText);
        Assert.Null(first.Entry.FailureCode);
        Assert.False(first.RetryTranslationCommand.CanExecute(null));
        Assert.Equal("Original", model.TextInput.Text);
    }

    [Fact]
    public async Task ClosingWaitsForTheActiveRequestBeforeDisposingItsOwnedBackend()
    {
        var service = new RetryService { HoldRetry = true };
        var owned = new OwnedBackend(service);
        var model = Create(new DesktopTranslationConnection(service, DesktopTranslationMode.Local, "Local", "Test", ownedResource: owned));
        model.TextInput.Text = "Original";
        await model.TranslateCommand.ExecuteAsync(null);
        var retry = model.Messages[0].RetryTranslationCommand.ExecuteAsync(null);
        await service.RetryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await model.DisposeAsync();
        await retry;
        await model.DisposeAsync();

        Assert.Equal(1, owned.Disposals);
        Assert.True(owned.RequestWasFinished);
        Assert.Empty(model.Messages);
    }

    [Fact]
    public async Task RealConnectionWithMissingModelsReportsItWithoutFallingBackToDemo()
    {
        var absent = Path.Combine(Path.GetTempPath(), "Saylo-absent-" + Guid.NewGuid().ToString("N"));
        var connection = DesktopTranslationFactory.Create(Array.Empty<string>(), key => key switch
        {
            "SAYLO_ARGOS_PYTHON" => Path.Combine(absent, "python.exe"),
            "SAYLO_ARGOS_MODELS" => Path.Combine(absent, "models"),
            _ => null
        });
        await using var model = Create(connection);
        Assert.NotNull(connection.Service);
        model.TextInput.Text = "Buenos días.";

        await model.TranslateCommand.ExecuteAsync(null);

        var message = Assert.Single(model.Messages);
        Assert.Equal(DesktopTranslationMode.Local, connection.Mode);
        Assert.False(model.IsDemonstration);
        Assert.False(message.HasTranslation);
        Assert.Equal(TranslationErrorCode.ModelUnavailable, message.Entry.FailureCode);
        Assert.Contains("Faltan modelos", message.Status);
        Assert.DoesNotContain(absent, message.Status);
    }

    [Fact]
    public async Task MissingOrInvalidConfigurationNeverSilentlyStartsDemonstration()
    {
        await using var absent = DesktopTranslationFactory.Create(Array.Empty<string>(), _ => null);
        await using var relative = DesktopTranslationFactory.Create(Array.Empty<string>(), key => key == "SAYLO_ARGOS_PYTHON" ? "python.exe" : "models");
        await using var explicitDemo = DesktopTranslationFactory.Create(new[] { "--demo" }, _ => "invalid");

        Assert.Null(absent.Service);
        Assert.Equal(DesktopTranslationMode.Unavailable, absent.Mode);
        Assert.Null(relative.Service);
        Assert.Equal(DesktopTranslationMode.Demonstration, explicitDemo.Mode);
        Assert.NotNull(explicitDemo.Service);
    }

    [Fact]
    public void LocalWorkerIsCopiedWithTheDesktopDependencies()
    {
        var directory = Path.GetDirectoryName(typeof(MainViewModel).Assembly.Location)!;
        Assert.True(File.Exists(Path.Combine(directory, "TextTranslation", "argos_worker.py")));
    }

    private static MainViewModel Create(DesktopTranslationConnection connection) =>
        new(new TestAudioAdapter(), clipboard: new ClipboardStub(), connection: connection);
    private static DesktopTranslationConnection Connection(ITranslationService service) =>
        new(service, DesktopTranslationMode.Local, "Local", "Local test connection");

    private sealed class RetryService : ITranslationService
    {
        internal readonly List<TextTranslationRequest> Requests = new();
        internal TranslationErrorCode FirstError = TranslationErrorCode.ModelUnavailable;
        internal bool HoldRetry, RequestFinished;
        internal TaskCompletionSource RetryEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<TranslationUpdate> TranslateTextAsync(TextTranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var id = Guid.NewGuid();
            if (Requests.Count == 1)
            {
                yield return new TranslationStageFailed(id, TranslationStage.TextTranslation, FirstError);
                yield break;
            }
            try
            {
                RetryEntered.TrySetResult();
                if (HoldRetry) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                yield return new TextTranslationCompleted(id, request.Text, "Translated original", request.SourceLanguage, request.TargetLanguage);
                yield return new PronunciationCompleted(id, new("Validated guide", PronunciationNotation.Approximate));
            }
            finally { RequestFinished = true; }
        }
        public IAsyncEnumerable<TranslationUpdate> TranslateVoiceAsync(VoiceTranslationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class OwnedBackend(RetryService service) : IAsyncDisposable
    {
        internal int Disposals;
        internal bool RequestWasFinished;
        public ValueTask DisposeAsync()
        {
            Disposals++;
            RequestWasFinished = service.RequestFinished;
            return ValueTask.CompletedTask;
        }
    }
}
