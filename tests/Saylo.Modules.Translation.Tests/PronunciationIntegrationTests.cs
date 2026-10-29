using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text.Json;
using Saylo.Modules.ModelManagement;
using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.Pronunciation;
using Saylo.Modules.Translation.SpeechRecognition;
using Saylo.Modules.Translation.TextTranslation;

namespace Saylo.Modules.Translation.Tests;

[Collection("ArgosIntegration")]
public sealed class PronunciationIntegrationTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static LocalPronunciationEngine Engine() => new(PronunciationResources.CreateCatalog(Environment.GetEnvironmentVariable("SAYLO_CMUDICT_DIR")!));

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Saylo.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }

    [PronunciationIntegrationFact]
    [Trait("Category", "PronunciationIntegration")]
    public async Task EvaluatesRealDictionaryAndSpanishRulesAndMeasuresReusedResources()
    {
        var metrics = new ConcurrentQueue<Metric>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == "Saylo.Pronunciation.Local") current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => metrics.Enqueue(new(instrument.Name, value)));
        listener.Start();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var before = new { ManagedBytes = GC.GetTotalMemory(false), WorkingSetBytes = process.WorkingSet64 };
        var constructor = Stopwatch.StartNew();
        await using var engine = Engine();
        var constructorMs = constructor.Elapsed.TotalMilliseconds;
        var first = Stopwatch.StartNew();
        Assert.Equal("ˈθæŋk ˈju.", (await engine.GenerateAsync("Thank you.", Language.English).WaitAsync(Limit)).Text);
        var firstMs = first.Elapsed.TotalMilliseconds;
        var dataset = await File.ReadAllBytesAsync(Path.Combine(Root(), "docs/validation/pronunciation-cases.json"));
        using var document = JsonDocument.Parse(dataset);
        var cases = document.RootElement.GetProperty("cases").Deserialize<Fixture[]>(JsonOptions)!;
        var rows = new List<Row>();
        foreach (var fixture in cases)
        {
            var timer = Stopwatch.StartNew();
            PronunciationResult? result = null;
            string? reason = null;
            TranslationErrorCode? code = null;
            try { result = await engine.GenerateAsync(fixture.Text, Language.FromCode(fixture.Language)).WaitAsync(Limit); }
            catch (TranslationEngineException failure)
            { reason = failure.Data["PronunciationReason"]?.ToString(); code = failure.Code; }
            rows.Add(new(fixture.Id, fixture.Language, fixture.Category, fixture.Text, result?.Text,
                result?.Notation.ToString(), result is null ? "controlled_pronunciation_rejection" : "generated_not_human_validated",
                reason, code?.ToString(), timer.Elapsed.TotalMilliseconds));
        }
        foreach (var id in new[] { "en-01", "en-02", "en-03", "en-07", "en-08", "en-09", "en-10", "es-01", "es-04", "es-05", "es-09", "es-10", "es-11", "es-12", "es-23", "es-24" })
            Assert.NotNull(rows.Single(row => row.Id == id).Ipa);
        foreach (var id in new[] { "en-11", "en-12", "en-13", "en-14", "en-15", "en-16", "en-17", "en-18", "en-19", "en-20", "en-21", "es-13", "es-15", "es-16", "es-17", "es-18", "es-19", "es-20", "es-21", "es-22" })
            Assert.Null(rows.Single(row => row.Id == id).Ipa);
        Assert.Contains("ˈɹɛd", rows.Single(row => row.Id == "en-08").Ipa);
        Assert.Contains("ˈɹid", rows.Single(row => row.Id == "en-09").Ipa);
        Assert.NotEqual(rows.Single(row => row.Id == "es-09").Ipa, rows.Single(row => row.Id == "es-10").Ipa);
        Assert.NotEqual(rows.Single(row => row.Id == "es-10").Ipa, rows.Single(row => row.Id == "es-11").Ipa);

        var performance = new List<Sample>();
        foreach (var language in new[] { Language.English, Language.Spanish })
        {
            var shortText = language == Language.English ? "Thank you for contacting customer support." : "Gracias por contactar con nosotros.";
            var longText = string.Concat(Enumerable.Repeat(language == Language.English
                ? "Your request has been processed successfully. Please do not cancel the order. Thank you for contacting customer support. "
                : "Su solicitud está en revisión. No cancele el pedido. Gracias por contactar con nosotros. ", 12));
            foreach (var item in new[] { (Kind: "short", Text: shortText, Count: 40), (Kind: "long", Text: longText, Count: 15) })
                for (var attempt = 1; attempt <= item.Count; attempt++)
                {
                    var timer = Stopwatch.StartNew();
                    var result = await engine.GenerateAsync(item.Text, language).WaitAsync(Limit);
                    Assert.Equal(PronunciationNotation.Ipa, result.Notation);
                    performance.Add(new(language.Code, item.Kind, attempt, item.Text.Length, result.Text.Length, timer.Elapsed.TotalMilliseconds));
                }
        }
        var concurrent = Stopwatch.StartNew();
        var jobs = Enumerable.Range(0, 24).Select(index => engine.GenerateAsync(index % 2 == 0 ? "Thank you." : "Gracias.",
            index % 2 == 0 ? Language.English : Language.Spanish)).ToArray();
        var results = await Task.WhenAll(jobs).WaitAsync(Limit);
        var concurrentMs = concurrent.Elapsed.TotalMilliseconds;
        for (var index = 0; index < results.Length; index++)
            Assert.Equal(index % 2 == 0 ? "ˈθæŋk ˈju." : "ˈɡɾasjas.", results[index].Text);
        Assert.Equal(1, engine.DictionaryLoadCount);
        Assert.True(engine.EnglishWordCount > 120_000);
        var englishCount = engine.EnglishWordCount;
        var spanishCount = engine.SpanishWordCount;
        process.Refresh();
        var after = new { ManagedBytes = GC.GetTotalMemory(false), WorkingSetBytes = process.WorkingSet64 };
        await engine.DisposeAsync();
        Assert.Equal(0, engine.EnglishWordCount);
        Assert.Equal(0, engine.SpanishWordCount);
        await Save("quality-and-performance", new
        {
            SchemaVersion = 1, Engine = "Saylo managed broad IPA + CMUdict", DictionaryVersion = PronunciationResources.DictionaryVersion,
            DatasetSha256 = Convert.ToHexString(SHA256.HashData(dataset)).ToLowerInvariant(),
            Notation = "Ipa", EnglishVariant = "en-US citation", SpanishVariant = "seseante yeista broad Spanish",
            ConstructorMs = constructorMs, FirstEnglishGenerationMs = firstMs, BeforeMemory = before, AfterMemory = after,
            EnglishWords = englishCount, SpanishWords = spanishCount, DictionaryLoads = 1,
            Results = rows, Generated = rows.Count(row => row.Ipa is not null), ControlledRejections = rows.Count(row => row.Ipa is null),
            PerformanceSamples = performance, Warm = performance.GroupBy(row => row.Language + "/" + row.Kind)
                .ToDictionary(group => group.Key, group => Stats(group.Select(row => row.TotalMs))),
            ConcurrentRequests = 24, ConcurrentBatchMs = concurrentMs, Metrics = metrics, ResourcesReleased = true,
            HumanLinguisticAcceptance = "pending", EnterpriseAcceptance = false,
            MemoryScope = "Host .NET test process snapshots, includes runtime and prior tests; not isolated engine peak memory. Dictionary references released, GC timing not forced."
        });
    }

    [PronunciationIntegrationFact]
    [Trait("Category", "PronunciationIntegration")]
    public async Task CancelsRealDictionaryParsingAndRecoversWithTheSameEngine()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = PronunciationResources.CreateCatalog(Environment.GetEnvironmentVariable("SAYLO_CMUDICT_DIR")!);
        var calls = 0;
        await using var engine = new LocalPronunciationEngine(catalog, (path, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) started.SetResult();
            return EnglishPronunciation.Load(path, token, PronunciationResources.DictionarySha256);
        });
        using var cancellation = new CancellationTokenSource();
        var task = engine.GenerateAsync("Thank you.", Language.English, cancellation.Token);
        await started.Task.WaitAsync(Limit);
        var timer = Stopwatch.StartNew();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Limit));
        var canceledMs = timer.Elapsed.TotalMilliseconds;
        Assert.Equal(0, engine.DictionaryLoadCount);
        var recovery = Stopwatch.StartNew();
        Assert.Equal("ˈθæŋk ˈju.", (await engine.GenerateAsync("Thank you.", Language.English).WaitAsync(Limit)).Text);
        var recoveryMs = recovery.Elapsed.TotalMilliseconds;
        Assert.Equal(1, engine.DictionaryLoadCount);
        using var preCanceled = new CancellationTokenSource();
        preCanceled.Cancel();
        timer.Restart();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.GenerateAsync("Thank you.", Language.English, preCanceled.Token));
        await Save("cancellation", new { CanceledLocalLoad = true, CancellationToCompletionMs = canceledMs,
            RecoveryMs = recoveryMs, PreCanceledMs = timer.Elapsed.TotalMilliseconds, PartialResultEmitted = false,
            Note = "Real pinned dictionary loader, signaled immediately before reading its file; no artificial sleep or phoneme backend." });
    }

    [PronunciationPipelineFact]
    [Trait("Category", "PronunciationPipelineIntegration")]
    public async Task RealTextAndVoicePipelinesDeliverTextBeforeIndependentPronunciation()
    {
        var options = new ArgosTranslationOptions(Environment.GetEnvironmentVariable("SAYLO_ARGOS_PYTHON")!,
            Environment.GetEnvironmentVariable("SAYLO_ARGOS_MODELS")!);
        await using var translation = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        await using var pronunciation = Engine();
        await using var recognition = new WhisperSpeechRecognitionEngine(new LocalModelCatalog(
            [WhisperModels.BaseMultilingual(Environment.GetEnvironmentVariable("SAYLO_WHISPER_MODEL_DIR")!)]));
        var service = new TranslationService(recognition, translation, pronunciation, new UnavailableSynthesis());
        var evidence = new List<object>();
        foreach (var voice in new[] { false, true })
        {
            var audio = voice ? new AudioData(await File.ReadAllBytesAsync(Environment.GetEnvironmentVariable("SAYLO_WHISPER_EN_PCM")!), AudioFormat.Pcm16, 16000, 1) : null;
            var stream = voice ? service.TranslateVoiceAsync(new(audio!, Language.English, Language.Spanish))
                : service.TranslateTextAsync(new("Buenos días. ¿En qué puedo ayudarle hoy?", Language.Spanish, Language.English));
            var rows = new List<object>();
            var timer = Stopwatch.StartNew();
            var updates = new List<TranslationUpdate>();
            await using var enumerator = stream.GetAsyncEnumerator();
            while (await enumerator.MoveNextAsync().AsTask().WaitAsync(Limit))
            {
                var update = enumerator.Current;
                updates.Add(update);
                rows.Add(new { Stage = update.GetType().Name, Code = (update as TranslationStageFailed)?.Code.ToString(), ElapsedMs = timer.Elapsed.TotalMilliseconds,
                    Text = update is TranscriptionCompleted transcript ? transcript.Text : update is TextTranslationCompleted written ? written.TranslatedText
                        : update is PronunciationCompleted guide ? guide.Pronunciation.Text : null });
                if (update is TextTranslationCompleted && !voice)
                    Assert.Equal(0, pronunciation.DictionaryLoadCount);
            }
            var translatedIndex = updates.FindIndex(update => update is TextTranslationCompleted);
            var pronunciationIndex = updates.FindIndex(update => update is PronunciationCompleted);
            Assert.True(translatedIndex >= 0 && pronunciationIndex > translatedIndex, JsonSerializer.Serialize(rows));
            Assert.IsType<PronunciationCompleted>(updates[pronunciationIndex]);
            Assert.DoesNotContain(updates.OfType<TranslationStageFailed>(), failure => failure.Stage == TranslationStage.Pronunciation);
            Assert.Contains(updates.OfType<TranslationStageFailed>(), failure => failure.Stage == TranslationStage.SpeechSynthesis && failure.Code == TranslationErrorCode.ModelUnavailable);
            Assert.Empty(updates.OfType<SpeechSynthesisCompleted>());
            Assert.All(updates, update => Assert.Equal(updates[0].OperationId, update.OperationId));
            evidence.Add(new { InputKind = voice ? "voice" : "text", Updates = rows, TextBeforePronunciation = true });
        }
        var pid = translation.WorkerProcessId;
        await translation.DisposeAsync();
        if (pid.HasValue)
            try { using var process = Process.GetProcessById(pid.Value); Assert.True(process.HasExited); }
            catch (ArgumentException) { }
        await Save("real-pipeline", new { Results = evidence, TtsImplemented = false, ArgosWorkerExited = true,
            HumanLinguisticAcceptance = "pending", Note = "TTS fixture reports ModelUnavailable; no synthesis implementation or audio generated." });
    }

    private static object Stats(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return new { Count = sorted.Length, MedianMs = sorted.Length % 2 == 0 ?
            (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2 : sorted[sorted.Length / 2],
            P95Ms = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], MinMs = sorted[0], MaxMs = sorted[^1] };
    }
    private static async Task Save(string name, object report)
    {
        var directory = Environment.GetEnvironmentVariable("SAYLO_PRONUNCIATION_REPORT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(report, JsonOptions));
    }
    private sealed record Fixture(string Id, string Language, string Category, string Text);
    private sealed record Row(string Id, string Language, string Category, string Input, string? Ipa, string? Notation,
        string Status, string? Reason, string? ErrorCode, double TotalMs);
    private sealed record Sample(string Language, string Kind, int Attempt, int InputLength, int OutputLength, double TotalMs);
    private sealed record Metric(string Name, double Value);
    private sealed class UnavailableSynthesis : ISpeechSynthesisEngine
    {
        public Task<AudioData> SynthesizeAsync(string text, Language language, CancellationToken token) =>
            throw new TranslationEngineException(TranslationStage.SpeechSynthesis, TranslationErrorCode.ModelUnavailable, "No synthesis engine configured.");
    }
}

public sealed class PronunciationIntegrationFactAttribute : FactAttribute
{
    public PronunciationIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SAYLO_CMUDICT_DIR")))
            Skip = "Configure the pinned local CMUdict directory; see docs/local-pronunciation-engine.md.";
    }
}
public sealed class PronunciationPipelineFactAttribute : FactAttribute
{
    public PronunciationPipelineFactAttribute()
    {
        if (new[] { "SAYLO_CMUDICT_DIR", "SAYLO_ARGOS_PYTHON", "SAYLO_ARGOS_MODELS", "SAYLO_WHISPER_MODEL_DIR", "SAYLO_WHISPER_EN_PCM" }
            .Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Configure the local pronunciation, Argos and Whisper resources for the real progressive pipeline.";
    }
}
