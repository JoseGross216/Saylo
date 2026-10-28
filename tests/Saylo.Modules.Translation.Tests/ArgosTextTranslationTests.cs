using System.Text.Json;
using Saylo.Modules.ModelManagement.Contracts;
using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.TextTranslation;

namespace Saylo.Modules.Translation.Tests;

public sealed class ArgosTextTranslationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static ArgosTranslationOptions Options(TimeSpan? startup = null, TimeSpan? translation = null) =>
        new(Path.Combine(Path.GetTempPath(), "saylo-python.exe"), Path.GetTempPath(),
            startupTimeout: startup, translationTimeout: translation);

    private static ArgosTextTranslationEngine Engine(SessionStub session, CatalogStub? catalog = null,
        ArgosTranslationOptions? options = null) => new(options ?? Options(), catalog ?? new(), (_, _) => Task.FromResult<IArgosSession>(session));

    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task TranslatesBothDirectionsAndCorrelatesRequests(string source, string target)
    {
        var session = new SessionStub { Translate = (id, text, from, to, token) =>
        {
            Assert.True(Guid.TryParse(id, out _));
            Assert.Equal("input fixture", text);
            Assert.Equal(source, from);
            Assert.Equal(target, to);
            Assert.True(token.CanBeCanceled);
            return Task.FromResult("translated fixture");
        } };
        await using var engine = Engine(session);
        Assert.Equal("translated fixture", await engine.TranslateAsync("input fixture", Language.FromCode(source), Language.FromCode(target)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task EmptyTextDoesNotReachTheWorker(string? text)
    {
        var session = new SessionStub();
        await using var engine = Engine(session);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => engine.TranslateAsync(text!, Language.Spanish, Language.English));
        Assert.Equal(0, session.Calls);
    }

    [Fact]
    public async Task LongInputAndMissingLanguagesAreRejected()
    {
        await using var engine = Engine(new());
        await Assert.ThrowsAsync<ArgumentException>(() => engine.TranslateAsync(new string('a', 1001), Language.Spanish, Language.English));
        await Assert.ThrowsAsync<ArgumentNullException>(() => engine.TranslateAsync("fixture", null!, Language.English));
        await Assert.ThrowsAsync<ArgumentNullException>(() => engine.TranslateAsync("fixture", Language.Spanish, null!));
        Assert.Throws<ArgumentException>(() => Language.FromCode("fr"));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task EqualLanguagesAreRejected(string code)
    {
        await using var engine = Engine(new());
        await Assert.ThrowsAsync<ArgumentException>(() => engine.TranslateAsync("fixture", Language.FromCode(code), Language.FromCode(code)));
    }

    [Fact]
    public void OptionsRequireExplicitPythonAndPositiveTimeouts()
    {
        Assert.Throws<ArgumentException>(() => new ArgosTranslationOptions("python", Path.GetTempPath()));
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(startup: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArgosTranslationOptions(Options().PythonExecutablePath, Path.GetTempPath(), threads: 0));
    }

    [Fact]
    public async Task MissingModelPreventsProcessStartup()
    {
        var starts = 0;
        await using var engine = new ArgosTextTranslationEngine(Options(), new CatalogStub { Missing = true }, (_, _) =>
        {
            starts++;
            return Task.FromResult<IArgosSession>(new SessionStub());
        });
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.TranslateAsync("fixture", Language.Spanish, Language.English));
        Assert.Equal(TranslationErrorCode.ModelUnavailable, failure.Code);
        Assert.Equal(0, starts);
    }

    [Theory]
    [InlineData(ModelIntegrity.Invalid)]
    [InlineData(ModelIntegrity.Unknown)]
    public async Task UnverifiedModelsAreNotLoaded(ModelIntegrity integrity)
    {
        await using var engine = Engine(new(), new CatalogStub { Integrity = integrity });
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.TranslateAsync("fixture", Language.Spanish, Language.English));
        Assert.Equal(TranslationErrorCode.ModelInvalid, failure.Code);
    }

    [Fact]
    public async Task InitializationFailuresAreIdentifiable()
    {
        await using var engine = new ArgosTextTranslationEngine(Options(), new CatalogStub(), (_, _) => throw new IOException("fixture"));
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.TranslateAsync("fixture", Language.Spanish, Language.English));
        Assert.Equal(TranslationErrorCode.RuntimeUnavailable, failure.Code);
        Assert.Equal(TranslationStage.TextTranslation, failure.Stage);
    }

    [Fact]
    public async Task StartupTimeoutDoesNotHangTheCaller()
    {
        await using var engine = new ArgosTextTranslationEngine(Options(startup: TimeSpan.FromMilliseconds(30)), new CatalogStub(), async (_, token) =>
        {
            await Task.Delay(System.Threading.Timeout.Infinite, token);
            return new SessionStub();
        });
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.TranslateAsync("fixture", Language.Spanish, Language.English).WaitAsync(Timeout));
        Assert.Equal(TranslationErrorCode.RuntimeUnavailable, failure.Code);
    }

    [Fact]
    public async Task ConsecutiveRequestsReuseOneWorkerAndHaveDifferentIds()
    {
        var starts = 0;
        var ids = new List<string>();
        var session = new SessionStub { Translate = (id, _, _, _, _) => { ids.Add(id); return Task.FromResult("fixture"); } };
        var catalog = new CatalogStub();
        await using var engine = new ArgosTextTranslationEngine(Options(), catalog, (_, _) =>
        {
            starts++;
            return Task.FromResult<IArgosSession>(session);
        });
        await engine.TranslateAsync("first", Language.Spanish, Language.English);
        await engine.TranslateAsync("second", Language.English, Language.Spanish);
        Assert.Equal(1, starts);
        Assert.Equal(2, catalog.Validations);
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public async Task CancellationDropsTheActiveWorkerAndAllowsRestart()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = BlockingSession(entered);
        var replacement = new SessionStub();
        var starts = 0;
        await using var engine = new ArgosTextTranslationEngine(Options(), new CatalogStub(), (_, _) =>
            Task.FromResult<IArgosSession>(++starts == 1 ? session : replacement));
        using var cancellation = new CancellationTokenSource();
        var run = engine.TranslateAsync("fixture", Language.Spanish, Language.English, cancellation.Token);
        await entered.Task.WaitAsync(Timeout);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Timeout));
        Assert.Equal(1, session.Disposals);
        Assert.Equal("translated fixture", await engine.TranslateAsync("next", Language.English, Language.Spanish));
        Assert.Equal(2, starts);
    }

    [Fact]
    public async Task QueuedCancellationDoesNotInterruptActiveWork()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new SessionStub { Translate = (_, _, _, _, token) => { entered.SetResult(); return release.Task.WaitAsync(token); } };
        await using var engine = Engine(session);
        var active = engine.TranslateAsync("first", Language.Spanish, Language.English);
        await entered.Task.WaitAsync(Timeout);
        using var cancellation = new CancellationTokenSource();
        var queued = engine.TranslateAsync("next", Language.English, Language.Spanish, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(Timeout));
        Assert.Equal(1, session.Calls);
        Assert.Equal(0, session.Disposals);
        release.SetResult("fixture");
        Assert.Equal("fixture", await active.WaitAsync(Timeout));
    }

    [Fact]
    public async Task TranslationTimeoutClosesTheWorker()
    {
        var session = BlockingSession(new(TaskCreationOptions.RunContinuationsAsynchronously));
        await using var engine = Engine(session, options: Options(translation: TimeSpan.FromMilliseconds(30)));
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.TranslateAsync("fixture", Language.Spanish, Language.English).WaitAsync(Timeout));
        Assert.Equal(TranslationErrorCode.EngineFailure, failure.Code);
        Assert.Equal(1, session.Disposals);
    }

    [Fact]
    public async Task PreCanceledRequestsDoNotStartAWorker()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var catalog = new CatalogStub();
        await using var engine = Engine(new(), catalog);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.TranslateAsync("fixture", Language.Spanish, Language.English, cancellation.Token));
        Assert.Equal(0, catalog.Validations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task BrokenCommunicationClosesTheWorkerAndReportsTheStage(int kind)
    {
        var session = new SessionStub { Translate = (_, _, _, _, _) => throw (kind switch
        {
            0 => new EndOfStreamException(), 1 => new InvalidDataException(), _ => new JsonException()
        }) };
        await using var engine = Engine(session);
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.TranslateAsync("fixture", Language.Spanish, Language.English));
        Assert.Equal(TranslationStage.TextTranslation, failure.Stage);
        Assert.Equal(TranslationErrorCode.EngineFailure, failure.Code);
        Assert.Equal(1, session.Disposals);
    }

    [Fact]
    public async Task EmptyWorkerOutputIsAnError()
    {
        await using var engine = Engine(new() { Translate = (_, _, _, _, _) => Task.FromResult(" ") });
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.TranslateAsync("fixture", Language.Spanish, Language.English));
        Assert.Equal(TranslationErrorCode.InvalidEngineOutput, failure.Code);
    }

    [Fact]
    public async Task DisposalCancelsActiveWorkAndDisposesExactlyOnce()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = BlockingSession(entered);
        var engine = Engine(session);
        var run = engine.TranslateAsync("fixture", Language.Spanish, Language.English);
        await entered.Task.WaitAsync(Timeout);
        await Task.WhenAll(engine.DisposeAsync().AsTask(), engine.DisposeAsync().AsTask()).WaitAsync(Timeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, session.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.TranslateAsync("fixture", Language.Spanish, Language.English));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoordinatorKeepsProgressiveRulesForTextAndVoice(bool voice)
    {
        await using var engine = Engine(new());
        var others = new TestEngines();
        var service = new TranslationService(others, engine, others, others);
        var stream = voice ? service.TranslateVoiceAsync(new(ContractValidationTests.CreateAudio(), Language.Spanish, Language.English))
            : service.TranslateTextAsync(new("fixture", Language.Spanish, Language.English));
        var updates = new List<TranslationUpdate>();
        await foreach (var update in stream)
            updates.Add(update);
        Assert.Equal("translated fixture", Assert.IsType<TextTranslationCompleted>(updates[voice ? 1 : 0]).TranslatedText);
        Assert.Single(updates.OfType<PronunciationCompleted>());
        Assert.Single(updates.OfType<SpeechSynthesisCompleted>());
    }

    [Fact]
    public void ProtocolRejectsMismatchedIdsVersionsAndMalformedJson()
    {
        Assert.Throws<InvalidDataException>(() => ArgosProtocol.ParseResponse("{\"protocol\":1,\"id\":\"other\",\"ok\":true,\"text\":\"fixture\"}", "wanted"));
        Assert.Throws<InvalidDataException>(() => ArgosProtocol.ParseResponse("{\"protocol\":2,\"id\":\"wanted\",\"ok\":true}", "wanted"));
        Assert.ThrowsAny<JsonException>(() => ArgosProtocol.ParseResponse("bad json", "wanted"));
        Assert.Throws<InvalidDataException>(() => ArgosProtocol.ParseResponse(null, "wanted"));
    }

    [Fact]
    public void ProtocolReturnsUnicodeAndDoesNotExposeProviderDetails()
    {
        var success = JsonSerializer.Serialize(new { protocol = 1, id = "fixture", ok = true, text = "\u00bfPedido?" });
        Assert.Equal("\u00bfPedido?", ArgosProtocol.ParseResponse(success, "fixture"));
        var failure = JsonSerializer.Serialize(new { protocol = 1, id = "fixture", ok = false, error = "private provider content" });
        Assert.DoesNotContain("private", Assert.Throws<TranslationEngineException>(() => ArgosProtocol.ParseResponse(failure, "fixture")).Message);
    }

    private static SessionStub BlockingSession(TaskCompletionSource entered) => new()
    {
        Translate = async (_, _, _, _, token) =>
        {
            entered.SetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, token);
            return "unreachable fixture";
        }
    };

    private sealed class SessionStub : IArgosSession
    {
        internal Func<string, string, string, string, CancellationToken, Task<string>> Translate { get; init; } =
            (_, _, _, _, _) => Task.FromResult("translated fixture");
        internal int Calls { get; private set; }
        internal int Disposals { get; private set; }
        public int ProcessId => 123;
        public long WorkingSetBytes => 1024;
        public Task<string> TranslateAsync(string id, string text, string source, string target, CancellationToken token)
        { Calls++; return Translate(id, text, source, target, token); }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class CatalogStub : IModelCatalog
    {
        internal bool Missing { get; init; }
        internal ModelIntegrity Integrity { get; init; } = ModelIntegrity.Valid;
        internal int Validations { get; private set; }
        public Task<IReadOnlyList<ModelDescriptor>> GetInstalledModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public Task<ModelDescriptor?> LocateAsync(string modelId, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult<ModelDescriptor?>(Missing ? null : new(modelId, version, ModelPurpose.TextTranslation,
                ["es", "en"], Path.GetTempPath(), []));
        public Task<ModelValidationResult> ValidateAsync(ModelDescriptor model, CancellationToken cancellationToken = default)
        { Validations++; return Task.FromResult(new ModelValidationResult(ModelAvailability.Available, ModelCompatibility.Unknown, Integrity)); }
    }
}
