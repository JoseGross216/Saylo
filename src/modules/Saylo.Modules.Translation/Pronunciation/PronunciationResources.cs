using Saylo.Modules.ModelManagement;
using Saylo.Modules.ModelManagement.Contracts;

namespace Saylo.Modules.Translation.Pronunciation;

/// <summary>Pinned local data and required redistribution notice; never downloads resources.</summary>
public static class PronunciationResources
{
    public const string DictionaryId = "cmudict-en-us";
    public const string DictionaryVersion = "74790861f652b15e4ac49015a90074ad62a27690";
    public const string DictionarySha256 = "81917843c7f44ce2b094ac63873c2c7a4cf802040792c455ba3ca406891c3d22";
    public const string LicenseSha256 = "bd4ce8e44170a5f9f481310ca85c51de3c4f851a65e679b40e603b143bd3542a";

    public static IModelCatalog CreateCatalog(string dictionaryDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dictionaryDirectory);
        if (IsNetworkPath(dictionaryDirectory))
            throw new ArgumentException("Pronunciation resources must be installed on a local filesystem.", nameof(dictionaryDirectory));
        return new LocalModelCatalog(
        [
            new(DictionaryId, DictionaryVersion, ModelPurpose.Pronunciation, ["en"], Path.GetFullPath(dictionaryDirectory),
            [new("cmudict.dict", DictionarySha256, 3618488), new("LICENSE", LicenseSha256, 1754)])
        ]);
    }

    internal static bool IsNetworkPath(string path) => path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);
}
