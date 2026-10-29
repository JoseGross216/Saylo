using System.Text.Json;
using Saylo.Modules.ModelManagement;
using Saylo.Modules.ModelManagement.Contracts;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Translation.TextTranslation;

public static class ArgosModels
{
    public static string Id(string source, string target) => $"argos-{source}-{target}";
    public static string Version(string source, string target) => (source, target) switch
    {
        ("es", "en") => "1.9",
        ("en", "es") => "1.0",
        _ => throw new ArgumentException("Only es/en direct models are supported.")
    };

    public static LocalModelCatalog CreateCatalog(string modelDirectory) => new(ReadDescriptors(modelDirectory));

    public static IReadOnlyList<ModelDescriptor> ReadDescriptors(string modelDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        var root = Path.GetFullPath(modelDirectory);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "argos-models.json")));
            if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
                throw new InvalidDataException("Unsupported manifest version.");
            var models = new List<ModelDescriptor>();
            foreach (var entry in document.RootElement.GetProperty("models").EnumerateArray())
            {
                var source = entry.GetProperty("source").GetString()!;
                var target = entry.GetProperty("target").GetString()!;
                var id = Id(source, target);
                var version = Version(source, target);
                if (entry.GetProperty("id").GetString() != id || entry.GetProperty("version").GetString() != version
                    || entry.GetProperty("directory").GetString() != $"{source}-{target}")
                    throw new InvalidDataException("Unexpected model metadata.");
                var files = entry.GetProperty("files").EnumerateArray().Select(file => new ModelFile(
                    file.GetProperty("relativePath").GetString()!, file.GetProperty("sha256").GetString()!,
                    file.GetProperty("sizeBytes").GetInt64())).ToArray();
                foreach (var required in new[] { "metadata.json", "model/model.bin" })
                    if (!files.Any(file => file.RelativePath == required))
                        throw new InvalidDataException("Manifest is missing a required file.");
                if (!files.Any(file => file.RelativePath is "bpe.model" or "sentencepiece.model"))
                    throw new InvalidDataException("Manifest is missing its tokenizer.");
                models.Add(new(id, version, ModelPurpose.TextTranslation, Array.AsReadOnly(new[] { source, target }),
                    Path.Combine(root, $"{source}-{target}"), Array.AsReadOnly(files)));
            }
            if (models.Count != 2 || models.Select(model => model.Id).Distinct().Count() != 2)
                throw new InvalidDataException("Both direct models must be declared once.");
            return models.AsReadOnly();
        }
        catch (FileNotFoundException exception) { throw Failure(TranslationErrorCode.ModelUnavailable, exception); }
        catch (DirectoryNotFoundException exception) { throw Failure(TranslationErrorCode.ModelUnavailable, exception); }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidDataException
            or ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            throw Failure(TranslationErrorCode.ModelInvalid, exception);
        }
    }

    private static TranslationEngineException Failure(TranslationErrorCode code, Exception exception) =>
        new(TranslationStage.TextTranslation, code, "The local Argos model manifest is missing, inaccessible or invalid.", exception);
}
