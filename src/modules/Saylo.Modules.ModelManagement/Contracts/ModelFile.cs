namespace Saylo.Modules.ModelManagement.Contracts;

/// <summary>
/// Expected file metadata from a trusted manifest, not measured validation results.
/// RelativePath must stay within the model directory. Sha256 is a hexadecimal hash.
/// SizeBytes is the expected nonnegative file length.
/// </summary>
public sealed record ModelFile(string RelativePath, string Sha256, long SizeBytes);
