using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Translation.Tests;

public sealed class TranslationServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static TextTranslationRequest TextRequest() => new("input fixture", Language.Spanish, Language.English);
    private static VoiceTranslationRequest VoiceRequest() =>
        new(ContractValidationTests.CreateAudio(), Language.Spanish, Language.English);

    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task TextTranslationSupportsBothDirectionsAndForwardsTheTarget(string sourceCode, string targetCode)
    {
        var source = Language.FromCode(sourceCode);
        var target = Language.FromCode(targetCode);
        var engines = new TestEngines
        {
            Translate = (text, from, to, _) =>
            {
                Assert.Equal("input fixture", text);
                Assert.Equal(source, from);
                Assert.Equal(target, to);
                return Task.FromResult("translated fixture");
            },
            Pronounce = (text, language, _) =>
            {
                Assert.Equal("translated fixture", text);
                Assert.Equal(target, language);
                return Task.FromResult(new PronunciationResult("phonetic fixture", PronunciationNotation.Approximate));
            },
            Synthesize = (text, language, _) =>
            {
                Assert.Equal("translated fixture", text);
                Assert.Equal(target, language);
                return Task.FromResult(ContractValidationTests.CreateAudio());
            }
        };

        var updates = await CollectAsync(engines.CreateService().TranslateTextAsync(new("input fixture", source, target)));
        var written = Assert.IsType<TextTranslationCompleted>(updates[0]);
        Assert.Equal("input fixture", written.SourceText);
        Assert.Equal("translated fixture", written.TranslatedText);
        Assert.Equal(source, written.SourceLanguage);
        Assert.Equal(target, written.TargetLanguage);
        Assert.Single(updates.OfType<PronunciationCompleted>());
        Assert.Single(updates.OfType<SpeechSynthesisCompleted>());
        Assert.Equal(3, updates.Count);
        Assert.DoesNotContain(engines.Calls, call => call.Stage == TranslationStage.SpeechRecognition);
        Assert.NotEqual(Guid.Empty, written.OperationId);
        Assert.All(updates, update => Assert.Equal(written.OperationId, update.OperationId));
    }

    [Fact]
    public async Task VoiceTranscribesBeforeTranslationAndReusesTheTextPipeline()
    {
        var request = VoiceRequest();
        var engines = new TestEngines
        {
            Recognize = (audio, language, _) =>
            {
                Assert.Same(request.Audio, audio);
                Assert.Equal(request.SourceLanguage, language);
                return Task.FromResult("recognized fixture");
            },
            Translate = (text, source, target, _) =>
            {
                Assert.Equal("recognized fixture", text);
                Assert.Equal(request.SourceLanguage, source);
                Assert.Equal(request.TargetLanguage, target);
                return Task.FromResult("translated fixture");
            }
        };
        var updates = await CollectAsync(engines.CreateService().TranslateVoiceAsync(request));
        Assert.Equal("recognized fixture", Assert.IsType<TranscriptionCompleted>(updates[0]).Text);
        Assert.Equal("recognized fixture", Assert.IsType<TextTranslationCompleted>(updates[1]).SourceText);
        Assert.Equal(4, updates.Count);
        Assert.All(updates, update => Assert.Equal(updates[0].OperationId, update.OperationId));
        Assert.Equal(new[] { TranslationStage.SpeechRecognition, TranslationStage.TextTranslation,
            TranslationStage.Pronunciation, TranslationStage.SpeechSynthesis }, engines.Calls.Select(call => call.Stage));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OptionalResultsAreIndependentAndTextArrivesBeforeEitherStarts(bool delaySpeech)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engines = new TestEngines();
        if (delaySpeech)
            engines.Synthesize = async (_, _, token) =>
            {
                await gate.Task.WaitAsync(token);
                return ContractValidationTests.CreateAudio();
            };
        else
            engines.Pronounce = async (_, _, token) =>
            {
                await gate.Task.WaitAsync(token);
                return new PronunciationResult("phonetic fixture", PronunciationNotation.Ipa);
            };

        await using var enumerator = engines.CreateService().TranslateTextAsync(TextRequest()).GetAsyncEnumerator();
        Assert.True(await NextAsync(enumerator));
        Assert.IsType<TextTranslationCompleted>(enumerator.Current);
        Assert.Single(engines.Calls);

        Assert.True(await NextAsync(enumerator));
        if (delaySpeech)
            Assert.IsType<PronunciationCompleted>(enumerator.Current);
        else
            Assert.IsType<SpeechSynthesisCompleted>(enumerator.Current);
        Assert.False(gate.Task.IsCompleted);

        gate.SetResult();
        Assert.True(await NextAsync(enumerator));
        if (delaySpeech)
            Assert.IsType<SpeechSynthesisCompleted>(enumerator.Current);
        else
            Assert.IsType<PronunciationCompleted>(enumerator.Current);
        Assert.False(await NextAsync(enumerator));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TranslationFailuresAreIdentifiedAndStopLaterStages(bool synchronousFailure)
    {
        var engines = new TestEngines
        {
            Translate = (_, _, _, _) => synchronousFailure
                ? throw new InvalidOperationException("provider failure")
                : Task.FromException<string>(new InvalidOperationException("provider failure"))
        };
        var failure = Assert.IsType<TranslationStageFailed>(
            Assert.Single(await CollectAsync(engines.CreateService().TranslateTextAsync(TextRequest()))));
        Assert.Equal(TranslationStage.TextTranslation, failure.Stage);
        Assert.Equal(TranslationErrorCode.EngineFailure, failure.Code);
        Assert.Single(engines.Calls);
    }

    [Fact]
    public async Task RecognitionFailureStopsTheVoicePipeline()
    {
        var engines = new TestEngines
        {
            Recognize = (_, _, _) => Task.FromException<string>(new InvalidOperationException())
        };
        var failure = Assert.IsType<TranslationStageFailed>(
            Assert.Single(await CollectAsync(engines.CreateService().TranslateVoiceAsync(VoiceRequest()))));
        Assert.Equal(TranslationStage.SpeechRecognition, failure.Stage);
        Assert.Equal(TranslationErrorCode.EngineFailure, failure.Code);
        Assert.Single(engines.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task InvalidTranslatedTextIsRejected(string? text)
    {
        var engines = new TestEngines { Translate = (_, _, _, _) => Task.FromResult(text!) };
        var failure = Assert.IsType<TranslationStageFailed>(
            Assert.Single(await CollectAsync(engines.CreateService().TranslateTextAsync(TextRequest()))));
        Assert.Equal(TranslationStage.TextTranslation, failure.Stage);
        Assert.Equal(TranslationErrorCode.InvalidEngineOutput, failure.Code);
        Assert.Single(engines.Calls);
    }

    [Fact]
    public async Task EmptyTranscriptionIsRejectedBeforeTranslation()
    {
        var engines = new TestEngines { Recognize = (_, _, _) => Task.FromResult("") };
        var failure = Assert.IsType<TranslationStageFailed>(
            Assert.Single(await CollectAsync(engines.CreateService().TranslateVoiceAsync(VoiceRequest()))));
        Assert.Equal(TranslationStage.SpeechRecognition, failure.Stage);
        Assert.Equal(TranslationErrorCode.InvalidEngineOutput, failure.Code);
        Assert.Single(engines.Calls);
    }

    [Theory]
    [InlineData(TranslationStage.Pronunciation)]
    [InlineData(TranslationStage.SpeechSynthesis)]
    public async Task OptionalStageFailurePreservesTextAndTheOtherResult(TranslationStage failedStage)
    {
        var engines = new TestEngines();
        if (failedStage == TranslationStage.Pronunciation)
            engines.Pronounce = (_, _, _) => Task.FromException<PronunciationResult>(new InvalidOperationException());
        else
            engines.Synthesize = (_, _, _) => Task.FromException<AudioData>(new InvalidOperationException());

        var updates = await CollectAsync(engines.CreateService().TranslateTextAsync(TextRequest()));
        Assert.Equal("translation fixture", Assert.IsType<TextTranslationCompleted>(updates[0]).TranslatedText);
        var failure = Assert.Single(updates.OfType<TranslationStageFailed>());
        Assert.Equal(failedStage, failure.Stage);
        Assert.Equal(TranslationErrorCode.EngineFailure, failure.Code);
        if (failedStage == TranslationStage.Pronunciation)
            Assert.Single(updates.OfType<SpeechSynthesisCompleted>());
        else
            Assert.Single(updates.OfType<PronunciationCompleted>());
        Assert.Equal(3, updates.Count);
    }

    [Fact]
    public async Task BothOptionalFailuresPreserveTheDeliveredText()
    {
        var engines = new TestEngines
        {
            Pronounce = (_, _, _) => throw new InvalidOperationException(),
            Synthesize = (_, _, _) => throw new InvalidOperationException()
        };
        var updates = await CollectAsync(engines.CreateService().TranslateTextAsync(TextRequest()));
        Assert.IsType<TextTranslationCompleted>(updates[0]);
        Assert.Equal(2, updates.OfType<TranslationStageFailed>().Count());
        Assert.Equal(3, updates.Count);
    }

    [Theory]
    [InlineData(TranslationStage.Pronunciation)]
    [InlineData(TranslationStage.SpeechSynthesis)]
    public async Task MissingOptionalOutputIsIdentified(TranslationStage stage)
    {
        var engines = new TestEngines();
        if (stage == TranslationStage.Pronunciation)
            engines.Pronounce = (_, _, _) => Task.FromResult<PronunciationResult>(null!);
        else
            engines.Synthesize = (_, _, _) => Task.FromResult<AudioData>(null!);
        var updates = await CollectAsync(engines.CreateService().TranslateTextAsync(TextRequest()));
        var failure = Assert.Single(updates.OfType<TranslationStageFailed>());
        Assert.Equal(stage, failure.Stage);
        Assert.Equal(TranslationErrorCode.InvalidEngineOutput, failure.Code);
        Assert.IsType<TextTranslationCompleted>(updates[0]);
    }

    [Fact]
    public async Task EveryEngineReceivesTheSameLinkedCancellationToken()
    {
        using var cancellation = new CancellationTokenSource();
        var engines = new TestEngines();
        await CollectAsync(engines.CreateService().TranslateVoiceAsync(VoiceRequest(), cancellation.Token));
        Assert.Equal(4, engines.Calls.Count);
        var token = engines.Calls.First().Token;
        Assert.True(token.CanBeCanceled);
        Assert.All(engines.Calls, call => Assert.Equal(token, call.Token));
        Assert.False(cancellation.IsCancellationRequested);
    }

    [Theory]
    [InlineData(TranslationStage.SpeechRecognition)]
    [InlineData(TranslationStage.TextTranslation)]
    [InlineData(TranslationStage.Pronunciation)]
    [InlineData(TranslationStage.SpeechSynthesis)]
    public async Task CallerCancellationInterruptsEveryStageAndDoesNotBecomeAnError(TranslationStage stage)
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<T> Block<T>(CancellationToken token)
        {
            Assert.True(token.CanBeCanceled);
            Assert.False(token.IsCancellationRequested);
            entered.SetResult();
            try
            {
                await never.Task.WaitAsync(token);
                throw new InvalidOperationException("Unreachable fixture branch.");
            }
            finally { stopped.SetResult(); }
        }

        var engines = new TestEngines();
        switch (stage)
        {
            case TranslationStage.SpeechRecognition:
                engines.Recognize = (_, _, token) => Block<string>(token);
                break;
            case TranslationStage.TextTranslation:
                engines.Translate = (_, _, _, token) => Block<string>(token);
                break;
            case TranslationStage.Pronunciation:
                engines.Pronounce = (_, _, token) => Block<PronunciationResult>(token);
                break;
            case TranslationStage.SpeechSynthesis:
                engines.Synthesize = (_, _, token) => Block<AudioData>(token);
                break;
        }
        var delivered = new List<TranslationUpdate>();
        var run = CollectAsync(engines.CreateService().TranslateVoiceAsync(VoiceRequest(), cancellation.Token), delivered);
        await entered.Task.WaitAsync(Timeout);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Timeout));
        Assert.True(stopped.Task.IsCompleted);
        Assert.Empty(delivered.OfType<TranslationStageFailed>());
        if (stage is TranslationStage.Pronunciation or TranslationStage.SpeechSynthesis)
            Assert.Single(delivered.OfType<TextTranslationCompleted>());
    }

    [Fact]
    public async Task EnumerationCancellationIsForwardedEvenWithoutAMethodToken()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engines = new TestEngines
        {
            Translate = (_, _, _, token) =>
            {
                entered.SetResult();
                return gate.Task.WaitAsync(token);
            }
        };
        await using var enumerator = engines.CreateService().TranslateTextAsync(TextRequest())
            .WithCancellation(cancellation.Token).GetAsyncEnumerator();
        async Task<bool> MoveNextAsync() => await enumerator.MoveNextAsync();
        var next = MoveNextAsync();
        await entered.Task.WaitAsync(Timeout);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next.WaitAsync(Timeout));
    }

    [Fact]
    public async Task PreCanceledOperationsDoNotCallEngines()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var engines = new TestEngines();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CollectAsync(
            engines.CreateService().TranslateTextAsync(TextRequest(), cancellation.Token)));
        Assert.Empty(engines.Calls);
    }

    [Fact]
    public async Task EarlyDisposalCancelsAndAwaitsPendingOptionalWork()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<PronunciationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engines = new TestEngines
        {
            Pronounce = async (_, _, token) =>
            {
                try { return await gate.Task.WaitAsync(token); }
                finally { stopped.SetResult(); }
            }
        };
        var enumerator = engines.CreateService().TranslateTextAsync(TextRequest()).GetAsyncEnumerator();
        try
        {
            Assert.True(await NextAsync(enumerator));
            Assert.IsType<TextTranslationCompleted>(enumerator.Current);
            Assert.True(await NextAsync(enumerator));
            Assert.IsType<SpeechSynthesisCompleted>(enumerator.Current);
            Assert.False(stopped.Task.IsCompleted);
        }
        finally { await enumerator.DisposeAsync().AsTask().WaitAsync(Timeout); }
        Assert.True(stopped.Task.IsCompleted);
    }

    [Fact]
    public async Task UnrequestedEngineCancellationIsAStageFailure()
    {
        var engines = new TestEngines
        {
            Translate = (_, _, _, _) => Task.FromException<string>(new OperationCanceledException())
        };
        var failure = Assert.IsType<TranslationStageFailed>(
            Assert.Single(await CollectAsync(engines.CreateService().TranslateTextAsync(TextRequest()))));
        Assert.Equal(TranslationStage.TextTranslation, failure.Stage);
        Assert.Equal(TranslationErrorCode.EngineFailure, failure.Code);
    }

    [Fact]
    public async Task EachOperationHasItsOwnIdentifier()
    {
        var service = new TestEngines().CreateService();
        var first = CollectAsync(service.TranslateTextAsync(TextRequest()));
        var second = CollectAsync(service.TranslateTextAsync(TextRequest()));
        await Task.WhenAll(first, second);
        Assert.NotEqual(first.Result[0].OperationId, second.Result[0].OperationId);
    }

    [Fact]
    public void MissingRequestsAndDependenciesAreRejected()
    {
        var engines = new TestEngines();
        var service = engines.CreateService();
        Assert.Throws<ArgumentNullException>(() => service.TranslateTextAsync(null!));
        Assert.Throws<ArgumentNullException>(() => service.TranslateVoiceAsync(null!));
        Assert.Throws<ArgumentNullException>(() => new TranslationService(null!, engines, engines, engines));
        Assert.Throws<ArgumentNullException>(() => new TranslationService(engines, null!, engines, engines));
        Assert.Throws<ArgumentNullException>(() => new TranslationService(engines, engines, null!, engines));
        Assert.Throws<ArgumentNullException>(() => new TranslationService(engines, engines, engines, null!));
    }

    private static Task<bool> NextAsync(IAsyncEnumerator<TranslationUpdate> enumerator) =>
        enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout);

    private static async Task<List<TranslationUpdate>> CollectAsync(IAsyncEnumerable<TranslationUpdate> stream,
        List<TranslationUpdate>? updates = null)
    {
        updates ??= new();
        await foreach (var update in stream)
            updates.Add(update);
        return updates;
    }
}
