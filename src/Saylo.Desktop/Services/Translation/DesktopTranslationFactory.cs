using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Saylo.Modules.ModelManagement;
using Saylo.Modules.ModelManagement.Contracts;
using Saylo.Modules.Translation;
using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.Pronunciation;
using Saylo.Modules.Translation.TextTranslation;

namespace Saylo.Desktop.Services.Translation;

public static class DesktopTranslationFactory
{
    public static DesktopTranslationConnection Create(string[] arguments, Func<string, string?>? readEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Contains("--demo", StringComparer.Ordinal)) return CreateDemonstration();
        readEnvironment ??= Environment.GetEnvironmentVariable;
        var python = readEnvironment("SAYLO_ARGOS_PYTHON");
        var models = readEnvironment("SAYLO_ARGOS_MODELS");
        if (string.IsNullOrWhiteSpace(python) && string.IsNullOrWhiteSpace(models))
            return Unavailable("El motor local aún no está disponible en este equipo. Solicita su configuración al personal de TI.");
        if (string.IsNullOrWhiteSpace(python) || string.IsNullOrWhiteSpace(models) ||
            !Path.IsPathFullyQualified(python) || !Path.IsPathFullyQualified(models))
            return Unavailable("La configuración del motor local está incompleta. Solicita su revisión al personal de TI.");
        try
        {
            var engine = new LazyArgosTextEngine(new ArgosTranslationOptions(python, models));
            var pronunciation = new LocalPronunciationEngine(CreatePronunciationCatalog(readEnvironment("SAYLO_CMUDICT_DIR")));
            var service = new TranslationService(new UnavailableRecognition(), engine, pronunciation);
            return new(service, DesktopTranslationMode.Local, "Traducción local · español ↔ inglés",
                "El texto y la guía IPA se procesan en este equipo. La guía en inglés requiere el diccionario local.",
                ArgosTranslationOptions.MaximumTextLength, ownedResource: new LocalEngineLifetime(engine, pronunciation));
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Unavailable("No se pudo leer la configuración local. Solicita su revisión al personal de TI.");
        }
    }

    public static DesktopTranslationConnection CreateDemonstration()
    {
        var engines = new DemonstrationEngines();
        return new(new TranslationService(engines, engines, engines, engines), DesktopTranslationMode.Demonstration,
            "Demostración · resultados de ejemplo",
            "Usa las frases precargadas para probar ambos idiomas. Las traducciones y guías son ejemplos; este modo no utiliza IA ni genera audio.",
            examples: DemonstrationEngines.Examples);
    }

    private static DesktopTranslationConnection Unavailable(string description) =>
        new(null, DesktopTranslationMode.Unavailable, "Traducción local sin configurar", description);

    private static IModelCatalog CreatePronunciationCatalog(string? dictionaryDirectory)
    {
        if (!string.IsNullOrWhiteSpace(dictionaryDirectory) && Path.IsPathFullyQualified(dictionaryDirectory))
        {
            try { return PronunciationResources.CreateCatalog(dictionaryDirectory); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                // Keep the embedded Spanish rules usable when the optional dictionary path is invalid.
            }
        }

        return new LocalModelCatalog([]);
    }

    private sealed class LocalEngineLifetime(LazyArgosTextEngine translation, LocalPronunciationEngine pronunciation)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() =>
            new(Task.WhenAll(translation.DisposeAsync().AsTask(), pronunciation.DisposeAsync().AsTask()));
    }

    private sealed class UnavailableRecognition : ISpeechRecognitionEngine
    {
        public Task<string> TranscribeAsync(AudioData audio, Language sourceLanguage, CancellationToken cancellationToken = default) =>
            Task.FromException<string>(new TranslationEngineException(TranslationStage.SpeechRecognition,
                TranslationErrorCode.ModelUnavailable, "Speech recognition is not configured."));
    }
}
