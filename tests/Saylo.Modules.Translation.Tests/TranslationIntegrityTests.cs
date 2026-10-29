using System.Text.RegularExpressions;
using Saylo.Modules.ModelManagement.Contracts;
using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.TextTranslation;

namespace Saylo.Modules.Translation.Tests;

public sealed class TranslationIntegrityTests
{
    private static readonly TranslationIntegrityOptions Defaults = new();
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData("Email soporte@example.com.", "soporte@example.com")]
    [InlineData("Tracking ZX987654321ES.", "ZX987654321ES")]
    [InlineData("Product AB-009871.", "AB-009871")]
    [InlineData("Visit https://example.com/orders?id=AB-123.", "https://example.com/orders?id=AB-123")]
    [InlineData("Call +1 (555) 123-4567.", "+1 (555) 123-4567")]
    [InlineData("Use SQL and model X200.", "SQL")]
    [InlineData("No comparta el codigo 004812.", "004812")]
    public void LiteralIsMaskedAndRestoredExactly(string source, string literal)
    {
        var plan = ProtectedText.Create(source, Defaults);
        Assert.NotEmpty(plan.Entries);
        Assert.DoesNotContain(literal, plan.Masked);
        Assert.Equal(source, plan.Restore(plan.Masked));
    }

    [Fact]
    public void RepeatedLiteralsHaveDifferentMarkersAndKeepTheirOccurrenceCount()
    {
        const string source = "soporte@example.com and soporte@example.com";
        var plan = ProtectedText.Create(source, Defaults);
        Assert.Equal(2, plan.Entries.Count);
        Assert.NotEqual(plan.Entries[0].Marker, plan.Entries[1].Marker);
        Assert.Equal(source, plan.Restore(plan.Masked));
    }

    [Fact]
    public void CollisionsWithOriginalTextAndPreviousMarkersAreRegenerated()
    {
        var values = new Queue<string>(["123456789", "234567891", "234567891", "345678912"]);
        var plan = ProtectedText.Create("123456789 SQL VPN", Defaults, values.Dequeue);
        Assert.Equal(new[] { "234567891", "345678912" }, plan.Entries.Select(entry => entry.Marker));
        Assert.Equal("123456789 SQL VPN", plan.Restore(plan.Masked));
    }

    [Fact]
    public void ExhaustedOrInvalidMarkerGeneratorFailsWithoutPrivateContent()
    {
        const string sensitive = "sensitive@example.com 123456789";
        var failure = Assert.Throws<TranslationEngineException>(() => ProtectedText.Create(sensitive, Defaults, () => "123456789"));
        Assert.Equal(TranslationErrorCode.IntegrityValidationFailed, failure.Code);
        Assert.DoesNotContain("sensitive", failure.Message);
        Assert.Throws<TranslationEngineException>(() => ProtectedText.Create("SQL", Defaults, () => "invalid"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicated")]
    [InlineData("modified")]
    [InlineData("embedded")]
    public void MarkerCorruptionNeverReturnsAPartiallyRestoredResult(string corruption)
    {
        var plan = ProtectedText.Create("Email soporte@example.com.", Defaults);
        var marker = plan.Entries.Single().Marker;
        var candidate = corruption switch
        {
            "missing" => "Email.",
            "duplicated" => plan.Masked + " " + marker,
            "embedded" => plan.Masked.Replace(marker, "X" + marker),
            _ => plan.Masked.Replace(marker, marker[..^1] + (marker[^1] == '1' ? "2" : "1"))
        };
        var failure = Assert.Throws<TranslationEngineException>(() => plan.Restore(candidate));
        Assert.DoesNotContain(marker, failure.Message);
        Assert.DoesNotContain("soporte", failure.Message);
    }

    [Fact]
    public void SwappedMarkersFailRatherThanGuessingTheirCorrespondence()
    {
        var plan = ProtectedText.Create("SQL then VPN", Defaults);
        var candidate = plan.Entries[1].Marker + " then " + plan.Entries[0].Marker;
        Assert.Throws<TranslationEngineException>(() => plan.Restore(candidate));
    }

    [Fact]
    public void RestoredNumericReferenceDoesNotDependOnTranslatedOrderTerminology()
    {
        var plan = TranslationIntegrityPlan.Prepare("Please confirm order 98765.", "en", "es", Defaults);
        var marker = plan.Segments.Single().Protection.Entries.Single().Marker;
        Assert.Equal("Confirme la orden 98765.", plan.ValidateSegment(plan.Segments.Single(), "Confirme la orden " + marker + "."));
    }

    [Fact]
    public void AdditionalUnprotectedIdentifierIsRejected()
    {
        var plan = TranslationIntegrityPlan.Prepare("Please confirm order AB-123.", "en", "es", Defaults);
        var segment = plan.Segments.Single();
        Assert.Throws<TranslationEngineException>(() => plan.ValidateSegment(segment, segment.Protection.Masked + " XY-999"));
    }

    [Fact]
    public void UppercaseNaturalLanguageIsNotTreatedAsAnIdentifier()
    {
        var plan = ProtectedText.Create("HELLO, DO NOT CANCEL THE ORDER.", Defaults);
        Assert.Empty(plan.Entries);
        Assert.Equal("HELLO, DO NOT CANCEL THE ORDER.", plan.Masked);
    }

    [Fact]
    public void NegativeWordsInsideAnEmailCannotHideLostSentenceNegation()
    {
        var plan = TranslationIntegrityPlan.Prepare("Do not cancel no@example.com.", "en", "es", Defaults);
        var segment = plan.Segments.Single();
        var marker = segment.Protection.Entries.Single().Marker;
        var failure = Assert.Throws<TranslationEngineException>(() => plan.ValidateSegment(segment, "Cancele " + marker + "."));
        Assert.Equal("Negation", failure.Data["IntegrityConstraint"]);
    }

    [Theory]
    [InlineData("49.95 USD", "USD 49,95")]
    [InlineData("EUR 1,250.50", "1.250,50 EUR")]
    [InlineData("15 %", "15 percent")]
    [InlineData("09:00", "9: 00")]
    [InlineData("17:30", "5:30 p.m.")]
    [InlineData("31/12/2026", "12/31/2026")]
    [InlineData("15 de noviembre de 2026", "November 15th 2026")]
    [InlineData("3 unidades", "3 units")]
    [InlineData("-25 EUR", "EUR -25")]
    [InlineData("-$25", "$-25")]
    [InlineData("\u221225 EUR", "EUR -25")]
    public void EquivalentValuesAndUnitsAreAccepted(string source, string target) =>
        NumericIntegrity.Validate(source, target, Defaults);

    [Theory]
    [InlineData("49.95 USD", "49.95 EUR")]
    [InlineData("80 USD", "$80")]
    [InlineData("25 EUR", "250 EUR")]
    [InlineData("-25 EUR", "25 EUR")]
    [InlineData("-$25", "$25")]
    [InlineData("\u221225 EUR", "25 EUR")]
    [InlineData("15 %", "15")]
    [InlineData("3 unidades", "3 kg")]
    [InlineData("09:00", "19:00")]
    [InlineData("2026-11-15", "2026-11-16")]
    [InlineData("25", "25 25")]
    [InlineData("3 X200 and 2 X300", "2 X200 and 3 X300")]
    public void ChangedValuesUnitsSignsAndAssociationsFail(string source, string target) =>
        Assert.Throws<TranslationEngineException>(() => NumericIntegrity.Validate(source, target, Defaults));

    [Theory]
    [InlineData("03/04/2026")]
    [InlineData("1,234 USD")]
    [InlineData("1.234 EUR")]
    [InlineData("31/02/2026")]
    [InlineData("25:90")]
    [InlineData("2026/03/04")]
    [InlineData("1 250,50 EUR")]
    [InlineData("(25 USD)")]
    [InlineData("20-30 EUR")]
    [InlineData("004812")]
    public void AmbiguousOrInvalidNumericInputIsRejectedBeforeInference(string text) =>
        Assert.Throws<TranslationEngineException>(() => TranslationIntegrityPlan.Prepare(text, "es", "en", Defaults));

    [Theory]
    [InlineData("No cancele el pedido.", "Cancel the order.", "es", "en")]
    [InlineData("Your request is complete.", "Su solicitud no esta completa.", "en", "es")]
    [InlineData("Nunca pediremos su clave.", "We ask for your password.", "es", "en")]
    [InlineData("Su direccion.", "His address.", "es", "en")]
    [InlineData("Necesito confirmar la direccion de entrega.", "I need to confirm his delivery address.", "es", "en")]
    [InlineData("Antes del viernes.", "By Friday.", "es", "en")]
    public void ObservableLinguisticConstraintsFailClosed(string source, string target, string from, string to) =>
        Assert.Throws<TranslationEngineException>(() => LinguisticConstraints.Validate(source, target, from, to, Defaults));

    [Fact]
    public void FormalPolicyAndGlossaryValidateWithoutRewriting()
    {
        var options = new TranslationIntegrityOptions(requireFormalSpanish: true, glossary:
            [new("en", "customer support", ["atencion al cliente", "soporte al cliente"])]);
        Assert.Throws<TranslationEngineException>(() =>
            LinguisticConstraints.Validate("Your address.", "Tu direccion.", "en", "es", options));
        Assert.Throws<TranslationEngineException>(() =>
            LinguisticConstraints.Validate("Contact customer support.", "Contacte al cliente.", "en", "es", options));
        LinguisticConstraints.Validate("Contact customer support.", "Contacte con soporte al cliente.", "en", "es", options);
    }

    [Fact]
    public void GlossaryAndProtectedTermsAreSnapshots()
    {
        var targets = new[] { "soporte" };
        var terms = new[] { "Saylo Pro" };
        var options = new TranslationIntegrityOptions(protectedTerms: terms, glossary: [new("en", "support", targets)]);
        targets[0] = "changed";
        terms[0] = "changed";
        Assert.Equal("soporte", options.Glossary[0].AllowedTargets[0]);
        Assert.Equal("Saylo Pro", options.ProtectedTerms[0]);
        var protectedText = ProtectedText.Create("Saylo Pro", options);
        Assert.Equal("Saylo Pro", protectedText.Restore(protectedText.Masked));
    }

    [Fact]
    public void SegmenterKeepsAbbreviationsDecimalsUrlsAndExactSeparators()
    {
        const string text = "  Sr. Lopez pago 49.95 USD.  Visite https://example.com/a.b.\nNo cancele AB-123.  ";
        var segments = SafeTextSegmenter.Split(text, Defaults);
        Assert.Equal(text, string.Concat(segments.Select(segment => segment.Text + segment.Separator)));
        Assert.Contains(segments, segment => segment.Text.Contains("Sr. Lopez pago 49.95 USD."));
        Assert.Contains(segments, segment => segment.Text.Contains("https://example.com/a.b."));
    }

    [Fact]
    public void LongTextUsesOrderedWholeSentencesAndNeverHardWraps()
    {
        var text = string.Concat(Enumerable.Repeat("No cancele el pedido. ", 40));
        var segments = SafeTextSegmenter.Split(text, Defaults);
        Assert.Equal(40, segments.Count);
        Assert.Throws<TranslationEngineException>(() => SafeTextSegmenter.Split(new string('a', 241), Defaults));
        Assert.Throws<TranslationEngineException>(() => SafeTextSegmenter.Split(string.Concat(Enumerable.Repeat("Hi. ", 65)), Defaults));
        var bounded = string.Concat(Enumerable.Repeat("No cancele el pedido. ", 20));
        var safe = SafeTextSegmenter.Split(bounded, Defaults);
        Assert.Equal(20, safe.Count);
        Assert.Equal(bounded, string.Concat(safe.Select(segment => segment.Text + segment.Separator)));
    }

    [Theory]
    [InlineData("Email soporte@example.com.", "Email corrupted.")]
    [InlineData("25 USD", "250 USD")]
    [InlineData("No cancele el pedido.", "Cancel the order.")]
    public async Task IntegrityFailureStopsTextAndVoiceBeforeOptionalStages(string input, string candidate)
    {
        foreach (var voice in new[] { false, true })
        {
            var session = new Session { Translate = text => Task.FromResult(candidate) };
            await using var engine = Engine(session);
            var other = new TestEngines { Recognize = (_, _, _) => Task.FromResult(input) };
            var service = new TranslationService(other, engine, other, other);
            var updates = new List<TranslationUpdate>();
            var stream = voice ? service.TranslateVoiceAsync(new(ContractValidationTests.CreateAudio(), Language.Spanish, Language.English))
                : service.TranslateTextAsync(new(input, Language.Spanish, Language.English));
            await foreach (var update in stream)
                updates.Add(update);
            Assert.Empty(updates.OfType<TextTranslationCompleted>());
            Assert.Equal(TranslationErrorCode.IntegrityValidationFailed, Assert.Single(updates.OfType<TranslationStageFailed>()).Code);
            Assert.DoesNotContain(other.Calls, call => call.Stage is TranslationStage.Pronunciation or TranslationStage.SpeechSynthesis);
        }
    }

    [Fact]
    public async Task LaterSegmentFailureDoesNotPublishAnEarlierPartialTranslation()
    {
        var calls = 0;
        var session = new Session { Translate = text => Task.FromResult(++calls == 1 ? "Thank you." : "Email corrupted.") };
        await using var engine = Engine(session);
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() =>
            engine.TranslateAsync("Gracias. Email soporte@example.com.", Language.Spanish, Language.English));
        Assert.Equal(2, calls);
        Assert.Equal(TranslationErrorCode.IntegrityValidationFailed, failure.Code);
    }

    [Fact]
    public async Task ConcurrentRequestsUseIndependentProtectionMapsAndOneSerializedSession()
    {
        var session = new Session { Translate = text => Task.FromResult(text) };
        await using var engine = Engine(session);
        var texts = Enumerable.Range(0, 16).Select(index => $"Email test{index}@example.com.").ToArray();
        var outputs = await Task.WhenAll(texts.Select(text => engine.TranslateAsync(text, Language.English, Language.Spanish))).WaitAsync(Limit);
        Assert.Equal(texts, outputs);
        Assert.Equal(16, session.Calls);
    }

    [Fact]
    public async Task CancellationBetweenSegmentsPreventsFurtherInferenceAndClosesSession()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new Session { Translate = text => { cancellation.Cancel(); return Task.FromResult("Thank you."); } };
        await using var engine = Engine(session);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.TranslateAsync("Gracias. Gracias.", Language.Spanish, Language.English, cancellation.Token));
        Assert.Equal(1, session.Calls);
        Assert.Equal(1, session.Disposals);
    }

    private static ArgosTextTranslationEngine Engine(Session session) =>
        new(new(Path.Combine(Path.GetTempPath(), "saylo-python.exe"), Path.GetTempPath()), new Catalog(),
            (_, _) => Task.FromResult<IArgosSession>(session));

    private sealed class Session : IArgosSession
    {
        internal Func<string, Task<string>> Translate { get; init; } = text => Task.FromResult(text);
        internal int Calls { get; private set; }
        internal int Disposals { get; private set; }
        public int ProcessId => 1;
        public long WorkingSetBytes => 0;
        public Task<string> TranslateAsync(string id, string text, string source, string target, CancellationToken token)
        {
            Calls++;
            return Translate(text);
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Catalog : IModelCatalog
    {
        public Task<IReadOnlyList<ModelDescriptor>> GetInstalledModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public Task<ModelDescriptor?> LocateAsync(string modelId, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult<ModelDescriptor?>(new(modelId, version, ModelPurpose.TextTranslation, ["es", "en"], Path.GetTempPath(), []));
        public Task<ModelValidationResult> ValidateAsync(ModelDescriptor model, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelValidationResult(ModelAvailability.Available, ModelCompatibility.Unknown, ModelIntegrity.Valid));
    }
}
