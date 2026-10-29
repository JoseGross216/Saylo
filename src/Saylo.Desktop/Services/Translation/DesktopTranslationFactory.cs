using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Saylo.Modules.Translation;
using Saylo.Modules.Translation.Contracts;
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
            var optional = new UnavailableOptionalEngines();
            var service = new TranslationService(optional, engine, optional, optional);
            return new(service, DesktopTranslationMode.Local, "Traducción local · español ↔ inglés",
                "El texto se procesa en este equipo. La pronunciación y la voz dependen de los motores instalados.",
                ArgosTranslationOptions.MaximumTextLength, ownedResource: engine);
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
}

internal sealed class UnavailableOptionalEngines : ISpeechRecognitionEngine, IPronunciationEngine, ISpeechSynthesisEngine
{
    public Task<string> TranscribeAsync(AudioData audio, Language sourceLanguage, CancellationToken cancellationToken = default) =>
        Task.FromException<string>(Failure(TranslationStage.SpeechRecognition));
    public Task<PronunciationResult> GenerateAsync(string text, Language targetLanguage, CancellationToken cancellationToken = default) =>
        Task.FromException<PronunciationResult>(Failure(TranslationStage.Pronunciation));
    public Task<AudioData> SynthesizeAsync(string text, Language targetLanguage, CancellationToken cancellationToken = default) =>
        Task.FromException<AudioData>(Failure(TranslationStage.SpeechSynthesis));
    private static TranslationEngineException Failure(TranslationStage stage) =>
        new(stage, TranslationErrorCode.ModelUnavailable, "This local capability is not configured.");
}
