using System.Text;
using Whisper.net;

namespace Saylo.Modules.Translation.SpeechRecognition;

// This internal seam allows lifecycle tests without loading native code or model files.
internal interface IWhisperBackend : IAsyncDisposable
{
    Task<string> TranscribeAsync(ReadOnlyMemory<float> samples, string language, CancellationToken token);
}

internal sealed class WhisperBackend : IWhisperBackend
{
    private readonly WhisperFactory _factory;
    private readonly int _threads;

    internal WhisperBackend(string modelPath, int threads)
    {
        _threads = threads;
        _factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = false });
        try { _ = _factory.CreateBuilder(); }
        catch
        {
            _factory.Dispose();
            throw;
        }
    }

    public async Task<string> TranscribeAsync(ReadOnlyMemory<float> samples, string language, CancellationToken token)
    {
        // Factory/model context is reused. Each request gets a processor with no prior text context.
        // WithTranslate is intentionally absent: the default task is transcription.
        await using var processor = _factory.CreateBuilder().WithLanguage(language).WithNoContext()
            .WithThreads(_threads).Build();
        var text = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(samples, token).ConfigureAwait(false))
            text.Append(segment.Text);
        // DisposeAsync waits for the native worker even if enumeration was canceled early.
        token.ThrowIfCancellationRequested();
        return text.ToString().Trim();
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Run(_factory.Dispose).ConfigureAwait(false);
    }
}
