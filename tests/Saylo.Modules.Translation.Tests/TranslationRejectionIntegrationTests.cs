using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.TextTranslation;
using Xunit.Abstractions;

namespace Saylo.Modules.Translation.Tests;

[Collection("ArgosIntegration")]
public sealed class TranslationRejectionIntegrationTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(90);

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Saylo.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("The authorized evaluation corpus was not found.");
    }

    private static ArgosTranslationOptions Options(TranslationIntegrityOptions? policy = null) =>
        new(Environment.GetEnvironmentVariable("SAYLO_ARGOS_PYTHON")!,
            Environment.GetEnvironmentVariable("SAYLO_ARGOS_MODELS")!, integrity: policy);

    private static TranslationIntegrityOptions PreviousPolicy() => new(requireFormalSpanish: true, glossary:
    [
        new("en", "customer support", ["atenci\u00f3n al cliente", "soporte al cliente", "servicio al cliente"]),
        new("en", "water damage", ["da\u00f1os por agua", "da\u00f1o por agua", "da\u00f1os causados por agua", "da\u00f1o causado por agua"]),
        new("es", "escalar", ["escalate", "escalating", "escalated"])
    ]);

    [ArgosIntegrationFact]
    [Trait("Category", "TranslationRejectionIntegration")]
    public async Task ReplaysOriginalCorpusWithThePreviousAndInitialBusinessProfiles()
    {
        var datasetPath = Path.Combine(Root(), "docs/validation/translation-quality-cases.json");
        var comparisonPath = Path.Combine(Root(), "docs/validation/integrity-results/2026-10-28/integrity-comparison.json");
        var datasetBytes = await File.ReadAllBytesAsync(datasetPath);
        var comparisonBytes = await File.ReadAllBytesAsync(comparisonPath);
        using var dataset = JsonDocument.Parse(datasetBytes);
        using var previous = JsonDocument.Parse(comparisonBytes);
        var cases = dataset.RootElement.GetProperty("cases").Deserialize<Fixture[]>(JsonOptions)!;
        var baseline = previous.RootElement.GetProperty("results").EnumerateArray().ToDictionary(
            row => row.GetProperty("id").GetString()!, row => row.GetProperty("newOutput").GetString());
        Assert.Equal(80, cases.Length);
        Assert.Equal(12, baseline.Values.Count(value => value is null));

        foreach (var profile in new[]
                 {
                     (Name: "previous-profile-v1", Policy: PreviousPolicy()),
                     (Name: CustomerServiceTranslationProfile.Id, Policy: CustomerServiceTranslationProfile.Create())
                 })
        {
            var measurements = new ConcurrentQueue<Metric>();
            using var listener = Listener(measurements);
            var rows = new List<Row>();
            var activeId = "";
            var activeText = "";
            RecordingSession? session = null;
            var options = Options(profile.Policy);
            var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory), async (settings, token) =>
            {
                var native = await Task.Run(() => ArgosProcessSession.StartAsync(settings, token), token);
                session = new(native, () => (activeId, activeText), id => baseline[id] is null);
                return session;
            });
            int? pid = null;
            try
            {
                foreach (var fixture in cases)
                {
                    activeId = fixture.Id;
                    activeText = fixture.Text;
                    var timer = Stopwatch.StartNew();
                    string? translated = null;
                    string? constraint = null;
                    try
                    {
                        translated = await engine.TranslateAsync(fixture.Text, Language.FromCode(fixture.Source),
                            Language.FromCode(fixture.Target)).WaitAsync(Limit);
                        pid ??= engine.WorkerProcessId;
                    }
                    catch (TranslationEngineException failure) when (failure.Code == TranslationErrorCode.IntegrityValidationFailed)
                    {
                        constraint = failure.Data["IntegrityConstraint"]?.ToString();
                    }
                    if (pid.HasValue)
                        Assert.Equal(pid, engine.WorkerProcessId);
                    if (translated is not null)
                    {
                        foreach (var literal in ProtectedText.FindLiterals(fixture.Text, options.Integrity).Select(span => span.Value).Distinct())
                            Assert.Equal(Regex.Matches(fixture.Text, Regex.Escape(literal)).Count, Regex.Matches(translated, Regex.Escape(literal)).Count);
                    }
                    rows.Add(new(fixture.Id, fixture.Source, fixture.Target, fixture.Text, baseline[fixture.Id], translated,
                        translated is null ? "controlled_integrity_rejection" : "accepted_by_rules", constraint, timer.Elapsed.TotalMilliseconds));
                    output.WriteLine($"{profile.Name}/{fixture.Id}: {rows[^1].Status}; {rows[^1].TotalMs:F3} ms");
                }
            }
            finally { await engine.DisposeAsync().AsTask().WaitAsync(Limit); }
            if (pid.HasValue)
                AssertExited(pid.Value);

            if (profile.Name == "previous-profile-v1")
            {
                foreach (var id in new[] { "es-04", "es-28", "es-40", "en-04" })
                    Assert.NotNull(rows.Single(row => row.Id == id).Output);
            }
            foreach (var id in new[] { "es-10", "es-17", "es-34", "es-12", "en-12", "es-38", "en-02", "en-37" })
                Assert.Null(rows.Single(row => row.Id == id).Output);
            var currency = rows.Single(row => row.Id == "es-40");
            if (currency.Output is not null)
                NumericIntegrity.Validate(currency.Input, currency.Output, profile.Policy);

            await Save(profile.Name + "-corpus", new
            {
                SchemaVersion = 1, Profile = profile.Name, Policy = profile.Policy,
                DatasetSha256 = Hash(datasetBytes), PreviousComparisonSha256 = Hash(comparisonBytes),
                Accepted = rows.Count(row => row.Output is not null), Rejected = rows.Count(row => row.Output is null),
                Recovered = rows.Where(row => row.PreviousOutput is null && row.Output is not null).Select(row => row.Id).ToArray(),
                NewlyRejected = rows.Where(row => row.PreviousOutput is not null && row.Output is null).Select(row => row.Id).ToArray(),
                FirstTranslationMs = rows[0].TotalMs, Results = rows, Metrics = measurements,
                WarmAll = rows.Skip(1).GroupBy(row => row.Source + "->" + row.Target)
                    .ToDictionary(group => group.Key, group => Stats(group.Select(row => row.TotalMs))),
                WarmAccepted = rows.Skip(1).Where(row => row.Output is not null).GroupBy(row => row.Source + "->" + row.Target)
                    .ToDictionary(group => group.Key, group => Stats(group.Select(row => row.TotalMs))),
                RejectionDiagnostics = session?.Diagnostics,
                Note = "Diagnostics contain only authorized old rejected fixtures whose source was not masked. No production logging hook.",
                HumanBilingualAcceptance = "pending", WorkerExited = true
            });
        }
    }

    [ArgosIntegrationFact]
    [Trait("Category", "TranslationRejectionIntegration")]
    public async Task MeasuresRepeatedAndDistinctLongMessagesWithoutPublishingPartialResults()
    {
        var measurements = new ConcurrentQueue<Metric>();
        using var listener = Listener(measurements);
        var rows = new List<object>();
        var options = Options();
        var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        int? pid = null;
        try
        {
            await engine.TranslateAsync("Thank you.", Language.English, Language.Spanish).WaitAsync(Limit);
            pid = engine.WorkerProcessId;
            foreach (var source in new[] { "es", "en" })
            {
                var sentence = source == "es"
                    ? "Su solicitud sigue en revision. No cancele el pedido 12345. Le informaremos por correo cuando termine. "
                    : "Your request is still under review. Do not cancel order 12345. We will email you when it is complete. ";
                var text = string.Concat(Enumerable.Repeat(sentence, 9));
                for (var attempt = 1; attempt <= 5; attempt++)
                {
                    var offset = measurements.Count;
                    var timer = Stopwatch.StartNew();
                    string? translated = null;
                    string? constraint = null;
                    try
                    {
                        translated = await engine.TranslateAsync(text, Language.FromCode(source),
                            Language.FromCode(source == "es" ? "en" : "es")).WaitAsync(Limit);
                        Assert.Equal(9, Regex.Matches(translated, @"\b12345\b").Count);
                        Assert.DoesNotContain("will be closed", translated, StringComparison.OrdinalIgnoreCase);
                        Assert.DoesNotContain("no ser\u00e1 completada", translated, StringComparison.OrdinalIgnoreCase);
                        Assert.DoesNotContain("I'm through", translated, StringComparison.OrdinalIgnoreCase);
                    }
                    catch (TranslationEngineException failure) when (failure.Code == TranslationErrorCode.IntegrityValidationFailed)
                    {
                        constraint = failure.Data["IntegrityConstraint"]?.ToString();
                    }
                    rows.Add(new { Kind = "repeated", Source = source, Attempt = attempt, Input = text, Output = translated,
                        Status = translated is null ? "controlled_integrity_rejection" : "accepted_by_rules",
                        Constraint = constraint, TotalMs = timer.Elapsed.TotalMilliseconds,
                        Metrics = measurements.Skip(offset).ToArray() });
                }
            }
            foreach (var text in new[]
            {
                "Your order AB-123 is ready. It will not be canceled. Please keep this reference.",
                "Do not cancel order AB-123. The amount is 49.95 USD. Please confirm 3 units of model X200.",
                "Please send the documents to support@example.com. Do not share code 004812."
            })
            {
                var timer = Stopwatch.StartNew();
                try
                {
                    var translated = await engine.TranslateAsync(text, Language.English, Language.Spanish).WaitAsync(Limit);
                    rows.Add(new { Kind = "distinct-references", Input = text, Output = translated, Status = "accepted_by_rules",
                        TotalMs = timer.Elapsed.TotalMilliseconds, HumanBilingualReview = "pending" });
                }
                catch (TranslationEngineException failure) when (failure.Code == TranslationErrorCode.IntegrityValidationFailed)
                {
                    rows.Add(new { Kind = "distinct-references", Input = text, Output = (string?)null,
                        Status = "controlled_integrity_rejection", TotalMs = timer.Elapsed.TotalMilliseconds });
                }
            }
        }
        finally { await engine.DisposeAsync().AsTask().WaitAsync(Limit); }
        AssertExited(pid!.Value);
        await Save("long-messages", new { SchemaVersion = 1, Policy = "default-integrity", Results = rows,
            Note = "Only exact prepared source is reused within this request. No persistent or cross-request cache.", WorkerExited = true });
    }

    [ArgosIntegrationFact]
    [Trait("Category", "TranslationRejectionIntegration")]
    public async Task ChecksRealIdentifiersAndTheBusinessProfileWithoutRelaxingRestoration()
    {
        var options = Options(CustomerServiceTranslationProfile.Create());
        await using var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        var rows = new List<object>();
        foreach (var text in new[]
        {
            "Please send the documents to support@example.com and copy support@example.com.",
            "Open https://example.com/orders/AB-123?ref=004812 and https://example.com/help.",
            "Your tracking number is ZX987654321ES and your order code is AB-009871.",
            "Please call +1 (212) 555-0199 and keep reference XY-004812.",
            "Your request is still under review. Do not cancel order 12345. We will email you when it is complete.",
            "Le informaremos por correo cuando termine."
        })
        {
            var source = text.StartsWith("Le ", StringComparison.Ordinal) ? Language.Spanish : Language.English;
            var timer = Stopwatch.StartNew();
            string? translated = null;
            string? constraint = null;
            try
            {
                translated = await engine.TranslateAsync(text, source, source == Language.English ? Language.Spanish : Language.English).WaitAsync(Limit);
                foreach (var literal in ProtectedText.FindLiterals(text, options.Integrity).Select(span => span.Value).Distinct())
                    Assert.Equal(Regex.Matches(text, Regex.Escape(literal)).Count, Regex.Matches(translated, Regex.Escape(literal)).Count);
                Assert.DoesNotContain("I'm through", translated, StringComparison.OrdinalIgnoreCase);
                if (source == Language.English)
                    Assert.DoesNotMatch(@"\b(tu|te)\b", translated);
            }
            catch (TranslationEngineException failure) when (failure.Code == TranslationErrorCode.IntegrityValidationFailed)
            {
                constraint = failure.Data["IntegrityConstraint"]?.ToString();
            }
            rows.Add(new { Input = text, Source = source.Code, Output = translated, Constraint = constraint,
                Status = translated is null ? "controlled_integrity_rejection" : "accepted_by_rules", TotalMs = timer.Elapsed.TotalMilliseconds });
        }
        var pid = engine.WorkerProcessId;
        await engine.DisposeAsync();
        AssertExited(pid!.Value);
        await Save("identifier-and-business-probes", new { Profile = CustomerServiceTranslationProfile.Id, Results = rows,
            HumanBilingualAcceptance = "pending", WorkerExited = true });
    }

    private static MeterListener Listener(ConcurrentQueue<Metric> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Saylo.TextTranslation.Argos")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => measurements.Enqueue(new(instrument.Name, value)));
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => measurements.Enqueue(new(instrument.Name, value)));
        listener.Start();
        return listener;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static object Stats(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return new { Count = sorted.Length, MinMs = sorted[0], MedianMs = sorted.Length % 2 == 0
            ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2 : sorted[sorted.Length / 2],
            P95Ms = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1], MaxMs = sorted[^1] };
    }
    private static void AssertExited(int pid)
    {
        try { using var process = Process.GetProcessById(pid); Assert.True(process.HasExited); }
        catch (ArgumentException) { }
    }
    private static async Task Save(string suffix, object value)
    {
        var directory = Environment.GetEnvironmentVariable("SAYLO_REJECTION_REPORT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, suffix + ".json"), JsonSerializer.Serialize(value, JsonOptions));
    }

    private sealed record Fixture(string Id, string Source, string Target, string Text);
    private sealed record Row(string Id, string Source, string Target, string Input, string? PreviousOutput, string? Output,
        string Status, string? Constraint, double TotalMs);
    private sealed record Metric(string Name, double Value);
    private sealed record Diagnostic(string Id, string Kind, IReadOnlyList<string> Candidates);

    private sealed class RecordingSession(IArgosSession inner, Func<(string Id, string Text)> current,
        Func<string, bool> wasRejected) : IArgosCandidateSession
    {
        internal List<Diagnostic> Diagnostics { get; } = [];
        public int ProcessId => inner.ProcessId;
        public long WorkingSetBytes => inner.WorkingSetBytes;
        public bool SupportsRankedCandidates => inner is IArgosCandidateSession { SupportsRankedCandidates: true };
        public async Task<string> TranslateAsync(string id, string text, string source, string target, CancellationToken token)
        {
            var result = await inner.TranslateAsync(id, text, source, target, token);
            var fixture = current();
            if (wasRejected(fixture.Id) && text == fixture.Text)
                Diagnostics.Add(new(fixture.Id, "native-primary", [result]));
            return result;
        }
        public async Task<IReadOnlyList<string>> TranslateCandidatesAsync(string id, string text, string source, string target, CancellationToken token)
        {
            var result = await ((IArgosCandidateSession)inner).TranslateCandidatesAsync(id, text, source, target, token);
            var fixture = current();
            if (wasRejected(fixture.Id) && text == fixture.Text)
                Diagnostics.Add(new(fixture.Id, "native-ranked-candidates", result));
            return result;
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
