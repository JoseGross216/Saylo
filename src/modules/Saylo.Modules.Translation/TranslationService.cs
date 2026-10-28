using System.Runtime.CompilerServices;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Translation;

/// <summary>Coordinates local engines supplied through constructor injection.</summary>
public sealed class TranslationService : ITranslationService
{
    private readonly ISpeechRecognitionEngine _recognition;
    private readonly ITextTranslationEngine _translation;
    private readonly IPronunciationEngine _pronunciation;
    private readonly ISpeechSynthesisEngine _synthesis;

    public TranslationService(ISpeechRecognitionEngine recognition, ITextTranslationEngine translation,
        IPronunciationEngine pronunciation, ISpeechSynthesisEngine synthesis)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        ArgumentNullException.ThrowIfNull(translation);
        ArgumentNullException.ThrowIfNull(pronunciation);
        ArgumentNullException.ThrowIfNull(synthesis);
        _recognition = recognition;
        _translation = translation;
        _pronunciation = pronunciation;
        _synthesis = synthesis;
    }

    public IAsyncEnumerable<TranslationUpdate> TranslateTextAsync(TextTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunAsync(request.Text, null, request.SourceLanguage, request.TargetLanguage, cancellationToken);
    }

    public IAsyncEnumerable<TranslationUpdate> TranslateVoiceAsync(VoiceTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunAsync(null, request.Audio, request.SourceLanguage, request.TargetLanguage, cancellationToken);
    }

    private async IAsyncEnumerable<TranslationUpdate> RunAsync(string? text, AudioData? audio,
        Language source, Language target, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = operation.Token;
        token.ThrowIfCancellationRequested();
        var operationId = Guid.NewGuid();

        if (audio is not null)
        {
            var transcription = await InvokeAsync(
                () => _recognition.TranscribeAsync(audio, source, token),
                value => !string.IsNullOrWhiteSpace(value), token).ConfigureAwait(false);
            if (transcription.Error is { } transcriptionError)
            {
                yield return new TranslationStageFailed(operationId, TranslationStage.SpeechRecognition, transcriptionError);
                yield break;
            }

            text = transcription.Value!;
            yield return new TranscriptionCompleted(operationId, text, source);
        }

        var translation = await InvokeAsync(
            () => _translation.TranslateAsync(text!, source, target, token),
            value => !string.IsNullOrWhiteSpace(value), token).ConfigureAwait(false);
        if (translation.Error is { } translationError)
        {
            yield return new TranslationStageFailed(operationId, TranslationStage.TextTranslation, translationError);
            yield break;
        }

        var translatedText = translation.Value!;
        yield return new TextTranslationCompleted(operationId, text!, translatedText, source, target);

        // Start optional stages only after text has been delivered to the consumer.
        token.ThrowIfCancellationRequested();
        var pronunciationTask = GeneratePronunciationAsync(operationId, translatedText, target, token);
        var synthesisTask = GenerateSpeechAsync(operationId, translatedText, target, token);
        var pending = new List<Task<TranslationUpdate>> { pronunciationTask, synthesisTask };
        try
        {
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(completed);
                var update = await completed.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                yield return update;
            }
        }
        finally
        {
            // Also clean up engines when the consumer stops enumeration early.
            operation.Cancel();
            try
            {
                await Task.WhenAll(pronunciationTask, synthesisTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // All tasks have been observed; caller cancellation still propagates from the body.
            }
        }
    }

    private async Task<TranslationUpdate> GeneratePronunciationAsync(Guid id, string text, Language language,
        CancellationToken token)
    {
        var outcome = await InvokeAsync(() => _pronunciation.GenerateAsync(text, language, token),
            _ => true, token).ConfigureAwait(false);
        return outcome.Error is { } error
            ? new TranslationStageFailed(id, TranslationStage.Pronunciation, error)
            : new PronunciationCompleted(id, outcome.Value!);
    }

    private async Task<TranslationUpdate> GenerateSpeechAsync(Guid id, string text, Language language,
        CancellationToken token)
    {
        var outcome = await InvokeAsync(() => _synthesis.SynthesizeAsync(text, language, token),
            _ => true, token).ConfigureAwait(false);
        return outcome.Error is { } error
            ? new TranslationStageFailed(id, TranslationStage.SpeechSynthesis, error)
            : new SpeechSynthesisCompleted(id, outcome.Value!);
    }

    private static async Task<EngineOutcome<T>> InvokeAsync<T>(Func<Task<T>> action, Func<T, bool> isValid,
        CancellationToken token) where T : class
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var value = await action().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return value is not null && isValid(value)
                ? new(value, null)
                : new(null, TranslationErrorCode.InvalidEngineOutput);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (TranslationEngineException exception)
        {
            token.ThrowIfCancellationRequested();
            return new(null, exception.Code);
        }
        catch (Exception)
        {
            token.ThrowIfCancellationRequested();
            return new(null, TranslationErrorCode.EngineFailure);
        }
    }

    private sealed record EngineOutcome<T>(T? Value, TranslationErrorCode? Error) where T : class;
}
