using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.TextTranslation;
using Xunit.Abstractions;

namespace Saylo.Modules.Translation.Tests;

[Collection("ArgosIntegration")]
public sealed class ArgosIntegrationTests(ITestOutputHelper output)
{
    private static ArgosTranslationOptions Options() => new(Environment.GetEnvironmentVariable("SAYLO_ARGOS_PYTHON")!,
        Environment.GetEnvironmentVariable("SAYLO_ARGOS_MODELS")!);

    [ArgosIntegrationFact]
    [Trait("Category", "ArgosIntegration")]
    public async Task TranslatesCustomerServiceFixturesInBothDirectionsAndReusesOneWorker()
    {
        var options = Options();
        var metrics = new ConcurrentQueue<object>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Saylo.TextTranslation.Argos")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            metrics.Enqueue(new { instrument.Name, Value = value, instrument.Unit });
            output.WriteLine($"{instrument.Name}={value:F3} {instrument.Unit}");
        });
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            metrics.Enqueue(new { instrument.Name, Value = (double)value, instrument.Unit });
            output.WriteLine($"{instrument.Name}={value} {instrument.Unit}");
        });
        listener.Start();
        var fixtures = new (string Source, string Text, string[] Markers)[]
        {
            ("es", "Buenos dias, en que puedo ayudarle?", ["help"]),
            ("es", "Puede proporcionarme su numero de pedido?", ["order"]),
            ("es", "Voy a verificar la informacion.", ["information"]),
            ("es", "Su solicitud ha sido procesada correctamente.", ["request", "processed"]),
            ("es", "Gracias por comunicarse con nosotros.", ["thank"]),
            ("en", "How can I help you today?", ["hoy"]),
            ("en", "Could you please confirm your order number?", ["pedido"]),
            ("en", "Let me check that information.", ["informaci"]),
            ("en", "Your request has been processed successfully.", ["solicitud", "procesad"]),
            ("en", "Thank you for contacting us.", ["gracias"]),
            ("es", "No cancele el pedido 12345. El importe es 25 euros.", ["not", "12345", "25"]),
            ("en", "Do not cancel order 12345. The amount is 25 euros.", ["no", "12345", "25"]),
            ("es", "La ingeniera Maria revisara el servidor SQL.", ["SQL"]),
            ("en", "Please ask Stanley to restart the SQL server.", ["SQL"])
        };
        var results = new List<object>();
        var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        int? firstPid = null;
        try
        {
            foreach (var fixture in fixtures)
            {
                var source = Language.FromCode(fixture.Source);
                var target = source == Language.Spanish ? Language.English : Language.Spanish;
                var started = Stopwatch.GetTimestamp();
                var text = await engine.TranslateAsync(fixture.Text, source, target);
                var totalMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                firstPid ??= engine.WorkerProcessId;
                Assert.Equal(firstPid, engine.WorkerProcessId);
                foreach (var marker in fixture.Markers)
                    Assert.True(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Expected marker '{marker}' missing from authorized fixture output: {text}");
                results.Add(new { Source = source.Code, Target = target.Code, Input = fixture.Text, Output = text, TotalMs = totalMs });
                output.WriteLine($"{source.Code}->{target.Code} total_ms={totalMs:F3}: {fixture.Text} => {text}");
            }
        }
        finally { await engine.DisposeAsync(); }
        Assert.True(firstPid is not null);
        AssertExited(firstPid!.Value);
        var report = Environment.GetEnvironmentVariable("SAYLO_ARGOS_REPORT");
        if (!string.IsNullOrWhiteSpace(report))
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Metrics = metrics, Results = results },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    [ArgosIntegrationFact]
    [Trait("Category", "ArgosIntegration")]
    public async Task CanceledRealWorkIsReapedAndTheNextRequestStartsAFreshWorker()
    {
        var options = Options();
        var engine = new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory));
        int? finalPid = null;
        try
        {
            await engine.TranslateAsync("Buenos dias.", Language.Spanish, Language.English);
            var oldPid = engine.WorkerProcessId!.Value;
            using var cancellation = new CancellationTokenSource();
            var run = engine.TranslateAsync(string.Join(" ", Enumerable.Repeat("No cancele este pedido.", 30)),
                Language.Spanish, Language.English, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            AssertExited(oldPid);
            Assert.False(string.IsNullOrWhiteSpace(await engine.TranslateAsync("Thank you.", Language.English, Language.Spanish)));
            finalPid = engine.WorkerProcessId;
            Assert.NotEqual(oldPid, finalPid);
        }
        finally { await engine.DisposeAsync(); }
        AssertExited(finalPid!.Value);
    }

    private static void AssertExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            Assert.True(process.HasExited, "The local worker must not remain orphaned.");
        }
        catch (ArgumentException) { }
    }
}

[CollectionDefinition("ArgosIntegration", DisableParallelization = true)]
public sealed class ArgosIntegrationCollection;

public sealed class ArgosIntegrationFactAttribute : FactAttribute
{
    public ArgosIntegrationFactAttribute()
    {
        if (new[] { "SAYLO_ARGOS_PYTHON", "SAYLO_ARGOS_MODELS" }.Any(name =>
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Real local translation requires a configured Python runtime and both models. See docs/local-text-translation.md.";
    }
}
