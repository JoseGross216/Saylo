using Saylo.Modules.ModelManagement;
using Saylo.Modules.ModelManagement.Contracts;
using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.SpeechRecognition;
using Whisper.net;

namespace Saylo.Modules.Translation.Tests;

public sealed class WhisperSpeechRecognitionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static AudioData Audio() => ContractValidationTests.CreateAudio();

    [Theory]
    [InlineData(8000, 1)]
    [InlineData(44100, 1)]
    [InlineData(16000, 2)]
    public async Task UnsupportedSampleRateOrChannelsNeverLoadTheModel(int rate, int channels)
    {
        var created = 0;
        await using var engine = new WhisperSpeechRecognitionEngine(new CatalogStub(), 2, (_, _) =>
        {
            created++;
            return new BackendStub();
        });
        var error = await Assert.ThrowsAsync<TranslationEngineException>(() =>
            engine.TranscribeAsync(new(new byte[8], AudioFormat.Pcm16, rate, channels), Language.Spanish));
        Assert.Equal(TranslationErrorCode.UnsupportedAudioFormat, error.Code);
        Assert.Equal(0, created);
    }

    [Fact]
    public void PcmConversionUsesSignedLittleEndianSamplesAndCorrectScaling()
    {
        byte[] bytes = [0, 128, 0, 192, 0, 0, 0, 64, 255, 127];
        var samples = Pcm16Audio.Convert(new(bytes, AudioFormat.Pcm16, 16000, 1), CancellationToken.None);
        Assert.Equal(new[] { -1f, -0.5f, 0f, 0.5f, 32767f / 32768f }, samples);
    }

    [Fact]
    public void WavHeadersAreNeverInterpretedAsPcmSamples()
    {
        var audio = new AudioData("RIFF1234WAVE"u8.ToArray(), AudioFormat.Pcm16, 16000, 1);
        Assert.Equal(TranslationErrorCode.UnsupportedAudioFormat,
            Assert.Throws<TranslationEngineException>(() => Pcm16Audio.Convert(audio, CancellationToken.None)).Code);
    }

    [Fact]
    public void ConversionRejectsPreCanceledWorkAndEmptyOrCorruptFrames()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Pcm16Audio.Convert(Audio(), cancellation.Token));
        Assert.Throws<ArgumentException>(() => new AudioData(Array.Empty<byte>(), AudioFormat.Pcm16, 16000, 1));
        Assert.Throws<ArgumentException>(() => new AudioData(new byte[3], AudioFormat.Pcm16, 16000, 1));
    }

    [Fact]
    public async Task MissingInputsAndUnsupportedLanguageCodesAreRejected()
    {
        await using var engine = new WhisperSpeechRecognitionEngine(new CatalogStub(), 2, (_, _) => new BackendStub());
        await Assert.ThrowsAsync<ArgumentNullException>(() => engine.TranscribeAsync(null!, Language.Spanish));
        await Assert.ThrowsAsync<ArgumentNullException>(() => engine.TranscribeAsync(Audio(), null!));
        Assert.Throws<ArgumentException>(() => Language.FromCode("fr"));
    }

    [Fact]
    public async Task ActualLocalCatalogReportsMissingModelWithoutLoadingNativeCode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SayloMissingModel", Guid.NewGuid().ToString("N"));
        await using var engine = new WhisperSpeechRecognitionEngine(new LocalModelCatalog([WhisperModels.BaseMultilingual(directory)]));
        var error = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.TranscribeAsync(Audio(), Language.English));
        Assert.Equal(TranslationErrorCode.ModelUnavailable, error.Code);
        Assert.Equal(TranslationStage.SpeechRecognition, error.Stage);
    }

    [Theory]
    [InlineData(ModelIntegrity.Invalid, ModelCompatibility.Unknown)]
    [InlineData(ModelIntegrity.Unknown, ModelCompatibility.Unknown)]
    [InlineData(ModelIntegrity.Valid, ModelCompatibility.Incompatible)]
    public async Task InvalidOrUnverifiedModelsNeverReachNativeCode(ModelIntegrity integrity, ModelCompatibility compatibility)
    {
        var catalog = new CatalogStub { Validation = new(ModelAvailability.Available, compatibility, integrity) };
        await using var engine = new WhisperSpeechRecognitionEngine(catalog, 2, (_, _) => throw new InvalidOperationException());
        var error = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.TranscribeAsync(Audio(), Language.Spanish));
        Assert.Equal(TranslationErrorCode.ModelInvalid, error.Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RuntimeAndModelLoadFailuresAreIdentified(bool badModel)
    {
        await using var engine = new WhisperSpeechRecognitionEngine(new CatalogStub(), 2, (_, _) =>
            throw (badModel ? new WhisperModelLoadException("fixture") : new DllNotFoundException("fixture")));
        var error = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.TranscribeAsync(Audio(), Language.Spanish));
        Assert.Equal(badModel ? TranslationErrorCode.ModelInvalid : TranslationErrorCode.RuntimeUnavailable, error.Code);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task LanguageAndSamplesAreForwardedWithCancellation(string code)
    {
        var backend = new BackendStub { Recognize = (samples, language, token) =>
        {
            Assert.Equal(code, language);
            Assert.Equal(2, samples.Length);
            Assert.True(token.CanBeCanceled);
            return Task.FromResult("recognized fixture");
        } };
        await using var engine = new WhisperSpeechRecognitionEngine(new CatalogStub(), 2, (_, _) => backend);
        Assert.Equal("recognized fixture", await engine.TranscribeAsync(Audio(), Language.FromCode(code)));
    }

    [Fact]
    public async Task RepeatedRequestsReuseOneModelAndNeverRunNativeWorkConcurrently()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var created = 0;
        var catalog = new CatalogStub();
        var backend = new BackendStub { Recognize = async (_, _, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
            }
            return "recognized fixture";
        } };
        await using var engine = new WhisperSpeechRecognitionEngine(catalog, 2, (_, _) =>
        {
            Interlocked.Increment(ref created);
            return backend;
        });
        var first = engine.TranscribeAsync(Audio(), Language.Spanish);
        await entered.Task.WaitAsync(Timeout);
        var second = engine.TranscribeAsync(Audio(), Language.English);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, calls);
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Timeout);
        Assert.Equal(2, calls);
        Assert.Equal(1, created);
        Assert.Equal(1, catalog.ValidationCalls);
    }

    [Fact]
    public async Task CancelingAQueuedRequestDoesNotInterruptTheActiveRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new BackendStub { Recognize = (_, _, token) =>
        {
            entered.SetResult();
            return release.Task.WaitAsync(token);
        } };
        await using var engine = new WhisperSpeechRecognitionEngine(new CatalogStub(), 2, (_, _) => backend);
        var first = engine.TranscribeAsync(Audio(), Language.Spanish);
        await entered.Task.WaitAsync(Timeout);
        using var cancellation = new CancellationTokenSource();
        var second = engine.TranscribeAsync(Audio(), Language.English, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(Timeout));
        Assert.False(first.IsCompleted);
        release.SetResult("recognized fixture");
        Assert.Equal("recognized fixture", await first.WaitAsync(Timeout));
    }

    [Fact]
    public async Task CancellationReachesNativeWorkAndTheEngineCanBeReusedAfterwards()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new BackendStub { Recognize = (_, _, token) =>
        {
            entered.SetResult();
            return never.Task.WaitAsync(token);
        } };
        await using var engine = new WhisperSpeechRecognitionEngine(new CatalogStub(), 2, (_, _) => backend);
        using var cancellation = new CancellationTokenSource();
        var run = engine.TranscribeAsync(Audio(), Language.Spanish, cancellation.Token);
        await entered.Task.WaitAsync(Timeout);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Timeout));
        backend.Recognize = (_, _, _) => Task.FromResult("next fixture");
        Assert.Equal("next fixture", await engine.TranscribeAsync(Audio(), Language.English));
    }

    [Fact]
    public async Task PreCanceledRequestsDoNotLoadTheModel()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var catalog = new CatalogStub();
        await using var engine = new WhisperSpeechRecognitionEngine(catalog, 2, (_, _) => throw new InvalidOperationException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.TranscribeAsync(Audio(), Language.English, cancellation.Token));
        Assert.Equal(0, catalog.ValidationCalls);
    }

    [Fact]
    public async Task DisposalCancelsInFlightWorkDisposesOnceAndRejectsNewRequests()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new BackendStub { Recognize = (_, _, token) =>
        {
            entered.SetResult();
            return never.Task.WaitAsync(token);
        } };
        var engine = new WhisperSpeechRecognitionEngine(new CatalogStub(), 2, (_, _) => backend);
        var run = engine.TranscribeAsync(Audio(), Language.Spanish);
        await entered.Task.WaitAsync(Timeout);
        await Task.WhenAll(engine.DisposeAsync().AsTask(), engine.DisposeAsync().AsTask()).WaitAsync(Timeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, backend.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.TranscribeAsync(Audio(), Language.English));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecognitionFailuresAndEmptyOutputAreNotFictitiousTranscriptions(bool throws)
    {
        var backend = new BackendStub { Recognize = (_, _, _) => throws
            ? throw new InvalidOperationException("fixture") : Task.FromResult(" ") };
        await using var engine = new WhisperSpeechRecognitionEngine(new CatalogStub(), 2, (_, _) => backend);
        var error = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.TranscribeAsync(Audio(), Language.Spanish));
        Assert.Equal(throws ? TranslationErrorCode.EngineFailure : TranslationErrorCode.InvalidEngineOutput, error.Code);
    }

    [Fact]
    public async Task TranslationServicePreservesWhisperDiagnosticCodes()
    {
        await using var engine = new WhisperSpeechRecognitionEngine(new CatalogStub { Missing = true }, 2, (_, _) => new BackendStub());
        var otherEngines = new TestEngines();
        var service = new TranslationService(engine, otherEngines, otherEngines, otherEngines);
        var updates = new List<TranslationUpdate>();
        await foreach (var update in service.TranslateVoiceAsync(new(Audio(), Language.Spanish, Language.English)))
            updates.Add(update);
        var failure = Assert.IsType<TranslationStageFailed>(Assert.Single(updates));
        Assert.Equal(TranslationStage.SpeechRecognition, failure.Stage);
        Assert.Equal(TranslationErrorCode.ModelUnavailable, failure.Code);
        Assert.Empty(otherEngines.Calls);
    }

    [Fact]
    public async Task DisposalDuringModelLoadWaitsAndReleasesTheCreatedModel()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(false);
        var backend = new BackendStub();
        var engine = new WhisperSpeechRecognitionEngine(new CatalogStub(), 2, (_, _) =>
        {
            entered.SetResult();
            if (!release.Wait(Timeout))
                throw new TimeoutException("Model-load fixture was not released.");
            return backend;
        });
        var run = engine.TranscribeAsync(Audio(), Language.Spanish);
        await entered.Task.WaitAsync(Timeout);
        var disposal = engine.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        release.Set();
        await disposal.WaitAsync(Timeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, backend.Disposals);
    }

    [Fact]
    public async Task RecognizedTextFeedsTheExistingProgressivePipeline()
    {
        await using var engine = new WhisperSpeechRecognitionEngine(new CatalogStub(), 2, (_, _) => new BackendStub());
        var others = new TestEngines { Translate = (text, _, _, _) => Task.FromResult(text + " translated fixture") };
        var service = new TranslationService(engine, others, others, others);
        var updates = new List<TranslationUpdate>();
        await foreach (var update in service.TranslateVoiceAsync(new(Audio(), Language.Spanish, Language.English)))
            updates.Add(update);
        Assert.Equal("recognized fixture", Assert.IsType<TranscriptionCompleted>(updates[0]).Text);
        Assert.Equal("recognized fixture translated fixture", Assert.IsType<TextTranslationCompleted>(updates[1]).TranslatedText);
        Assert.Equal(4, updates.Count);
    }

    private sealed class BackendStub : IWhisperBackend
    {
        internal Func<ReadOnlyMemory<float>, string, CancellationToken, Task<string>> Recognize { get; set; } =
            (_, _, _) => Task.FromResult("recognized fixture");
        internal int Disposals { get; private set; }
        public Task<string> TranscribeAsync(ReadOnlyMemory<float> samples, string language, CancellationToken token) =>
            Recognize(samples, language, token);
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class CatalogStub : IModelCatalog
    {
        internal bool Missing { get; init; }
        internal int ValidationCalls { get; private set; }
        internal ModelValidationResult Validation { get; init; } =
            new(ModelAvailability.Available, ModelCompatibility.Unknown, ModelIntegrity.Valid);
        public Task<IReadOnlyList<ModelDescriptor>> GetInstalledModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public Task<ModelDescriptor?> LocateAsync(string modelId, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult(Missing ? null : WhisperModels.BaseMultilingual(Path.GetTempPath()));
        public Task<ModelValidationResult> ValidateAsync(ModelDescriptor model, CancellationToken cancellationToken = default)
        {
            ValidationCalls++;
            return Task.FromResult(Validation);
        }
    }
}
