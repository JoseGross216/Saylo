using System;
using System.IO;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Services.Translation;

public static class TranslationFailureMessages
{
    public static TranslationErrorCode CodeFor(Exception error) => error switch
    {
        TranslationEngineException engine => engine.Code,
        OutOfMemoryException => TranslationErrorCode.InsufficientMemory,
        IOException or UnauthorizedAccessException => TranslationErrorCode.ResourceUnavailable,
        _ => TranslationErrorCode.EngineFailure
    };

    public static string For(TranslationErrorCode? code, bool demonstration = false) => code switch
    {
        TranslationErrorCode.ModelUnavailable => "Faltan modelos locales. Solicita su instalación y reintenta; el original se conserva.",
        TranslationErrorCode.ModelInvalid => "Un modelo local no es válido o no se puede leer. Solicita su revisión y reintenta.",
        TranslationErrorCode.RuntimeUnavailable => "El motor local no pudo iniciarse. Solicita su revisión y reintenta.",
        TranslationErrorCode.InsufficientMemory => "No hay memoria suficiente. Libera recursos del equipo y reintenta; el original se conserva.",
        TranslationErrorCode.ResourceUnavailable => "No se pudo acceder a un recurso local. Revisa su disponibilidad o permisos y reintenta.",
        TranslationErrorCode.InvalidEngineOutput => "El motor no devolvió una traducción válida. Puedes reintentar; el original se conserva.",
        _ when demonstration => "La frase no está entre los ejemplos de demostración. Usa una frase precargada o configura el motor local.",
        _ => "No se pudo traducir. Conserva el mensaje y reintenta."
    };
}
