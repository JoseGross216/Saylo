using System.Text.Json;
using System.Text.RegularExpressions;
using Saylo.Modules.ModelManagement.Contracts;
using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.TextTranslation;

namespace Saylo.Modules.Translation.Tests;

// Test sessions verify coordination and invariants, never linguistic quality.
public sealed class TranslationRejectionTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData("Le informaremos cuando termine.", "We'll report them by mail after I'm through.")]
    [InlineData("Confirmaremos el pedido.", "I will confirm the order.")]
    [InlineData("Enviaremos los documentos.", "Send the documents.")]
    public void ExplicitPluralCommitmentCannotChangeTheSpeaker(string source, string translated)
    {
        var failure = Assert.Throws<TranslationEngineException>(() => LinguisticConstraints.Validate(
            source, translated, "es", "en", new()));
        Assert.Equal("Referent", failure.Data["IntegrityConstraint"]);
    }

    [Theory]
    [InlineData("Le informaremos cuando termine.", "We will notify you when it is complete.")]
    [InlineData("Revisaremos el pedido.", "We'll review the order.")]
    [InlineData("Contactaremos soporte@example.com.", "We will contact soporte@example.com.")]
    public void ExplicitPluralCommitmentKeepsAPluralSpeaker(string source, string translated) =>
        LinguisticConstraints.Validate(source, translated, "es", "en", new());

    [Fact]
    public async Task NativeAlternativeMustPassTheSameReferentConstraint()
    {
        var session = new Session
        {
            Primary = (_, _) => Task.FromResult("I need to confirm his delivery address."),
            Alternatives = (_, _) => Task.FromResult<IReadOnlyList<string>>(
                ["I need to confirm his delivery address.", "I need to confirm the delivery address."])
        };
        await using var engine = Engine(session);
        Assert.Equal("I need to confirm the delivery address.",
            await engine.TranslateAsync("Necesito confirmar la direccion de entrega.", Language.Spanish, Language.English));
        Assert.Equal(1, session.PrimaryCalls);
        Assert.Equal(1, session.AlternativeCalls);
        Assert.Equal(session.Requests[0].Text, session.Requests[1].Text);
        Assert.Equal(2, session.Requests.Select(request => request.Id).Distinct().Count());
        Assert.Equal(0, session.Disposals);
    }

    [Fact]
    public async Task FormalAlternativeIsSelectedWithoutRewriting()
    {
        var session = new Session
        {
            Primary = (_, _) => Task.FromResult("Necesito verificar tu direccion de entrega."),
            Alternatives = (_, _) => Task.FromResult<IReadOnlyList<string>>(
                ["Necesito verificar tu direccion de entrega.", "Necesito verificar su direccion de entrega."])
        };
        await using var engine = Engine(session, CustomerServiceTranslationProfile.Create());
        Assert.Equal("Necesito verificar su direccion de entrega.",
            await engine.TranslateAsync("I need to verify your delivery address.", Language.English, Language.Spanish));
    }

    [Fact]
    public async Task AlternativeWithWrongValueFailsEvenWhenCurrencyIsExplicit()
    {
        var session = new Session
        {
            Primary = (_, _) => Task.FromResult("$80"),
            Alternatives = (_, _) => Task.FromResult<IReadOnlyList<string>>(["$80", "US $90", "US $80"])
        };
        await using var engine = Engine(session);
        Assert.Equal("US $80", await engine.TranslateAsync("80 USD", Language.Spanish, Language.English));
    }

    [Fact]
    public async Task MissingOrDuplicatedMarkersCannotBeRecoveredByGuessing()
    {
        var session = new Session
        {
            Primary = (_, _) => Task.FromResult("Email."),
            Alternatives = (text, _) => Task.FromResult<IReadOnlyList<string>>(
                ["Email.", text + " " + Regex.Match(text, @"\d{9}").Value])
        };
        await using var engine = Engine(session);
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() =>
            engine.TranslateAsync("Email fixture@example.com.", Language.English, Language.Spanish));
        Assert.Equal(TranslationErrorCode.IntegrityValidationFailed, failure.Code);
        Assert.Equal("MarkerCorrespondence", failure.Data["IntegrityConstraint"]);
        Assert.DoesNotContain("fixture", failure.Message);
    }

    [Fact]
    public async Task ValidMarkersInAnAlternativeAreRestoredExactly()
    {
        var session = new Session
        {
            Primary = (_, _) => Task.FromResult("Email."),
            Alternatives = (text, _) => Task.FromResult<IReadOnlyList<string>>(["Email.", text])
        };
        await using var engine = Engine(session);
        Assert.Equal("Email fixture@example.com.",
            await engine.TranslateAsync("Email fixture@example.com.", Language.English, Language.Spanish));
    }

    [Fact]
    public async Task LegacyCapabilityDoesNotEnableUnnegotiatedRetries()
    {
        var session = new Session { SupportsRankedCandidates = false, Primary = (_, _) => Task.FromResult("His address.") };
        await using var engine = Engine(session);
        await Assert.ThrowsAsync<TranslationEngineException>(() =>
            engine.TranslateAsync("La direccion.", Language.Spanish, Language.English));
        Assert.Equal(0, session.AlternativeCalls);
    }

    [Fact]
    public async Task InvalidCandidatesDoNotCloseAHealthyCorrelatedSession()
    {
        var session = new Session
        {
            Primary = (_, _) => Task.FromResult("His address."),
            Alternatives = (_, _) => Task.FromResult<IReadOnlyList<string>>(["His address.", "Her address."])
        };
        await using var engine = Engine(session);
        await Assert.ThrowsAsync<TranslationEngineException>(() =>
            engine.TranslateAsync("La direccion.", Language.Spanish, Language.English));
        Assert.Equal(0, session.Disposals);
        session.Primary = (_, _) => Task.FromResult("Thank you.");
        Assert.Equal("Thank you.", await engine.TranslateAsync("Gracias.", Language.Spanish, Language.English));
    }

    [Fact]
    public async Task AmbiguousDatesRemainBlockedBeforeAnyInference()
    {
        var session = new Session();
        await using var engine = Engine(session);
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() =>
            engine.TranslateAsync("La fecha es 03/04/2026.", Language.Spanish, Language.English));
        Assert.Equal(TranslationErrorCode.IntegrityValidationFailed, failure.Code);
        Assert.Equal(0, session.PrimaryCalls);
        Assert.Equal(0, session.AlternativeCalls);
    }

    [Fact]
    public async Task UnprovenThirdPersonOwnerIsNotApprovedByTheCustomerProfile()
    {
        var session = new Session
        {
            Primary = (_, _) => Task.FromResult("Mrs. Ana Perez can pick up the package with her ID."),
            Alternatives = (_, _) => Task.FromResult<IReadOnlyList<string>>(
                ["Mrs. Ana Perez can pick up the package with her ID.",
                 "Mrs. Ana Perez can pick up the package with her identification."])
        };
        await using var engine = Engine(session, CustomerServiceTranslationProfile.Create());
        await Assert.ThrowsAsync<TranslationEngineException>(() =>
            engine.TranslateAsync("La senora Ana Perez puede recoger el paquete con su identificacion.", Language.Spanish, Language.English));
    }

    [Fact]
    public async Task RepeatedSegmentsReuseOnlyAValidatedResultWithinTheCurrentRequest()
    {
        var session = new Session { Primary = (_, _) => Task.FromResult("Thank you.") };
        await using var engine = Engine(session);
        Assert.Equal("Thank you. Thank you. Thank you.",
            await engine.TranslateAsync("Gracias. Gracias. Gracias.", Language.Spanish, Language.English));
        Assert.Equal(1, session.PrimaryCalls);
        await engine.TranslateAsync("Gracias. Gracias.", Language.Spanish, Language.English);
        Assert.Equal(2, session.PrimaryCalls);
    }

    [Fact]
    public async Task MemoizationDoesNotMixDifferentIdentifiersOrOmitRepeatedOccurrences()
    {
        var session = new Session();
        await using var engine = Engine(session);
        const string input = "Order AB-123. Order AB-456. Order AB-123.";
        Assert.Equal(input, await engine.TranslateAsync(input, Language.English, Language.Spanish));
        Assert.Equal(2, session.PrimaryCalls);
    }

    [Fact]
    public async Task CancellationIsCheckedBeforeReusingAValidatedSegment()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new Session { Primary = (_, _) => { cancellation.Cancel(); return Task.FromResult("Thank you."); } };
        await using var engine = Engine(session);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.TranslateAsync("Gracias. Gracias.", Language.Spanish, Language.English, cancellation.Token));
        Assert.Equal(1, session.PrimaryCalls);
        Assert.Equal(1, session.Disposals);
    }

    [Fact]
    public async Task UserCancellationDuringAlternativesClosesTheSession()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new Session
        {
            Primary = (_, _) => Task.FromResult("His address."),
            Alternatives = async (_, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return [];
            }
        };
        await using var engine = Engine(session);
        using var cancellation = new CancellationTokenSource();
        var run = engine.TranslateAsync("La direccion.", Language.Spanish, Language.English, cancellation.Token);
        await entered.Task.WaitAsync(Limit);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Limit));
        Assert.Equal(1, session.Disposals);
    }

    [Fact]
    public async Task AlternativesUseTheExistingTotalDeadline()
    {
        var session = new Session
        {
            Primary = (_, _) => Task.FromResult("His address."),
            Alternatives = async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return [];
            }
        };
        await using var engine = Engine(session, timeout: TimeSpan.FromMilliseconds(30));
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() =>
            engine.TranslateAsync("La direccion.", Language.Spanish, Language.English).WaitAsync(Limit));
        Assert.Equal(TranslationErrorCode.EngineFailure, failure.Code);
        Assert.Equal(1, session.Disposals);
    }

    [Fact]
    public async Task FailedAlternativesStillStopTextAndVoiceOptionalStages()
    {
        foreach (var voice in new[] { false, true })
        {
            var session = new Session
            {
                Primary = (_, _) => Task.FromResult("$80"),
                Alternatives = (_, _) => Task.FromResult<IReadOnlyList<string>>(["$80", "US $90"])
            };
            await using var engine = Engine(session);
            var others = new TestEngines { Recognize = (_, _, _) => Task.FromResult("80 USD") };
            var service = new TranslationService(others, engine, others, others);
            var updates = new List<TranslationUpdate>();
            var stream = voice
                ? service.TranslateVoiceAsync(new(ContractValidationTests.CreateAudio(), Language.Spanish, Language.English))
                : service.TranslateTextAsync(new("80 USD", Language.Spanish, Language.English));
            await foreach (var update in stream)
                updates.Add(update);
            Assert.Empty(updates.OfType<TextTranslationCompleted>());
            Assert.Equal(TranslationErrorCode.IntegrityValidationFailed, Assert.Single(updates.OfType<TranslationStageFailed>()).Code);
            Assert.DoesNotContain(others.Calls, call => call.Stage is TranslationStage.Pronunciation or TranslationStage.SpeechSynthesis);
        }
    }

    [Fact]
    public async Task AFailedLaterSegmentDoesNotExposeTheEarlierMemoizedResult()
    {
        var session = new Session
        {
            Primary = (text, _) => Task.FromResult(text.Contains("USD", StringComparison.Ordinal) ? "$80" : "Thank you."),
            Alternatives = (_, _) => Task.FromResult<IReadOnlyList<string>>(["$80", "US $90"])
        };
        await using var engine = Engine(session);
        await Assert.ThrowsAsync<TranslationEngineException>(() =>
            engine.TranslateAsync("Gracias. Gracias. 80 USD.", Language.Spanish, Language.English));
        Assert.Equal(2, session.PrimaryCalls);
    }

    [Fact]
    public async Task ConcurrentRequestsKeepIndependentMapsAndRequestScopedMemos()
    {
        var session = new Session();
        await using var engine = Engine(session);
        var texts = Enumerable.Range(1, 12).Select(index => $"Order AB-{index}. Order AB-{index}.").ToArray();
        var results = await Task.WhenAll(texts.Select(text => engine.TranslateAsync(text, Language.English, Language.Spanish))).WaitAsync(Limit);
        Assert.Equal(texts, results);
        Assert.Equal(12, session.PrimaryCalls);
        Assert.Equal(12, session.Requests.Select(request => request.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("80 USD", "US $80")]
    [InlineData("US$80", "80 USD")]
    [InlineData("-80 USD", "-US $80")]
    [InlineData("US $1,500.25", "1.500,25 USD")]
    public void ExplicitUsDollarNotationHasProvableNumericEquivalence(string source, string target) =>
        NumericIntegrity.Validate(source, target, new());

    [Theory]
    [InlineData("80 USD", "$80")]
    [InlineData("80 USD", "CA $80")]
    [InlineData("80 USD", "US $90")]
    [InlineData("-80 USD", "US $80")]
    [InlineData("80 USD", "80 EUR")]
    public void AmbiguousOrChangedCurrencyRemainsBlocked(string source, string target) =>
        Assert.Throws<TranslationEngineException>(() => NumericIntegrity.Validate(source, target, new()));

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("too-many")]
    [InlineData("null")]
    [InlineData("long")]
    [InlineData("mismatch")]
    [InlineData("correlation")]
    public void CandidateProtocolRejectsMalformedOrUncorrelatedBatches(string fault)
    {
        var candidates = fault switch
        {
            "empty" => Array.Empty<string?>(),
            "too-many" => Enumerable.Repeat<string?>("fixture", 5).ToArray(),
            "null" => [null],
            "long" => [new string('x', 4097)],
            "mismatch" => ["other"],
            _ => new string?[] { "fixture" }
        };
        var payload = fault == "missing" ? JsonSerializer.Serialize(new { protocol = 1, id = "fixture", ok = true, text = "fixture" }) :
            JsonSerializer.Serialize(new { protocol = 1, id = fault == "correlation" ? "other" : "fixture", ok = true, text = "fixture", candidates });
        Assert.Throws<InvalidDataException>(() => ArgosProtocol.ParseCandidates(payload, "fixture"));
    }

    [Fact]
    public void CandidateProtocolKeepsNativeOrderAndDeduplicatesWholeStrings()
    {
        var payload = JsonSerializer.Serialize(new { protocol = 1, id = "fixture", ok = true, text = "one",
            candidates = new[] { "one", "two", "two", "three" } });
        Assert.Equal(new[] { "one", "two", "three" }, ArgosProtocol.ParseCandidates(payload, "fixture"));
    }

    [Fact]
    public void CustomerProfileIsBidirectionalAndDoesNotAcceptTheKnownWrongGlossaryTerms()
    {
        var profile = CustomerServiceTranslationProfile.Create(["Saylo Pro"]);
        Assert.True(profile.RequireFormalSpanish);
        Assert.Contains(profile.Glossary, entry => entry.SourceLanguage == "es");
        Assert.Contains(profile.Glossary, entry => entry.SourceLanguage == "en");
        Assert.Throws<TranslationEngineException>(() =>
            LinguisticConstraints.Validate("Contact customer support.", "Contacte al cliente.", "en", "es", profile));
        Assert.Throws<TranslationEngineException>(() =>
            LinguisticConstraints.Validate("Water damage.", "Dano al agua.", "en", "es", profile));
        Assert.Throws<TranslationEngineException>(() =>
            LinguisticConstraints.Validate("Vamos a escalar.", "Let's scale up.", "es", "en", profile));
    }

    private static ArgosTextTranslationEngine Engine(Session session, TranslationIntegrityOptions? policy = null, TimeSpan? timeout = null) =>
        new(new(Path.Combine(Path.GetTempPath(), "saylo-python.exe"), Path.GetTempPath(), integrity: policy, translationTimeout: timeout),
            new Catalog(), (_, _) => Task.FromResult<IArgosSession>(session));

    private sealed class Session : IArgosCandidateSession
    {
        internal Func<string, CancellationToken, Task<string>> Primary { get; set; } = (text, _) => Task.FromResult(text);
        internal Func<string, CancellationToken, Task<IReadOnlyList<string>>> Alternatives { get; set; } =
            (_, _) => Task.FromResult<IReadOnlyList<string>>([]);
        internal int PrimaryCalls, AlternativeCalls, Disposals;
        internal List<(string Id, string Text)> Requests { get; } = [];
        public int ProcessId => 1;
        public long WorkingSetBytes => 0;
        public bool SupportsRankedCandidates { get; init; } = true;
        public Task<string> TranslateAsync(string id, string text, string source, string target, CancellationToken token)
        {
            Requests.Add((id, text));
            PrimaryCalls++;
            return Primary(text, token);
        }
        public Task<IReadOnlyList<string>> TranslateCandidatesAsync(string id, string text, string source, string target, CancellationToken token)
        {
            Requests.Add((id, text));
            AlternativeCalls++;
            return Alternatives(text, token);
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Catalog : IModelCatalog
    {
        public Task<IReadOnlyList<ModelDescriptor>> GetInstalledModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public Task<ModelDescriptor?> LocateAsync(string id, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult<ModelDescriptor?>(new(id, version, ModelPurpose.TextTranslation, ["es", "en"], Path.GetTempPath(), []));
        public Task<ModelValidationResult> ValidateAsync(ModelDescriptor model, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelValidationResult(ModelAvailability.Available, ModelCompatibility.Unknown, ModelIntegrity.Valid));
    }
}
