using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Saylo.Modules.ModelManagement;
using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.SpeechRecognition;
using Xunit.Abstractions;

namespace Saylo.Modules.Translation.Tests;

[Collection("WhisperIntegration")]
public sealed class WhisperIntegrationTests(ITestOutputHelper output)
{
    [WhisperIntegrationFact("ES")]
    [Trait("Category", "WhisperIntegration")]
    public Task TranscribesSpanishLocally() => RunAsync("ES", Language.Spanish);

    [WhisperIntegrationFact("EN")]
    [Trait("Category", "WhisperIntegration")]
    public Task TranscribesEnglishLocally() => RunAsync("EN", Language.English);

    [WhisperIntegrationFact("ES")]
    [Trait("Category", "WhisperIntegration")]
    public async Task NativeCancellationAllowsSubsequentReuse()
    {
        var directory = Environment.GetEnvironmentVariable("SAYLO_WHISPER_MODEL_DIR")!;
        var path = Environment.GetEnvironmentVariable("SAYLO_WHISPER_ES_PCM")!;
        var audio = new AudioData(await File.ReadAllBytesAsync(path), AudioFormat.Pcm16, 16000, 1);
        await using var engine = new WhisperSpeechRecognitionEngine(new LocalModelCatalog([WhisperModels.BaseMultilingual(directory)]));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        Assert.False(string.IsNullOrWhiteSpace(await engine.TranscribeAsync(audio, Language.Spanish, deadline.Token)));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.TranscribeAsync(audio, Language.Spanish, cancellation.Token));
        Assert.False(string.IsNullOrWhiteSpace(await engine.TranscribeAsync(audio, Language.Spanish, deadline.Token)));
        output.WriteLine("Native cancellation completed and the same model context transcribed a subsequent request.");
    }

    private async Task RunAsync(string suffix, Language language)
    {
        var directory = Environment.GetEnvironmentVariable("SAYLO_WHISPER_MODEL_DIR")!;
        var path = Environment.GetEnvironmentVariable($"SAYLO_WHISPER_{suffix}_PCM")!;
        var expected = Environment.GetEnvironmentVariable($"SAYLO_WHISPER_{suffix}_EXPECTED")!;
        Assert.True(File.Exists(Path.Combine(directory, WhisperModels.BaseFileName)), "Configured model file does not exist.");
        Assert.True(File.Exists(path), "Configured PCM fixture does not exist.");
        var audio = new AudioData(await File.ReadAllBytesAsync(path), AudioFormat.Pcm16, 16000, 1);
        var measurements = new ConcurrentQueue<(string Name, double Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Saylo.SpeechRecognition.Whisper")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => measurements.Enqueue((instrument.Name, value)));
        listener.Start();
        await using var engine = new WhisperSpeechRecognitionEngine(new LocalModelCatalog([WhisperModels.BaseMultilingual(directory)]));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            var text = await engine.TranscribeAsync(audio, language, deadline.Token);
            var elapsed = Stopwatch.GetElapsedTime(started);
            // Expected source-language words verify transcription rather than English translation.
            foreach (var word in expected.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                Assert.True(text.Contains(word, StringComparison.OrdinalIgnoreCase), $"Expected source-language marker '{word}' was not recognized.");
            output.WriteLine($"language={language.Code}; attempt={attempt}; audio_s={audio.Data.Length / 32000d:F3}; total_ms={elapsed.TotalMilliseconds:F1}");
            output.WriteLine($"Authorized test fixture transcription: {text}");
        }
        foreach (var metric in measurements)
            output.WriteLine($"{metric.Name}={metric.Value:F3}");
        Assert.Single(measurements.Where(metric => metric.Name == "saylo.whisper.model_load"));
        Assert.Equal(2, measurements.Count(metric => metric.Name == "saylo.whisper.inference"));
    }
}

[CollectionDefinition("WhisperIntegration", DisableParallelization = true)]
public sealed class WhisperIntegrationCollection;

public sealed class WhisperIntegrationFactAttribute : FactAttribute
{
    public WhisperIntegrationFactAttribute(string suffix)
    {
        if (new[] { "SAYLO_WHISPER_MODEL_DIR", $"SAYLO_WHISPER_{suffix}_PCM", $"SAYLO_WHISPER_{suffix}_EXPECTED" }
            .Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Real Whisper integration requires a configured local model, authorized PCM fixture and expected source-language words. See docs/whisper-speech-recognition.md.";
    }
}
