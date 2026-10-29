using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text.Json;
using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.TextTranslation;
using Xunit.Abstractions;

namespace Saylo.Modules.Translation.Tests;

[Collection("ArgosIntegration")]
public sealed class TranslationIntegrityIntegrationTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(90);

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Saylo.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("The validation corpus was not found.");
    }

    private static ArgosTranslationOptions Options(TranslationIntegrityOptions? integrity = null) =>
        new(Environment.GetEnvironmentVariable("SAYLO_ARGOS_PYTHON")!,
            Environment.GetEnvironmentVariable("SAYLO_ARGOS_MODELS")!, integrity: integrity);

    private static TranslationIntegrityOptions CustomerPolicy() => new(requireFormalSpanish: true, glossary:
    [
        new("en", "customer support", ["atenci\u00f3n al cliente", "soporte al cliente", "servicio al cliente"]),
        new("en", "water damage", ["da\u00f1os por agua", "da\u00f1o por agua", "da\u00f1os causados por agua", "da\u00f1o causado por agua"]),
        new("es", "escalar", ["escalate", "escalating", "escalated"])
    ]);

    [ArgosIntegrationFact]
    [Trait("Category", "TranslationIntegrityIntegration")]
    public async Task ReplaysHistoricalCorpusAndRecordsAcceptedAndControlledOutcomes()
    {
        var datasetPath = Path.Combine(Root(), "docs", "validation", "translation-quality-cases.json");
        var historyPath = Path.Combine(Root(), "docs", "validation", "results", "2026-10-28", "translation-quality.json");
        var datasetBytes = await File.ReadAllBytesAsync(datasetPath);
        var historyBytes = await File.ReadAllBytesAsync(historyPath);
        using var dataset = JsonDocument.Parse(datasetBytes);
        using var history = JsonDocument.Parse(historyBytes);
        var old = history.RootElement.GetProperty("Results").EnumerateArray()
            .ToDictionary(row => row.GetProperty("Id").GetString()!, row => row.GetProperty("Output").GetString()!);
        var metrics = new ConcurrentQueue<Measurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Saylo.TextTranslation.Argos")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => metrics.Enqueue(new(instrument.Name, value)));
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => metrics.Enqueue(new(instrument.Name, value)));
        listener.Start();

        var options = Options(CustomerPolicy());
        var rows = new List<Outcome>();
        var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        int? pid = null;
        try
        {
            foreach (var fixture in dataset.RootElement.GetProperty("cases").EnumerateArray())
            {
                var id = fixture.GetProperty("id").GetString()!;
                var text = fixture.GetProperty("text").GetString()!;
                var source = fixture.GetProperty("source").GetString()!;
                var target = fixture.GetProperty("target").GetString()!;
                var timer = Stopwatch.StartNew();
                try
                {
                    var translated = await engine.TranslateAsync(text, Language.FromCode(source), Language.FromCode(target)).WaitAsync(Limit);
                    pid ??= engine.WorkerProcessId;
                    Assert.Equal(pid, engine.WorkerProcessId);
                    rows.Add(new(id, source, target, text, old[id], translated, "accepted_by_rules", null, null, timer.Elapsed.TotalMilliseconds));
                }
                catch (TranslationEngineException failure) when (failure.Code == TranslationErrorCode.IntegrityValidationFailed)
                {
                    if (pid.HasValue)
                        Assert.Equal(pid, engine.WorkerProcessId);
                    rows.Add(new(id, source, target, text, old[id], null, "controlled_integrity_rejection", failure.Code.ToString(), failure.Data["IntegrityConstraint"]?.ToString(),
                        timer.Elapsed.TotalMilliseconds));
                }
                output.WriteLine($"{id}: {rows[^1].Status}; {rows[^1].TotalMs:F3} ms");
            }
        }
        finally { await engine.DisposeAsync().AsTask().WaitAsync(Limit); }
        Assert.Equal(80, rows.Count);
        Assert.Equal(40, rows.Count(row => row.Source == "es"));
        foreach (var id in new[] { "es-18", "es-33" })
        {
            var row = rows.Single(row => row.Id == id);
            if (row.Output is not null)
                Assert.Contains(id == "es-18" ? "soporte@example.com" : "ZX987654321ES", row.Output, StringComparison.Ordinal);
        }
        await Save("corpus", new
        {
            SchemaVersion = 1, DatasetSha256 = Hash(datasetBytes), HistoricalResultsSha256 = Hash(historyBytes),
            Policy = "customer-service-validation-v1", Engine = "CTranslate2 4.8.4 CPU/int8", options.Threads,
            FirstTranslationMs = rows[0].TotalMs,
            Accepted = rows.Count(row => row.Output is not null), ControlledRejections = rows.Count(row => row.Output is null),
            Results = rows, Metrics = metrics,
            WarmLatency = rows.Skip(1).GroupBy(row => row.Source + "->" + row.Target)
                .ToDictionary(group => group.Key, group => Stats(group.Select(row => row.TotalMs))),
            WarmAcceptedLatency = rows.Skip(1).Where(row => row.Output is not null).GroupBy(row => row.Source + "->" + row.Target)
                .ToDictionary(group => group.Key, group => Stats(group.Select(row => row.TotalMs))),
            HumanBilingualAcceptance = "pending",
            Note = "Rule acceptance is not semantic certification. Controlled rejections have no completed translation."
        });
    }

    [ArgosIntegrationFact]
    [Trait("Category", "TranslationIntegrityIntegration")]
    public async Task LongRepetitiveMessagesPreserveEveryInstructionOrFailClosed()
    {
        var options = Options();
        var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        var rows = new List<object>();
        try
        {
            foreach (var source in new[] { "es", "en" })
            {
                var sentence = source == "es"
                    ? "Su solicitud sigue en revision. No cancele el pedido 12345. Le informaremos por correo cuando termine. "
                    : "Your request is still under review. Do not cancel order 12345. We will email you when it is complete. ";
                var text = string.Concat(Enumerable.Repeat(sentence, 9));
                var timer = Stopwatch.StartNew();
                try
                {
                    var translated = await engine.TranslateAsync(text, Language.FromCode(source),
                        Language.FromCode(source == "es" ? "en" : "es")).WaitAsync(Limit);
                    Assert.Equal(9, System.Text.RegularExpressions.Regex.Matches(translated, @"\b12345\b").Count);
                    Assert.DoesNotContain("will be closed", translated, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("no ser\u00e1 completada", translated, StringComparison.OrdinalIgnoreCase);
                    rows.Add(new { Source = source, Input = text, Output = translated, Status = "accepted_by_rules", TotalMs = timer.Elapsed.TotalMilliseconds });
                }
                catch (TranslationEngineException failure) when (failure.Code == TranslationErrorCode.IntegrityValidationFailed)
                {
                    rows.Add(new { Source = source, Input = text, Output = (string?)null, Status = "controlled_integrity_rejection", Constraint = failure.Data["IntegrityConstraint"], TotalMs = timer.Elapsed.TotalMilliseconds });
                }
            }
            // Cross-sentence references remain linguistic review items even when deterministic invariants pass.
            const string reference = "Your order AB-123 is ready. It will not be canceled. Please keep this reference.";
            try
            {
                var translated = await engine.TranslateAsync(reference, Language.English, Language.Spanish).WaitAsync(Limit);
                Assert.Contains("AB-123", translated);
                rows.Add(new { Source = "en", Input = reference, Output = translated, Status = "accepted_by_rules_human_reference_review_pending" });
            }
            catch (TranslationEngineException failure) when (failure.Code == TranslationErrorCode.IntegrityValidationFailed)
            {
                rows.Add(new { Source = "en", Input = reference, Status = "controlled_integrity_rejection" });
            }
        }
        finally { await engine.DisposeAsync().AsTask().WaitAsync(Limit); }
        await Save("long-messages", rows);
    }

    [ArgosIntegrationFact]
    [Trait("Category", "TranslationIntegrityIntegration")]
    public async Task RealLiteralProtectionSurvivesConcurrencyAndRecoversFromControlledRejection()
    {
        var options = Options();
        var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        try
        {
            var inputs = new[] { "Please email support@example.com.", "El numero de seguimiento es ZX987654321ES.",
                "Su pedido AB-009871 llegara.", "Please visit https://example.com/help." };
            var results = await Task.WhenAll(inputs.Select(async (text, index) =>
            {
                try
                {
                    var translated = await engine.TranslateAsync(text, Language.FromCode(index is 1 or 2 ? "es" : "en"),
                        Language.FromCode(index is 1 or 2 ? "en" : "es")).WaitAsync(Limit);
                    var literal = new[] { "support@example.com", "ZX987654321ES", "AB-009871", "https://example.com/help" }[index];
                    Assert.Contains(literal, translated, StringComparison.Ordinal);
                    return new { Index = index, Status = "accepted_by_rules", Output = (string?)translated };
                }
                catch (TranslationEngineException failure) when (failure.Code == TranslationErrorCode.IntegrityValidationFailed)
                {
                    return new { Index = index, Status = "controlled_integrity_rejection", Output = (string?)null };
                }
            })).WaitAsync(Limit);
            var pid = engine.WorkerProcessId;
            await Assert.ThrowsAsync<TranslationEngineException>(() =>
                engine.TranslateAsync("La fecha es 03/04/2026.", Language.Spanish, Language.English));
            Assert.Equal(pid, engine.WorkerProcessId);
            Assert.Contains("gracias", await engine.TranslateAsync("Thank you.", Language.English, Language.Spanish), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(pid, engine.WorkerProcessId);
            await Save("literal-concurrency", new { Results = results, ReusedAfterRejection = true });
        }
        finally { await engine.DisposeAsync().AsTask().WaitAsync(Limit); }
    }

[ArgosIntegrationFact]
    [Trait("Category", "TranslationIntegrityIntegration")]
    public async Task MeasuresProtectionAndSegmentationAgainstTheSameRawWorker()
    {
        var options = Options();
        var measurements = new ConcurrentQueue<Measurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Saylo.TextTranslation.Argos")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => measurements.Enqueue(new(instrument.Name, value)));
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => measurements.Enqueue(new(instrument.Name, value)));
        listener.Start();

        // Raw IPC is a test-only baseline. Production has no switch to disable integrity.
        var rawStartup = Stopwatch.StartNew();
        var raw = await ArgosProcessSession.StartAsync(options, CancellationToken.None).WaitAsync(Limit);
        var rawStartupMs = rawStartup.Elapsed.TotalMilliseconds;
        var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        var probes = new List<PerformanceProbe>();
        var first = Stopwatch.StartNew();
        try
        {
            await engine.TranslateAsync("Thank you.", Language.English, Language.Spanish).WaitAsync(Limit);
            var firstMs = first.Elapsed.TotalMilliseconds;
            var cases = new[]
            {
                new { Kind = "plain-short", Source = "en", Text = "Thank you for contacting us.", Repeats = 7 },
                new { Kind = "email", Source = "es", Text = "Por favor, envie los documentos a soporte@example.com.", Repeats = 7 },
                new { Kind = "tracking", Source = "es", Text = "El numero de seguimiento es ZX987654321ES.", Repeats = 7 },
                new { Kind = "two-sentences", Source = "en", Text = "Your order AB-123 is ready. Do not cancel it.", Repeats = 7 },
                new { Kind = "long-repeated", Source = "en",
                    Text = string.Concat(Enumerable.Repeat("Your request is still under review. Do not cancel order 12345. We will email you when it is complete. ", 9)), Repeats = 3 }
            };
            var memory = new List<long>();
            foreach (var fixture in cases)
            {
                for (var attempt = 1; attempt <= fixture.Repeats; attempt++)
                {
                    var target = fixture.Source == "en" ? "es" : "en";
                    var timer = Stopwatch.StartNew();
                    var rawOutput = await raw.TranslateAsync(Guid.NewGuid().ToString("N"), fixture.Text,
                        fixture.Source, target, CancellationToken.None).WaitAsync(Limit);
                    var rawMs = timer.Elapsed.TotalMilliseconds;
                    var offset = measurements.Count;
                    timer.Restart();
                    string? translated = null;
                    var status = "accepted_by_rules";
                    try
                    {
                        translated = await engine.TranslateAsync(fixture.Text, Language.FromCode(fixture.Source),
                            Language.FromCode(target)).WaitAsync(Limit);
                    }
                    catch (TranslationEngineException failure) when (failure.Code == TranslationErrorCode.IntegrityValidationFailed)
                    {
                        status = "controlled_integrity_rejection";
                    }
                    var protectedMs = timer.Elapsed.TotalMilliseconds;
                    var events = measurements.Skip(offset).ToArray();
                    using var worker = Process.GetProcessById(engine.WorkerProcessId!.Value);
                    memory.Add(worker.WorkingSet64);
                    probes.Add(new(fixture.Kind, attempt, fixture.Text, rawOutput, translated, status, rawMs, protectedMs,
                        events.Where(item => item.Name == "saylo.argos.integrity_preparation").Sum(item => item.Value),
                        events.Where(item => item.Name == "saylo.argos.integrity_validation").Sum(item => item.Value),
                        (int)events.Last(item => item.Name == "saylo.argos.segment_count").Value));
                }
            }
            await Save("performance", new
            {
                SchemaVersion = 1, FirstProtectedTranslationMs = firstMs, RawWorkerStartupWithoutCatalogMs = rawStartupMs,
                Policy = "default-integrity", Probes = probes,
                ByKind = probes.GroupBy(probe => probe.Kind).ToDictionary(group => group.Key, group => new
                {
                    Raw = Stats(group.Select(probe => probe.RawMs)),
                    Protected = Stats(group.Select(probe => probe.ProtectedMs)),
                    Preparation = Stats(group.Select(probe => probe.PreparationMs)),
                    Validation = Stats(group.Select(probe => probe.ValidationMs)),
                    Accepted = group.Count(probe => probe.Status == "accepted_by_rules")
                }),
                ProtectedWorkingSetMinBytes = memory.Min(), ProtectedWorkingSetMaxBytes = memory.Max(),
                RawWorkingSetBytes = raw.WorkingSetBytes,
                Note = "Same models, CPU and worker code. Raw path is unsafe test baseline only; no production bypass. One PC, not SLA. Long n=3."
            });
        }
        finally
        {
            await engine.DisposeAsync().AsTask().WaitAsync(Limit);
            await raw.DisposeAsync().AsTask().WaitAsync(Limit);
        }
    }

    private sealed record PerformanceProbe(string Kind, int Attempt, string Input, string RawOutput, string? Output,
        string Status, double RawMs, double ProtectedMs, double PreparationMs, double ValidationMs, int Segments);

[ArgosIntegrationFact]
    [Trait("Category", "TranslationIntegrityIntegration")]
    public async Task RepeatedRealIdentifiersKeepAllOccurrencesOrReturnAnIntegrityFailure()
    {
        var options = Options();
        var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        const string text = "Email support@example.com, then email support@example.com.";
        try
        {
            string? translated = null;
            var status = "accepted_by_rules";
            try
            {
                translated = await engine.TranslateAsync(text, Language.English, Language.Spanish).WaitAsync(Limit);
                Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(translated, @"support@example\.com").Count);
            }
            catch (TranslationEngineException failure) when (failure.Code == TranslationErrorCode.IntegrityValidationFailed)
            {
                status = "controlled_integrity_rejection";
            }
            var pid = engine.WorkerProcessId;
            Assert.Contains("gracias", await engine.TranslateAsync("Thank you.", Language.English, Language.Spanish), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(pid, engine.WorkerProcessId);
            await Save("repeated-identifiers", new { Input = text, Output = translated, Status = status, SameWorkerAfterNextRequest = true });
        }
        finally { await engine.DisposeAsync().AsTask().WaitAsync(Limit); }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static object Stats(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return new { Count = sorted.Length, MinMs = sorted[0], MedianMs = sorted.Length % 2 == 0
            ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2 : sorted[sorted.Length / 2],
            P95Ms = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1], MaxMs = sorted[^1] };
    }

    private static async Task Save(string suffix, object value)
    {
        var directory = Environment.GetEnvironmentVariable("SAYLO_INTEGRITY_REPORT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, $"integrity-{suffix}.json"), JsonSerializer.Serialize(value, JsonOptions));
    }

    private sealed record Outcome(string Id, string Source, string Target, string Input, string PreviousOutput,
        string? Output, string Status, string? Error, string? Constraint, double TotalMs);
    private sealed record Measurement(string Name, double Value);
}
