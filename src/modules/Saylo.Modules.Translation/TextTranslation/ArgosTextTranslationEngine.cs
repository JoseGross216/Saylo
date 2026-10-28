using System.Diagnostics;
using System.Diagnostics.Metrics;
using Saylo.Modules.ModelManagement.Contracts;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Translation.TextTranslation;

/// <summary>Serialized requests over a persistent local worker; no HTTP or global Python dependency.</summary>
public sealed class ArgosTextTranslationEngine : ITextTranslationEngine, IAsyncDisposable
{
    private static readonly Meter Meter = new("Saylo.TextTranslation.Argos", "1.0");
    private static readonly Histogram<double> StartupTime = Meter.CreateHistogram<double>("saylo.argos.startup", "ms");
    private static readonly Histogram<double> InferenceTime = Meter.CreateHistogram<double>("saylo.argos.translation", "ms");
    private static readonly Histogram<long> WorkerMemory = Meter.CreateHistogram<long>("saylo.argos.worker_memory", "By");
    private readonly ArgosTranslationOptions _options;
    private readonly IModelCatalog _catalog;
    private readonly Func<ArgosTranslationOptions, CancellationToken, Task<IArgosSession>> _start;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _disposeLock = new();
    private IArgosSession? _session;
    private Task? _disposeTask;
    private int _disposing;
    internal int? WorkerProcessId => _session?.ProcessId;

    public ArgosTextTranslationEngine(ArgosTranslationOptions options, IModelCatalog catalog)
        : this(options, catalog, (settings, token) => Task.Run(() => ArgosProcessSession.StartAsync(settings, token), token)) { }

    internal ArgosTextTranslationEngine(ArgosTranslationOptions options, IModelCatalog catalog,
        Func<ArgosTranslationOptions, CancellationToken, Task<IArgosSession>> start)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(start);
        _options = options;
        _catalog = catalog;
        _start = start;
    }

    public async Task<string> TranslateAsync(string text, Language sourceLanguage, Language targetLanguage,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(sourceLanguage);
        ArgumentNullException.ThrowIfNull(targetLanguage);
        if (text.Length > ArgosTranslationOptions.MaximumTextLength)
            throw new ArgumentException("Local translation currently supports at most 1000 UTF-16 characters per request.", nameof(text));
        if (sourceLanguage == targetLanguage)
            throw new ArgumentException("Source and target languages must differ.", nameof(targetLanguage));
        _ = ArgosModels.Version(sourceLanguage.Code, targetLanguage.Code);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var token = operation.Token;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this);
            if (_session is null)
                await StartAsync(token).ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(_options.TranslationTimeout);
            var started = Stopwatch.GetTimestamp();
            try
            {
                var result = await _session!.TranslateAsync(Guid.NewGuid().ToString("N"), text, sourceLanguage.Code,
                    targetLanguage.Code, deadline.Token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(result))
                    throw Error(TranslationErrorCode.InvalidEngineOutput, "The local engine returned no translated text.");
                WorkerMemory.Record(_session.WorkingSetBytes);
                return result;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                await ResetAsync().ConfigureAwait(false);
                throw;
            }
            catch (OperationCanceledException exception)
            {
                await ResetAsync().ConfigureAwait(false);
                throw Error(TranslationErrorCode.EngineFailure, "Local translation exceeded its time limit.", exception);
            }
            catch (Exception exception)
            {
                await ResetAsync().ConfigureAwait(false);
                if (exception is TranslationEngineException)
                    throw;
                throw Error(TranslationErrorCode.EngineFailure, "Local translation communication failed.", exception);
            }
            finally { InferenceTime.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
        }
        finally { _gate.Release(); }
    }

    private async Task StartAsync(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(_options.StartupTimeout);
        var started = Stopwatch.GetTimestamp();
        try
        {
            foreach (var (source, target) in new[] { ("es", "en"), ("en", "es") })
            {
                var model = await _catalog.LocateAsync(ArgosModels.Id(source, target), ArgosModels.Version(source, target), deadline.Token).ConfigureAwait(false);
                if (model is null)
                    throw Error(TranslationErrorCode.ModelUnavailable, "Both local Argos translation models must be installed.");
                var validation = await _catalog.ValidateAsync(model, deadline.Token).ConfigureAwait(false);
                if (validation.Availability != ModelAvailability.Available)
                    throw Error(TranslationErrorCode.ModelUnavailable, "A local Argos model is missing or inaccessible.");
                if (validation.Integrity != ModelIntegrity.Valid || validation.Compatibility == ModelCompatibility.Incompatible
                    || model.Purpose != ModelPurpose.TextTranslation)
                    throw Error(TranslationErrorCode.ModelInvalid, "A local Argos model failed validation.");
            }
            _session = await _start(_options, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await ResetAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await ResetAsync().ConfigureAwait(false);
            if (exception is TranslationEngineException)
                throw;
            throw Error(TranslationErrorCode.RuntimeUnavailable, "The local Argos worker failed to start or exceeded its startup time limit.", exception);
        }
        finally { StartupTime.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    }

    private async Task ResetAsync()
    {
        var session = _session;
        _session = null;
        if (session is not null)
            await session.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            if (_disposeTask is null)
            {
                Volatile.Write(ref _disposing, 1);
                _disposeTask = DisposeCoreAsync();
            }
            return new(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await ResetAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private static TranslationEngineException Error(TranslationErrorCode code, string message, Exception? exception = null) =>
        new(TranslationStage.TextTranslation, code, message, exception);
}
