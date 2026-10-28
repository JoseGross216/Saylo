using System.Text.Json;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Translation.TextTranslation;

internal static class ArgosProtocol
{
    internal static string ParseResponse(string? line, string expectedId, bool ready = false)
    {
        if (line is null || line.Length > 65536)
            throw new InvalidDataException("The local worker closed its pipe or exceeded the response limit.");
        using var document = JsonDocument.Parse(line);
        var response = document.RootElement;
        if (response.GetProperty("protocol").GetInt32() != 1 || response.GetProperty("id").GetString() != expectedId)
            throw new InvalidDataException("Unexpected local response correlation or protocol.");
        if (!response.GetProperty("ok").GetBoolean())
            throw new TranslationEngineException(TranslationStage.TextTranslation,
                ready ? response.TryGetProperty("code", out var code) && code.GetString() == "model_invalid"
                    ? TranslationErrorCode.ModelInvalid : TranslationErrorCode.RuntimeUnavailable : TranslationErrorCode.EngineFailure,
                ready ? "The local Argos worker could not initialize." : "The local Argos worker could not translate this request.");
        if (ready)
            return "";
        var text = response.GetProperty("text").GetString();
        if (string.IsNullOrWhiteSpace(text))
            throw new TranslationEngineException(TranslationStage.TextTranslation, TranslationErrorCode.InvalidEngineOutput,
                "The local Argos worker returned no translated text.");
        return text;
    }
}
