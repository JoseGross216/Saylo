namespace Saylo.Modules.Translation.Contracts;

/// <summary>
/// Enumerate once per operation. Text is yielded before pronunciation and synthesis start.
/// Additional updates arrive independently, in completion order. End of stream means finished.
/// STT or translation failures terminate the stream after a stage error; optional failures do not.
/// Cancellation throws OperationCanceledException; disposing enumeration cancels pending work.
/// Engines must cooperate with cancellation so disposal can wait for their cleanup.
/// Consumers retain delivered text and decide whether to play audio. No playback is performed here.
/// </summary>
public interface ITranslationService
{
    IAsyncEnumerable<TranslationUpdate> TranslateTextAsync(TextTranslationRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TranslationUpdate> TranslateVoiceAsync(VoiceTranslationRequest request,
        CancellationToken cancellationToken = default);
}
