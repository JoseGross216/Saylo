using System;
using System.IO;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Services.Translation;

public static class TranslationFailureMessages
{
    public static string ForSpeech(TranslationErrorCode? code) => code switch
    {
        TranslationErrorCode.ModelUnavailable => "Falta el modelo local de voz. Solicita su instalación; el texto se conserva.",
        TranslationErrorCode.ModelInvalid => "El modelo de voz no es válido. Solicita su revisión; el texto se conserva.",
        TranslationErrorCode.RuntimeUnavailable => "El motor de voz no pudo iniciarse. Solicita su revisión; el texto se conserva.",
        TranslationErrorCode.InsufficientMemory => "No hay memoria suficiente para generar la voz. El texto se conserva.",
        TranslationErrorCode.ResourceUnavailable => "No se pudo acceder a los recursos de voz. Revisa su disponibilidad o permisos; el texto se conserva.",
        _ => "No se pudo generar la voz. Puedes seguir leyendo y copiando la traducción."
    };

    public static string ForRecognition(TranslationErrorCode? code) => code switch
    {
        TranslationErrorCode.ModelUnavailable => "Falta el modelo de transcripción. Solicita su instalación o escribe el mensaje.",
        TranslationErrorCode.ModelInvalid => "El modelo de transcripción no es válido. Solicita su revisión o escribe el mensaje.",
        TranslationErrorCode.RuntimeUnavailable => "El motor de transcripción no pudo iniciarse. Solicita su revisión o escribe el mensaje.",
        TranslationErrorCode.InsufficientMemory => "No hay memoria suficiente para transcribir. Libera recursos y graba de nuevo o escribe el mensaje.",
        TranslationErrorCode.ResourceUnavailable => "No se pudo acceder a los recursos de transcripción. Revisa los permisos o escribe el mensaje.",
        _ => "No se pudo transcribir la grabación. Graba de nuevo o escribe el mensaje."
    };

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
