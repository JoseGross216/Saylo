using System.Reflection;
using Saylo.Modules.ModelManagement.Contracts;
using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.Pronunciation;

namespace Saylo.Modules.Translation.Tests;

public sealed class LocalPronunciationTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData("//server/share")]
    public void ResourceFactoryRejectsNetworkLocations(string path) =>
        Assert.Throws<ArgumentException>(() => PronunciationResources.CreateCatalog(path));

    [Fact]
    public void SpanishLoanwordsWithoutAnApprovedReadingAreNotGuessed() =>
        Assert.Equal("AmbiguousPronunciation", Assert.Throws<TranslationEngineException>(() =>
            new SpanishPronunciation().Generate("email")).Data["PronunciationReason"]);

    [Theory]
    [InlineData("hola", "ˈola")]
    [InlineData("gracias", "ˈɡɾasjas")]
    [InlineData("pedido", "peˈdido")]
    [InlineData("término", "ˈteɾmino")]
    [InlineData("termino", "teɾˈmino")]
    [InlineData("terminó", "teɾmiˈno")]
    [InlineData("día", "ˈdia")]
    [InlineData("país", "paˈis")]
    [InlineData("guitarra", "ɡiˈtara")]
    [InlineData("llave", "ˈʝabe")]
    [InlineData("queso", "ˈkeso")]
    [InlineData("ciudad", "sjuˈdad")]
    [InlineData("pingüino", "pinˈɡwino")]
    [InlineData("aire", "ˈai̯ɾe")]
    [InlineData("méxico", "ˈmexiko")]
    [InlineData("rápidamente", "ˌrapidaˈmente")]
    public void SpanishRulesProduceBroadPhonesAndLexicalStress(string word, string expected) =>
        Assert.Equal(expected, new SpanishPronunciation().Generate(word));

    [Theory]
    [InlineData("TH AE1 NG K", "ˈθæŋk")]
    [InlineData("K AH0 N F ER1 M", "kənˈfɝm")]
    [InlineData("AH0 D R EH1 S", "əˈdɹɛs")]
    [InlineData("K AH1 S T AH0 M ER0", "ˈkʌstəmɚ")]
    [InlineData("IH2 N F ER0 M EY1 SH AH0 N", "ˌɪnfɚˈmeɪʃən")]
    public void ArpabetConversionKeepsStressAndUsRhoticity(string phones, string expected) =>
        Assert.Equal(expected, EnglishPronunciation.ToIpa(phones));

    [Theory]
    [InlineData("", "InputBudget")]
    [InlineData("   ", "InputBudget")]
    [InlineData("...", "NoWords")]
    [InlineData("Contact help@example.com.", "UnverifiedIdentifier")]
    [InlineData("Visite https://example.com/help.", "UnverifiedIdentifier")]
    [InlineData("Order AB-009871.", "UnverifiedIdentifier")]
    [InlineData("Tracking ZX987654321ES.", "UnverifiedIdentifier")]
    [InlineData("Call +1 (212) 555-0199.", "UnverifiedIdentifier")]
    [InlineData("SQL", "UnverifiedIdentifier")]
    [InlineData("NASA", "UnverifiedAbbreviation")]
    [InlineData("Dr. Smith", "UnverifiedAbbreviation")]
    [InlineData("03/04/2026", "NumericReadingUnavailable")]
    [InlineData("2026-12-31", "NumericReadingUnavailable")]
    [InlineData("49.95 USD", "NumericReadingUnavailable")]
    [InlineData("$80", "NumericReadingUnavailable")]
    [InlineData("25%", "NumericReadingUnavailable")]
    [InlineData("0048", "NumericReadingUnavailable")]
    [InlineData("3 units", "NumericReadingUnavailable")]
    [InlineData("<speak>hola</speak>", "UnsupportedSymbol")]
    public async Task UnverifiableInputFailsWithoutInventingReadings(string text, string reason)
    {
        await using var engine = new LocalPronunciationEngine(new Catalog(null));
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.GenerateAsync(text, Language.Spanish));
        Assert.Equal(TranslationStage.Pronunciation, failure.Stage);
        Assert.Equal(reason, failure.Data["PronunciationReason"]);
        if (text.Length > 0)
            Assert.DoesNotContain(text, failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, engine.DictionaryLoadCount);
    }

    [Theory]
    [InlineData("Jencer")]
    [InlineData("qwertyzz")]
    [InlineData("François")]
    public async Task UnknownSpanishWordsAndNamesAreNotSoundedOutArbitrarily(string text)
    {
        await using var engine = new LocalPronunciationEngine(new Catalog(null));
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.GenerateAsync(text, Language.Spanish));
        Assert.Equal("OutOfVocabulary", failure.Data["PronunciationReason"]);
    }

    [Fact]
    public async Task CompleteSpanishSentenceWorksWithoutEnglishResources()
    {
        var catalog = new Catalog(null);
        await using var engine = new LocalPronunciationEngine(catalog);
        var result = await engine.GenerateAsync("Hola. ¿Cómo puedo ayudarle hoy?", Language.Spanish);
        Assert.Equal("ˈola. ¿ˈkomo ˈpwedo aʝuˈdaɾle oi̯?", result.Text);
        Assert.Equal(PronunciationNotation.Ipa, result.Notation);
        Assert.Equal(0, catalog.Locations);
    }

    [Theory]
    [InlineData("0", "ˈseɾo")]
    [InlineData("22", "bei̯ntiˈdos")]
    [InlineData("31", "ˈtɾei̯nta i ˈuno")]
    public async Task OnlyExplicitStandaloneSmallCardinalsAreExpanded(string text, string expected)
    {
        await using var engine = new LocalPronunciationEngine(new Catalog(null));
        var result = await engine.GenerateAsync(text, Language.Spanish);
        Assert.Equal(expected, result.Text);
    }

    [Fact]
    public async Task UnicodeNormalizationPreservesAccentedStress()
    {
        await using var engine = new LocalPronunciationEngine(new Catalog(null));
        Assert.Equal((await engine.GenerateAsync("término", Language.Spanish)).Text,
            (await engine.GenerateAsync("te\u0301rmino", Language.Spanish)).Text);
    }

    [Fact]
    public async Task PublicLanguageContractRejectsUnsupportedLanguagesAndNullIsNotInferred()
    {
        Assert.Throws<ArgumentException>(() => Language.FromCode("fr"));
        await using var engine = new LocalPronunciationEngine(new Catalog(null));
        await Assert.ThrowsAsync<ArgumentNullException>(() => engine.GenerateAsync("hola", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => engine.GenerateAsync(null!, Language.Spanish));
        var futureLanguage = (Language)typeof(Language).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(string)], null)!.Invoke(["fr"]);
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.GenerateAsync("bonjour", futureLanguage));
        Assert.Equal(TranslationErrorCode.UnsupportedLanguage, failure.Code);
    }

    [Fact]
    public async Task MissingDictionaryDoesNotPreventSpanishOrPoisonLaterRequests()
    {
        var catalog = new Catalog(null);
        await using var engine = new LocalPronunciationEngine(catalog);
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.GenerateAsync("hello", Language.English));
        Assert.Equal(TranslationErrorCode.ModelUnavailable, failure.Code);
        Assert.Equal("ˈola", (await engine.GenerateAsync("hola", Language.Spanish)).Text);
        Assert.Equal(0, engine.DictionaryLoadCount);
    }

    [Theory]
    [InlineData(ModelIntegrity.Invalid, ModelAvailability.Available, TranslationErrorCode.ModelInvalid)]
    [InlineData(ModelIntegrity.Unknown, ModelAvailability.Available, TranslationErrorCode.ModelInvalid)]
    [InlineData(ModelIntegrity.Valid, ModelAvailability.Inaccessible, TranslationErrorCode.ModelUnavailable)]
    public async Task ResourcesMustBeValidatedBeforeLoading(ModelIntegrity integrity, ModelAvailability availability, TranslationErrorCode expected)
    {
        using var dictionary = new DictionaryFile();
        var catalog = new Catalog(dictionary.Model) { Validation = new(availability, ModelCompatibility.Compatible, integrity) };
        var loads = 0;
        await using var engine = new LocalPronunciationEngine(catalog, (path, token) =>
        { loads++; return EnglishPronunciation.Load(path, token); });
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.GenerateAsync("hello", Language.English));
        Assert.Equal(expected, failure.Code);
        Assert.Equal(0, loads);
    }

    [Fact]
    public async Task WrongPurposeAndMissingRedistributionNoticeFailBeforeLoading()
    {
        using var dictionary = new DictionaryFile();
        foreach (var model in new[] { dictionary.Model with { Purpose = ModelPurpose.TextTranslation },
                     dictionary.Model with { Files = dictionary.Model.Files.Where(file => file.RelativePath != "LICENSE").ToArray() } })
        {
            await using var engine = new LocalPronunciationEngine(new Catalog(model));
            var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.GenerateAsync("hello", Language.English));
            Assert.Equal(TranslationErrorCode.ModelInvalid, failure.Code);
        }
    }

    [Theory]
    [InlineData("hello INVALID", "DictionaryFormat")]
    [InlineData("hello HH AH3 L OW1", "DictionaryFormat")]
    [InlineData("", "DictionaryFormat")]
    public void MalformedPhonemeResourcesAreNotAccepted(string data, string reason)
    {
        using var dictionary = new DictionaryFile(data);
        var failure = Assert.Throws<TranslationEngineException>(() => EnglishPronunciation.Load(dictionary.Path, CancellationToken.None));
        Assert.Equal(reason, failure.Data["PronunciationReason"]);
    }

    [Fact]
    public void DictionaryWithoutSyllabicInformationDoesNotInventVowels()
    {
        using var dictionary = new DictionaryFile("hmm HH M");
        var engine = EnglishPronunciation.Load(dictionary.Path, CancellationToken.None);
        Assert.Equal("NoSyllable", Assert.Throws<TranslationEngineException>(() => engine.Generate(["hmm"], 0)).Data["PronunciationReason"]);
    }

    [Fact]
    public void ReadRequiresSupportedContextAndNeverUsesTheFirstVariantBlindly()
    {
        using var dictionary = new DictionaryFile();
        var engine = EnglishPronunciation.Load(dictionary.Path, CancellationToken.None);
        Assert.Equal("ˈɹid", engine.Generate(["please", "read"], 1));
        Assert.Equal("ˈɹɛd", engine.Generate(["have", "read"], 1));
        Assert.Throws<TranslationEngineException>(() => engine.Generate(["I", "read"], 1));
        Assert.Throws<TranslationEngineException>(() => engine.Generate(["will", ".", "read"], 2));
        Assert.Throws<TranslationEngineException>(() => engine.Generate(["have", "read", "access"], 1));
        Assert.Throws<TranslationEngineException>(() => engine.Generate(["record"], 0));
        Assert.Throws<TranslationEngineException>(() => engine.Generate(["unknownname"], 0));
    }

    [Fact]
    public async Task ConcurrentRequestsReuseOneDictionaryAndKeepIndependentResults()
    {
        using var dictionary = new DictionaryFile();
        await using var engine = new LocalPronunciationEngine(new Catalog(dictionary.Model), (path, token) => EnglishPronunciation.Load(path, token));
        var tasks = Enumerable.Range(0, 24).Select(index => engine.GenerateAsync(index % 2 == 0 ? "hello" : "hola",
            index % 2 == 0 ? Language.English : Language.Spanish)).ToArray();
        var results = await Task.WhenAll(tasks).WaitAsync(Limit);
        for (var index = 0; index < results.Length; index++)
            Assert.Equal(index % 2 == 0 ? "həˈloʊ" : "ˈola", results[index].Text);
        Assert.Equal(1, engine.DictionaryLoadCount);
        Assert.True(engine.EnglishWordCount > 0);
        Assert.True(engine.SpanishWordCount > 0);
        File.Move(dictionary.Path, dictionary.Path + ".moved");
        File.Move(dictionary.Path + ".moved", dictionary.Path);
    }

    [Fact]
    public async Task ProductionLoaderChecksTheOpenedFileEvenIfACustomCatalogClaimsValidity()
    {
        using var dictionary = new DictionaryFile();
        await using var engine = new LocalPronunciationEngine(new Catalog(dictionary.Model));
        var failure = await Assert.ThrowsAsync<TranslationEngineException>(() => engine.GenerateAsync("hello", Language.English));
        Assert.Equal(TranslationErrorCode.ModelInvalid, failure.Code);
        Assert.Equal("DictionaryChecksum", failure.Data["PronunciationReason"]);
        Assert.Equal(0, engine.DictionaryLoadCount);
    }

    [Fact]
    public async Task ActiveCancellationDuringLoadingLeavesNoPartialDictionaryAndCanRetry()
    {
        using var dictionary = new DictionaryFile();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var engine = new LocalPronunciationEngine(new Catalog(dictionary.Model), (path, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            { started.SetResult(); token.WaitHandle.WaitOne(Limit); token.ThrowIfCancellationRequested(); }
            return EnglishPronunciation.Load(path, token);
        });
        var task = engine.GenerateAsync("hello", Language.English, cancellation.Token);
        await started.Task.WaitAsync(Limit);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Limit));
        Assert.Equal(0, engine.DictionaryLoadCount);
        Assert.Equal("həˈloʊ", (await engine.GenerateAsync("hello", Language.English)).Text);
        Assert.Equal(1, engine.DictionaryLoadCount);
    }

    [Fact]
    public async Task CancellationInQueueDoesNotCancelActiveWorkAndLoadingIsNotOnTheCallerThread()
    {
        using var dictionary = new DictionaryFile();
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var caller = Environment.CurrentManagedThreadId;
        await using var engine = new LocalPronunciationEngine(new Catalog(dictionary.Model), (path, token) =>
        { started.SetResult(Environment.CurrentManagedThreadId); release.Wait(Limit, token); return EnglishPronunciation.Load(path, token); });
        var active = engine.GenerateAsync("hello", Language.English);
        Assert.NotEqual(caller, await started.Task.WaitAsync(Limit));
        var queued = engine.GenerateAsync("hola", Language.Spanish, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        release.Set();
        Assert.Equal("həˈloʊ", (await active.WaitAsync(Limit)).Text);
        Assert.Equal(1, engine.DictionaryLoadCount);
    }

    [Fact]
    public async Task DisposeCancelsActiveWorkReleasesResourcesAndIsIdempotent()
    {
        using var dictionary = new DictionaryFile();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new LocalPronunciationEngine(new Catalog(dictionary.Model), (path, token) =>
        { started.SetResult(); token.WaitHandle.WaitOne(Limit); token.ThrowIfCancellationRequested(); return EnglishPronunciation.Load(path, token); });
        var active = engine.GenerateAsync("hello", Language.English);
        await started.Task.WaitAsync(Limit);
        await engine.DisposeAsync().AsTask().WaitAsync(Limit);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        await engine.DisposeAsync();
        Assert.Equal(0, engine.EnglishWordCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.GenerateAsync("hola", Language.Spanish));
    }

    [Fact]
    public async Task PreCanceledAndOversizedRequestsDoNotInitializeResources()
    {
        await using var engine = new LocalPronunciationEngine(new Catalog(null));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.GenerateAsync("hola", Language.Spanish, new(true)));
        await Assert.ThrowsAsync<TranslationEngineException>(() => engine.GenerateAsync(new('a', 8001), Language.Spanish));
        Assert.Equal(0, engine.SpanishWordCount);
    }

    // This tiny phoneme fixture tests loading and coordination, not real-corpus coverage.
    private sealed class DictionaryFile : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "saylo-pronunciation-" + Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(_directory, "cmudict.dict");
        internal ModelDescriptor Model => new(PronunciationResources.DictionaryId, PronunciationResources.DictionaryVersion,
            ModelPurpose.Pronunciation, ["en"], System.IO.Path.GetDirectoryName(Path)!,
            [new("cmudict.dict", PronunciationResources.DictionarySha256, 3618488), new("LICENSE", PronunciationResources.LicenseSha256, 1754)]);
        internal DictionaryFile(string? data = null)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path, data ?? "hello HH AH0 L OW1\nread R EH1 D\nread(2) R IY1 D\nrecord R EH1 K ER0 D\nrecord(2) R IH0 K AO1 R D\n");
        }
        public void Dispose() { File.Delete(Path); Directory.Delete(_directory); }
    }

    private sealed class Catalog(ModelDescriptor? model) : IModelCatalog
    {
        internal int Locations;
        internal ModelValidationResult Validation { get; init; } = new(ModelAvailability.Available, ModelCompatibility.Compatible, ModelIntegrity.Valid);
        public Task<IReadOnlyList<ModelDescriptor>> GetInstalledModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>(model is null ? [] : [model]);
        public Task<ModelDescriptor?> LocateAsync(string id, string version, CancellationToken cancellationToken = default)
        { Locations++; return Task.FromResult(model); }
        public Task<ModelValidationResult> ValidateAsync(ModelDescriptor descriptor, CancellationToken cancellationToken = default) => Task.FromResult(Validation);
    }
}
