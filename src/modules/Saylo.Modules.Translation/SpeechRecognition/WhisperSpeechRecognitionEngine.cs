using System.Diagnostics;
using System.Diagnostics.Metrics;
using Saylo.Modules.ModelManagement.Contracts;
using Saylo.Modules.Translation.Contracts;
using Whisper.net;

namespace Saylo.Modules.Translation.SpeechRecognition;

/// <summary>One reusable CPU model context, serialized inference and asynchronous shutdown.</summary>
public sealed class WhisperSpeechRecognitionEngine : ISpeechRecognitionEngine, IAsyncDisposable
{
    private static readonly Meter Meter = new("Saylo.SpeechRecognition.Whisper", "1.0");
    private static readonly Histogram<double> LoadTime = Meter.CreateHistogram<double>("saylo.whisper.model_load", "ms");
    private static readonly Histogram<double> InferenceTime = Meter.CreateHistogram<double>("saylo.whisper.inference", "ms");
    private static readonly Histogram<double> AudioDuration = Meter.CreateHistogram<double>("saylo.whisper.audio_duration", "s");
    private readonly IModelCatalog _catalog;
    private readonly Func<string, int, IWhisperBackend> _createBackend;
    private readonly int _threads;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _disposeLock = new();
    private IWhisperBackend? _backend;
    private Task? _disposeTask;
    private int _disposing;

    public WhisperSpeechRecognitionEngine(IModelCatalog catalog, int threads = 4)
        : this(catalog, threads, (path, count) => new WhisperBackend(path, count)) { }

    internal WhisperSpeechRecognitionEngine(IModelCatalog catalog, int threads,
        Func<string, int, IWhisperBackend> createBackend)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(createBackend);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threads);
        _catalog = catalog;
        _threads = threads;
        _createBackend = createBackend;
    }

    public async Task<string> TranscribeAsync(AudioData audio, Language sourceLanguage,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this);
        ArgumentNullException.ThrowIfNull(sourceLanguage);
        if (sourceLanguage.Code is not ("es" or "en"))
            throw Error(TranslationErrorCode.UnsupportedLanguage, "Supported transcription languages are es and en.");
        Pcm16Audio.Validate(audio);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var token = operation.Token;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this);
            // Model loading and sample conversion must never execute on the UI thread.
            return await Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                if (_backend is null)
                    await LoadModelAsync(token).ConfigureAwait(false);
                var samples = Pcm16Audio.Convert(audio, token);
                AudioDuration.Record(samples.Length / 16000d);
                var started = Stopwatch.GetTimestamp();
                try
                {
                    var result = await _backend!.TranscribeAsync(samples, sourceLanguage.Code, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(result))
                        throw Error(TranslationErrorCode.InvalidEngineOutput, "Whisper did not recognize any speech.");
                    return result;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (TranslationEngineException) { throw; }
                catch (Exception exception)
                {
                    throw Error(TranslationErrorCode.EngineFailure, "Local speech recognition failed.", exception);
                }
                finally { InferenceTime.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task LoadModelAsync(CancellationToken token)
    {
        var model = await _catalog.LocateAsync(WhisperModels.BaseId, WhisperModels.BaseVersion, token).ConfigureAwait(false);
        if (model is null)
            throw Error(TranslationErrorCode.ModelUnavailable, "The multilingual ggml-base.bin model is not installed in the configured directory.");
        var validation = await _catalog.ValidateAsync(model, token).ConfigureAwait(false);
        if (validation.Availability != ModelAvailability.Available)
            throw Error(TranslationErrorCode.ModelUnavailable, "The configured model is missing or inaccessible.");
        if (validation.Integrity != ModelIntegrity.Valid || validation.Compatibility == ModelCompatibility.Incompatible
            || model.Purpose != ModelPurpose.SpeechRecognition || !model.Languages.Contains("es") || !model.Languages.Contains("en"))
            throw Error(TranslationErrorCode.ModelInvalid, "The model failed integrity or compatibility validation. Install the multilingual base model.");
        var file = model.Files.SingleOrDefault(file => file.RelativePath == WhisperModels.BaseFileName);
        if (file is null)
            throw Error(TranslationErrorCode.ModelInvalid, "The configured model does not declare ggml-base.bin.");
        var started = Stopwatch.GetTimestamp();
        try
        {
            _backend = _createBackend(Path.Combine(model.DirectoryPath, file.RelativePath), _threads);
            token.ThrowIfCancellationRequested();
        }
        catch (WhisperModelLoadException exception)
        {
            throw Error(TranslationErrorCode.ModelInvalid, "Whisper could not load the model format.", exception);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            throw Error(TranslationErrorCode.RuntimeUnavailable, "The local Whisper CPU runtime could not initialize. Check native dependencies and CPU support.", exception);
        }
        finally { LoadTime.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
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
        try
        {
            if (_backend is not null)
                await _backend.DisposeAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        // The gate and shutdown source remain usable by already queued callers during shutdown.
    }

    private static TranslationEngineException Error(TranslationErrorCode code, string message, Exception? exception = null) =>
        new(TranslationStage.SpeechRecognition, code, message, exception);
}
