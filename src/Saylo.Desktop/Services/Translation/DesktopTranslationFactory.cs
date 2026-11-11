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
using Saylo.Modules.Translation.SpeechRecognition;
using Saylo.Modules.Translation.TextTranslation;

namespace Saylo.Desktop.Services.Translation;

public static class DesktopTranslationFactory
{
    public static DesktopTranslationConnection Create(string[] arguments, Func<string, string?>? readEnvironment = null,
        string? applicationDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Contains("--demo", StringComparer.Ordinal)) return CreateDemonstration();
        var resources = DesktopLocalResources.Resolve(applicationDirectory ?? AppContext.BaseDirectory,
            readEnvironment ?? Environment.GetEnvironmentVariable);
        var python = resources.Python;
        var models = resources.ArgosModels;
        var recognition = CreateRecognition(resources.WhisperModels);
        if (string.IsNullOrWhiteSpace(python) && string.IsNullOrWhiteSpace(models))
            return Unavailable("Faltan los recursos locales de traducción. Reinstala Saylo con el paquete completo.", recognition);
        if (string.IsNullOrWhiteSpace(python) || string.IsNullOrWhiteSpace(models) ||
            !Path.IsPathFullyQualified(python) || !Path.IsPathFullyQualified(models))
            return Unavailable("La instalación de los recursos de traducción está incompleta. Revisa la instalación de Saylo.", recognition);
        try
        {
            var engine = new LazyArgosTextEngine(new ArgosTranslationOptions(python, models));
            var pronunciation = new LocalPronunciationEngine(CreatePronunciationCatalog(resources.CmuDictionary));
            var service = new TranslationService((ISpeechRecognitionEngine?)recognition ?? new UnavailableRecognition(), engine, pronunciation);
            return new DesktopTranslationConnection(service, DesktopTranslationMode.Local, "Traducción local · español ↔ inglés",
                "El texto y la guía IPA se procesan en este equipo. La guía en inglés requiere el diccionario local.",
                ArgosTranslationOptions.MaximumTextLength, ownedResource: new LocalEngineLifetime(engine, pronunciation, recognition))
            {
                Recognition = recognition
            };
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Unavailable("No se pudo leer la configuración local. Solicita su revisión al personal de TI.", recognition);
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

    private static DesktopTranslationConnection Unavailable(string description, WhisperSpeechRecognitionEngine? recognition) =>
        new(null, DesktopTranslationMode.Unavailable, "Recursos de traducción incompletos", description, ownedResource: recognition)
        {
            Recognition = recognition
        };

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

    private static WhisperSpeechRecognitionEngine? CreateRecognition(string? modelDirectory)
    {
        if (string.IsNullOrWhiteSpace(modelDirectory) || !Path.IsPathFullyQualified(modelDirectory) ||
            modelDirectory.StartsWith(@"\\", StringComparison.Ordinal) || modelDirectory.StartsWith("//", StringComparison.Ordinal))
            return null;

        try
        {
            return new WhisperSpeechRecognitionEngine(new LocalModelCatalog([WhisperModels.BaseMultilingual(modelDirectory)]));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            // A bad optional model path leaves written translation usable.
            return null;
        }
    }

    private sealed class LocalEngineLifetime(LazyArgosTextEngine translation, LocalPronunciationEngine pronunciation,
        WhisperSpeechRecognitionEngine? recognition)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() =>
            new(Task.WhenAll(translation.DisposeAsync().AsTask(), pronunciation.DisposeAsync().AsTask(),
                recognition?.DisposeAsync().AsTask() ?? Task.CompletedTask));
    }

    private sealed class UnavailableRecognition : ISpeechRecognitionEngine
    {
        public Task<string> TranscribeAsync(AudioData audio, Language sourceLanguage, CancellationToken cancellationToken = default) =>
            Task.FromException<string>(new TranslationEngineException(TranslationStage.SpeechRecognition,
                TranslationErrorCode.ModelUnavailable, "Speech recognition is not configured."));
    }
}
