using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Translation.Pronunciation;

internal static class PronunciationFailure
{
    internal static TranslationEngineException Create(string reason, TranslationErrorCode code = TranslationErrorCode.InvalidEngineOutput)
    {
        var failure = new TranslationEngineException(TranslationStage.Pronunciation, code,
            "A reliable local pronunciation guide is unavailable for this request.");
        failure.Data["PronunciationReason"] = reason;
        return failure;
    }
}
