namespace Saylo.Modules.ModelManagement.Contracts;

/// <summary>
/// Metadata for one local model version. DirectoryPath is an absolute local directory.
/// Version is an opaque identifier; no semantic version ordering is assumed.
/// Languages contains language tags. No model engine types are exposed.
/// </summary>
public sealed record ModelDescriptor(
    string Id,
    string Version,
    ModelPurpose Purpose,
    IReadOnlyList<string> Languages,
    string DirectoryPath,
    IReadOnlyList<ModelFile> Files);
