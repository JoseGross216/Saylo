namespace Saylo.Modules.Translation.Contracts;

/// <summary>A provider-independent failure with a safe diagnostic message.</summary>
public sealed class TranslationEngineException : Exception
{
    public TranslationStage Stage { get; }
    public TranslationErrorCode Code { get; }

    public TranslationEngineException(TranslationStage stage, TranslationErrorCode code, string message,
        Exception? innerException = null) : base(message, innerException)
    {
        Stage = stage;
        Code = code;
    }
}
