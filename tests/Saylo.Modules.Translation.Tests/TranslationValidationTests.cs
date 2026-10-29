using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.TextTranslation;
using Xunit.Abstractions;

namespace Saylo.Modules.Translation.Tests;

[Collection("ArgosIntegration")]
public sealed class TranslationValidationTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(90);

    private static ArgosTranslationOptions Options(TimeSpan? timeout = null) =>
        new(Environment.GetEnvironmentVariable("SAYLO_ARGOS_PYTHON")!,
            Environment.GetEnvironmentVariable("SAYLO_ARGOS_MODELS")!, translationTimeout: timeout);

    private static string DatasetPath()
    {
        var configured = Environment.GetEnvironmentVariable("SAYLO_VALIDATION_DATASET");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Saylo.slnx")))
                return Path.Combine(directory.FullName, "docs", "validation", "translation-quality-cases.json");
        throw new DirectoryNotFoundException("Configure the public validation dataset.");
    }

    [ArgosIntegrationFact]
    [Trait("Category", "TranslationValidation")]
    public async Task EvaluatesPublicCorpusAndMeasuresRealLatency()
    {
        var datasetBytes = await File.ReadAllBytesAsync(DatasetPath());
        var dataset = JsonSerializer.Deserialize<Dataset>(datasetBytes, JsonOptions)!;
        Assert.Equal(1, dataset.SchemaVersion);
        Assert.Equal(80, dataset.Cases.Length);
        Assert.Equal(80, dataset.Cases.Select(item => item.Id).Distinct().Count());
        Assert.Equal(40, dataset.Cases.Count(item => item.Source == "es" && item.Target == "en"));
        Assert.Equal(40, dataset.Cases.Count(item => item.Source == "en" && item.Target == "es"));
        Assert.All(dataset.Cases, item => Assert.InRange(item.Text.Length, 1, ArgosTranslationOptions.MaximumTextLength));

        var metrics = new ConcurrentQueue<Metric>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Saylo.TextTranslation.Argos")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
            metrics.Enqueue(new(instrument.Name, value, instrument.Unit)));
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            metrics.Enqueue(new(instrument.Name, value, instrument.Unit)));
        listener.Start();

        var options = Options();
        var catalog = ArgosModels.CreateCatalog(options.ModelDirectory);
        var integrity = new List<object>();
        foreach (var model in ArgosModels.ReadDescriptors(options.ModelDirectory))
        {
            var validation = await catalog.ValidateAsync(model);
            Assert.Equal(Saylo.Modules.ModelManagement.Contracts.ModelIntegrity.Valid, validation.Integrity);
            integrity.Add(new { model.Id, model.Version, Files = model.Files.Count, Integrity = validation.Integrity.ToString() });
        }

        var rows = new List<object>();
        var durations = new List<(string Direction, double Ms)>();
        var longDurations = new List<(string Direction, double Ms, int InputUtf16Length, int OutputUtf16Length, int Attempt, string Input, string Output)>();
        var errors = new List<string>();
        var controlledRejections = new List<string>();
        var longRejections = new List<object>();
        var engine = new ArgosTextTranslationEngine(options, catalog);
        int? pid = null;
        try
        {
            foreach (var item in dataset.Cases)
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    var text = await engine.TranslateAsync(item.Text, Language.FromCode(item.Source),
                        Language.FromCode(item.Target)).WaitAsync(Limit);
                    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    pid ??= engine.WorkerProcessId;
                    Assert.Equal(pid, engine.WorkerProcessId);
                    if (rows.Count > 0)
                        durations.Add(($"{item.Source}->{item.Target}", elapsed));
                    var numbersBefore = Numbers(item.Text);
                    var numbersAfter = Numbers(text);
                    var missingLiterals = item.ExactLiterals.Where(literal => !text.Contains(literal, StringComparison.OrdinalIgnoreCase)).ToArray();
                    var negationMarkerFound = item.NegationMarkers.Length == 0 ? (bool?)null :
                        item.NegationMarkers.Any(marker => Regex.IsMatch(text, @"(?i)(?<!\p{L})" + Regex.Escape(marker) + @"(?!\p{L})"));
                    rows.Add(new
                    {
                        item.Id, item.Source, item.Target, Input = item.Text, Output = text, TotalMs = elapsed,
                        Cold = rows.Count == 0, InputUtf16Length = item.Text.Length,
                        AutomatedChecks = new
                        {
                            NumberTokensBefore = numbersBefore, NumberTokensAfter = numbersAfter,
                            NumberTokensPreserved = numbersBefore.SequenceEqual(numbersAfter),
                            MissingExactLiterals = missingLiterals, NegationMarkerFound = negationMarkerFound
                        },
                        HumanBilingualReview = "pending"
                    });
                    output.WriteLine($"{item.Id}: {elapsed:F3} ms");
                }
                catch (TranslationEngineException failure)
                {
                    if (failure.Code == TranslationErrorCode.IntegrityValidationFailed)
                        controlledRejections.Add(item.Id);
                    else
                        errors.Add(item.Id);
                    rows.Add(new { item.Id, Input = item.Text, Error = failure.Code.ToString(),
                        Status = failure.Code == TranslationErrorCode.IntegrityValidationFailed ? "controlled_integrity_rejection" : "unexpected_failure",
                        TotalMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds });
                }
            }

            foreach (var source in new[] { "es", "en" })
            {
                var sentence = source == "es"
                    ? "Su solicitud sigue en revision. No cancele el pedido 12345. Le informaremos por correo cuando termine. "
                    : "Your request is still under review. Do not cancel order 12345. We will email you when it is complete. ";
                var longText = string.Concat(Enumerable.Repeat(sentence, 9));
                Assert.InRange(longText.Length, 800, 1000);
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var started = Stopwatch.GetTimestamp();
                    try
                    {
                        var translated = await engine.TranslateAsync(longText, Language.FromCode(source),
                            Language.FromCode(source == "es" ? "en" : "es")).WaitAsync(Limit);
                        Assert.False(string.IsNullOrWhiteSpace(translated));
                        Assert.Equal(pid, engine.WorkerProcessId);
                        longDurations.Add(($"{source}->{(source == "es" ? "en" : "es")}", Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                            longText.Length, translated.Length, attempt + 1, longText, translated));
                    }
                    catch (TranslationEngineException failure) when (failure.Code == TranslationErrorCode.IntegrityValidationFailed)
                    {
                        longRejections.Add(new { Source = source, Attempt = attempt + 1, Status = "controlled_integrity_rejection",
                            TotalMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds });
                    }
                }
            }
        }
        finally { await engine.DisposeAsync().AsTask().WaitAsync(Limit); }
        if (pid.HasValue)
            await AssertExited(pid.Value);

        using var host = Process.GetCurrentProcess();
        await Save("quality", new
        {
            SchemaVersion = 2, DatasetId = dataset.DatasetId,
            DatasetSha256 = Convert.ToHexString(SHA256.HashData(datasetBytes)).ToLowerInvariant(),
            ExecutedAtUtc = DateTimeOffset.UtcNow,
            Environment = new { OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Dotnet = Environment.Version.ToString(), LogicalProcessors = Environment.ProcessorCount,
                options.Threads, HostWorkingSetBytes = host.WorkingSet64, host.PrivateMemorySize64 },
            Integrity = integrity, WorkerProcessId = pid, WorkerExitedAfterDispose = true,
            EngineMetrics = metrics, Results = rows, Errors = errors, ControlledRejections = controlledRejections,
            LongControlledRejections = longRejections,
            WarmCorpus = durations.GroupBy(item => item.Direction).ToDictionary(group => group.Key, group => Statistics(group.Select(item => item.Ms))),
            WarmLongMessages = longDurations.GroupBy(item => item.Direction).ToDictionary(group => group.Key, group => Statistics(group.Select(item => item.Ms))),
            LongMessageSamples = longDurations.Select(item => new { item.Direction, item.Ms, item.InputUtf16Length,
                item.OutputUtf16Length, item.Attempt, item.Input, item.Output }),
            Notes = new[] { "Sequential wall latency includes IPC and engine overhead.",
                "Nearest-rank p95; cold first request excluded from warm corpus. No SLA.",
                "Number tokens and negation markers are conservative review flags, not semantic certification.",
                "Long messages are performance probes, outside the 80-case linguistic corpus.",
                "Network was not disabled at OS level by this test." }
        });
        Assert.Empty(errors);
    }

    [ArgosIntegrationFact]
    [Trait("Category", "TranslationValidation")]
    public async Task ConcurrentRealRequestsKeepTheirOwnResults()
    {
        var options = Options();
        var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        var fixtures = new[] { "No cancele el pedido 12345.", "Gracias por su paciencia.", "El precio es 25 euros.", "Reinicie el servidor SQL.",
            "Please confirm order 98765.", "Your refund is 80 euros.", "Do not share your password.", "Good afternoon." };
        int? pid = null;
        var elapsed = Stopwatch.StartNew();
        try
        {
            var expected = new List<string>();
            for (var index = 0; index < fixtures.Length; index++)
                expected.Add(await engine.TranslateAsync(fixtures[index], From(index), To(index)).WaitAsync(Limit));
            pid = engine.WorkerProcessId;
            var actual = await Task.WhenAll(fixtures.Select((text, index) =>
                engine.TranslateAsync(text, From(index), To(index)))).WaitAsync(Limit);
            for (var index = 0; index < fixtures.Length; index++)
            {
                var literals = ProtectedText.FindLiterals(fixtures[index], new TranslationIntegrityOptions());
                if (literals.Count == 0)
                    Assert.Equal(expected[index], actual[index]);
                else
                {
                    // Fresh opaque references can change wording; correlation and literal integrity must hold.
                    foreach (var literal in literals)
                        Assert.Contains(literal.Value, actual[index], StringComparison.Ordinal);
                    var positions = literals.Select(literal => new LiteralSpan(actual[index].IndexOf(literal.Value, StringComparison.Ordinal),
                        literal.Length, literal.Value)).ToArray();
                    NumericIntegrity.Validate(fixtures[index], actual[index], new TranslationIntegrityOptions(), positions);
                }
            }
            Assert.Equal(pid, engine.WorkerProcessId);
        }
        finally { await engine.DisposeAsync().AsTask().WaitAsync(Limit); }
        await AssertExited(pid!.Value);
        await Save("concurrency", new { Status = "passed", Requests = 8, BaselineRequests = 8, SingleWorker = true,
            CorrelationCheckedByProductionProtocol = true, WorkerExited = true, ElapsedMs = elapsed.Elapsed.TotalMilliseconds });
    }

    [ArgosIntegrationFact]
    [Trait("Category", "TranslationValidation")]
    public async Task CrashedRealWorkerIsReportedAndNextRequestRecovers()
    {
        var options = Options();
        var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        int? recoveredPid = null;
        var timer = Stopwatch.StartNew();
        int oldPid;
        double recoveryMs;
        try
        {
            await engine.TranslateAsync("Buenos dias.", Language.Spanish, Language.English).WaitAsync(Limit);
            oldPid = engine.WorkerProcessId!.Value;
            using (var worker = Process.GetProcessById(oldPid))
            {
                worker.Kill(entireProcessTree: true);
                await worker.WaitForExitAsync().WaitAsync(Limit);
            }
            var failure = await Assert.ThrowsAsync<TranslationEngineException>(() =>
                engine.TranslateAsync("fixture", Language.Spanish, Language.English).WaitAsync(Limit));
            Assert.Equal(TranslationErrorCode.EngineFailure, failure.Code);
            var restart = Stopwatch.StartNew();
            var translated = await engine.TranslateAsync("Thank you.", Language.English, Language.Spanish).WaitAsync(Limit);
            recoveryMs = restart.Elapsed.TotalMilliseconds;
            Assert.Contains("gracias", translated, StringComparison.OrdinalIgnoreCase);
            recoveredPid = engine.WorkerProcessId;
            Assert.NotEqual(oldPid, recoveredPid);
        }
        finally { await engine.DisposeAsync().AsTask().WaitAsync(Limit); }
        await AssertExited(oldPid);
        await AssertExited(recoveredPid!.Value);
        await Save("crash-recovery", new { Status = "passed", RecoveryMs = recoveryMs, BothWorkersExited = true,
            ElapsedMs = timer.Elapsed.TotalMilliseconds });
    }

    [ArgosIntegrationFact]
    [Trait("Category", "TranslationValidation")]
    public async Task RealTranslationTimeoutReapsTheWorker()
    {
        var options = Options(TimeSpan.FromMilliseconds(1));
        int? pid = null;
        var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory),
            async (settings, token) =>
            {
                var session = await ArgosProcessSession.StartAsync(settings, token);
                pid = session.ProcessId;
                return session;
            });
        var timer = Stopwatch.StartNew();
        try
        {
            var failure = await Assert.ThrowsAsync<TranslationEngineException>(() =>
                engine.TranslateAsync(string.Concat(Enumerable.Repeat("No cancele el pedido 12345. ", 30)),
                    Language.Spanish, Language.English).WaitAsync(Limit));
            Assert.Equal(TranslationErrorCode.EngineFailure, failure.Code);
            Assert.Null(engine.WorkerProcessId);
        }
        finally { await engine.DisposeAsync().AsTask().WaitAsync(Limit); }
        await AssertExited(pid!.Value);
        await Save("timeout", new { Status = "passed", TranslationTimeoutMs = 1, WorkerExited = true,
            TotalIncludingStartupAndReapingMs = timer.Elapsed.TotalMilliseconds });
    }

    [ArgosIntegrationFact]
    [Trait("Category", "TranslationValidation")]
    public async Task CancelingQueuedRealWorkDoesNotInterruptTheActiveRequest()
    {
        var options = Options();
        var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        int? pid = null;
        var timer = Stopwatch.StartNew();
        try
        {
            await engine.TranslateAsync("Buenos dias.", Language.Spanish, Language.English).WaitAsync(Limit);
            pid = engine.WorkerProcessId;
            var active = engine.TranslateAsync(string.Concat(Enumerable.Repeat("No cancele el pedido. ", 40)), Language.Spanish, Language.English);
            using var cancellation = new CancellationTokenSource();
            var queued = engine.TranslateAsync("Thank you.", Language.English, Language.Spanish, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(Limit));
            Assert.False(string.IsNullOrWhiteSpace(await active.WaitAsync(Limit)));
            Assert.Equal(pid, engine.WorkerProcessId);
        }
        finally { await engine.DisposeAsync().AsTask().WaitAsync(Limit); }
        await AssertExited(pid!.Value);
        await Save("queued-cancellation", new { Status = "passed", ActiveRequestCompleted = true, SameWorker = true,
            WorkerExited = true, ElapsedMs = timer.Elapsed.TotalMilliseconds });
    }

    [ArgosIntegrationFact]
    [Trait("Category", "TranslationValidation")]
    public async Task DisposalDuringRealInferenceReapsTheWorker()
    {
        var options = Options();
        var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        int? pid = null;
        var timer = Stopwatch.StartNew();
        try
        {
            await engine.TranslateAsync("Buenos dias.", Language.Spanish, Language.English).WaitAsync(Limit);
            pid = engine.WorkerProcessId;
            var active = engine.TranslateAsync(string.Concat(Enumerable.Repeat("No cancele el pedido. ", 40)), Language.Spanish, Language.English);
            await engine.DisposeAsync().AsTask().WaitAsync(Limit);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active.WaitAsync(Limit));
        }
        finally { await engine.DisposeAsync().AsTask().WaitAsync(Limit); }
        await AssertExited(pid!.Value);
        await Save("disposal", new { Status = "passed", ActiveRequestCanceled = true, WorkerExited = true,
            ElapsedMs = timer.Elapsed.TotalMilliseconds });
    }

    private static Language From(int index) => index < 4 ? Language.Spanish : Language.English;
    private static Language To(int index) => index < 4 ? Language.English : Language.Spanish;
    private static string[] Numbers(string text) => Regex.Matches(text, @"\d+(?:[.,:/-]\d+)*")
        .Select(match => match.Value).Order(StringComparer.Ordinal).ToArray();

    private static object Statistics(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return new { Count = sorted.Length, MinMs = sorted[0],
            MedianMs = sorted.Length % 2 == 0 ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2 : sorted[sorted.Length / 2],
            P95Ms = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1], MaxMs = sorted[^1] };
    }

    private static async Task AssertExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(process.HasExited);
        }
        catch (ArgumentException) { }
    }

    private static async Task Save(string suffix, object evidence)
    {
        var directory = Environment.GetEnvironmentVariable("SAYLO_VALIDATION_REPORT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, $"translation-{suffix}.json"),
            JsonSerializer.Serialize(evidence, JsonOptions));
    }

    private sealed record Dataset(int SchemaVersion, string DatasetId, Fixture[] Cases);
    private sealed record Fixture(string Id, string Source, string Target, string Text, string Reference,
        string[] Tags, string[] ExactLiterals, string[] NegationMarkers);
    private sealed record Metric(string Name, double Value, string? Unit);
}
