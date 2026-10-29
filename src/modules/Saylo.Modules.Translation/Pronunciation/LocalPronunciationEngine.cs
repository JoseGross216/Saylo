using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.RegularExpressions;
using Saylo.Modules.ModelManagement.Contracts;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Translation.Pronunciation;

/// <summary>Reusable, local broad-IPA engine. No synthesis, network access or conversation cache.</summary>
public sealed class LocalPronunciationEngine : IPronunciationEngine, IAsyncDisposable
{
    private static readonly Meter Meter = new("Saylo.Pronunciation.Local", "1.0");
    private static readonly Histogram<double> LoadTime = Meter.CreateHistogram<double>("saylo.pronunciation.resource_load", "ms");
    private static readonly Histogram<double> GenerationTime = Meter.CreateHistogram<double>("saylo.pronunciation.generation", "ms");
    private readonly IModelCatalog _catalog;
    private readonly Func<string, CancellationToken, EnglishPronunciation> _load;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _disposeLock = new();
    private EnglishPronunciation? _english;
    private SpanishPronunciation? _spanish;
    private Task? _disposeTask;
    private int _disposing;
    internal int DictionaryLoadCount { get; private set; }
    internal int EnglishWordCount => _english?.WordCount ?? 0;
    internal int SpanishWordCount => _spanish?.WordCount ?? 0;

    public LocalPronunciationEngine(IModelCatalog catalog) : this(catalog,
        (path, token) => EnglishPronunciation.Load(path, token, PronunciationResources.DictionarySha256)) { }

    internal LocalPronunciationEngine(IModelCatalog catalog, Func<string, CancellationToken, EnglishPronunciation> load)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(load);
        _catalog = catalog;
        _load = load;
    }

    public async Task<PronunciationResult> GenerateAsync(string translatedText, Language targetLanguage,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this);
        ArgumentNullException.ThrowIfNull(translatedText);
        ArgumentNullException.ThrowIfNull(targetLanguage);
        if (targetLanguage.Code is not ("en" or "es"))
            throw PronunciationFailure.Create("UnsupportedLanguage", TranslationErrorCode.UnsupportedLanguage);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var token = operation.Token;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this);
            // Bounded parsing and dictionary conversion must not run on the consumer's UI thread.
            return await Task.Run(async () =>
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    var tokens = PronunciationText.Prepare(translatedText, targetLanguage.Code, token);
                    if (targetLanguage == Language.English && _english is null)
                        await LoadEnglishAsync(token).ConfigureAwait(false);
                    if (targetLanguage == Language.Spanish && _spanish is null)
                    {
                        var loadStarted = Stopwatch.GetTimestamp();
                        _spanish = new();
                        LoadTime.Record(Stopwatch.GetElapsedTime(loadStarted).TotalMilliseconds);
                    }
                    var text = new StringBuilder();
                    for (var index = 0; index < tokens.Length; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        var word = tokens[index];
                        if (char.IsLetter(word[0]))
                        {
                            if (text.Length > 0 && text[^1] != ' ' && !"¿¡([«“".Contains(text[^1]))
                                text.Append(' ');
                            text.Append(targetLanguage == Language.English ? _english!.Generate(tokens, index) : _spanish!.Generate(word));
                        }
                        else
                        {
                            text.Append(word);
                            if (".,;:!?—-".Contains(word[0]))
                                text.Append(' ');
                        }
                        if (text.Length > 32_000)
                            throw PronunciationFailure.Create("OutputBudget");
                    }
                    token.ThrowIfCancellationRequested();
                    var result = text.ToString().Trim();
                    if (string.IsNullOrWhiteSpace(result))
                        throw PronunciationFailure.Create("NoWords");
                    return new PronunciationResult(result, PronunciationNotation.Ipa);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (TranslationEngineException) { throw; }
                catch (RegexMatchTimeoutException) { throw PronunciationFailure.Create("InputBudget"); }
                catch (Exception) { throw PronunciationFailure.Create("LocalEngineFailure", TranslationErrorCode.EngineFailure); }
                finally { GenerationTime.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task LoadEnglishAsync(CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var model = await _catalog.LocateAsync(PronunciationResources.DictionaryId, PronunciationResources.DictionaryVersion, token).ConfigureAwait(false);
            if (model is null)
                throw PronunciationFailure.Create("DictionaryMissing", TranslationErrorCode.ModelUnavailable);
            if (PronunciationResources.IsNetworkPath(model.DirectoryPath) ||
                model.Purpose != ModelPurpose.Pronunciation || !model.Languages.Contains("en") ||
                !model.Files.Any(file => file.RelativePath == "cmudict.dict" && file.Sha256 == PronunciationResources.DictionarySha256) ||
                !model.Files.Any(file => file.RelativePath == "LICENSE" && file.Sha256 == PronunciationResources.LicenseSha256))
                throw PronunciationFailure.Create("DictionaryMetadata", TranslationErrorCode.ModelInvalid);
            var validation = await _catalog.ValidateAsync(model, token).ConfigureAwait(false);
            if (validation.Availability != ModelAvailability.Available)
                throw PronunciationFailure.Create("DictionaryMissing", TranslationErrorCode.ModelUnavailable);
            if (validation.Integrity != ModelIntegrity.Valid || validation.Compatibility == ModelCompatibility.Incompatible)
                throw PronunciationFailure.Create("DictionaryChecksum", TranslationErrorCode.ModelInvalid);
            var loaded = _load(Path.Combine(model.DirectoryPath, "cmudict.dict"), token);
            token.ThrowIfCancellationRequested();
            _english = loaded;
            DictionaryLoadCount++;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (TranslationEngineException) { throw; }
        catch (IOException) { throw PronunciationFailure.Create("DictionaryMissing", TranslationErrorCode.ModelUnavailable); }
        catch (UnauthorizedAccessException) { throw PronunciationFailure.Create("DictionaryMissing", TranslationErrorCode.ModelUnavailable); }
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
        try { _english = null; _spanish = null; }
        finally { _gate.Release(); }
    }
}
