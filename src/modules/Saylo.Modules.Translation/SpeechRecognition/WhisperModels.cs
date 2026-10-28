using Saylo.Modules.ModelManagement.Contracts;

namespace Saylo.Modules.Translation.SpeechRecognition;

public static class WhisperModels
{
    public const string BaseId = "whisper-base";
    public const string BaseVersion = "ggml-base-60ed5bc3";
    public const string BaseFileName = "ggml-base.bin";
    public const string BaseSha256 = "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe";
    public const long BaseSizeBytes = 147951465;

    /// <summary>Manifest metadata verified against the upstream model's Git LFS pointer.</summary>
    public static ModelDescriptor BaseMultilingual(string modelDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        return new(BaseId, BaseVersion, ModelPurpose.SpeechRecognition, Array.AsReadOnly(new[] { "es", "en" }),
            Path.GetFullPath(modelDirectory), Array.AsReadOnly(new[] { new ModelFile(BaseFileName, BaseSha256, BaseSizeBytes) }));
    }
}
