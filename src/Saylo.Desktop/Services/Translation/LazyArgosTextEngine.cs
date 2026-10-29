using System;
using System.Threading;
using System.Threading.Tasks;
using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.TextTranslation;

namespace Saylo.Desktop.Services.Translation;

/// <summary>Loads manifests off the UI thread and owns the persistent local worker.</summary>
internal sealed class LazyArgosTextEngine(ArgosTranslationOptions options) : ITextTranslationEngine, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _sync = new();
    private ArgosTextTranslationEngine? _engine;
    private Task? _disposal;
    private bool _disposed;

    public async Task<string> TranslateAsync(string text, Language sourceLanguage, Language targetLanguage,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        await _gate.WaitAsync(operation.Token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _engine ??= await Task.Run(() => new ArgosTextTranslationEngine(options, ArgosModels.CreateCatalog(options.ModelDirectory)), operation.Token);
            var engine = _engine;
            return await Task.Run(() => engine.TranslateAsync(text, sourceLanguage, targetLanguage, operation.Token), operation.Token);
        }
        finally { _gate.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync) return new(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        await _shutdown.CancelAsync();
        await _gate.WaitAsync();
        try { if (_engine is not null) await _engine.DisposeAsync(); }
        finally { _gate.Release(); }
    }
}
