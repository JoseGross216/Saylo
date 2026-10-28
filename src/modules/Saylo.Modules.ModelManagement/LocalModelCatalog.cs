using System.Security.Cryptography;
using Saylo.Modules.ModelManagement.Contracts;

namespace Saylo.Modules.ModelManagement;

/// <summary>A configured local catalog. Never downloads, installs or loads models.</summary>
public sealed class LocalModelCatalog : IModelCatalog
{
    private readonly ModelDescriptor[] _models;

    public LocalModelCatalog(IEnumerable<ModelDescriptor> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        _models = models.Select(Snapshot).ToArray();
        if (_models.GroupBy(model => (model.Id, model.Version)).Any(group => group.Count() > 1))
            throw new ArgumentException("Model IDs and versions must be unique.", nameof(models));
    }

    public Task<IReadOnlyList<ModelDescriptor>> GetInstalledModelsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ModelDescriptor> installed = Array.AsReadOnly(_models.Where(IsPresent).ToArray());
        return Task.FromResult(installed);
    }

    public Task<ModelDescriptor?> LocateAsync(string modelId, string version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_models.SingleOrDefault(model =>
            model.Id == modelId && model.Version == version && IsPresent(model)));
    }

    public async Task<ModelValidationResult> ValidateAsync(ModelDescriptor model, CancellationToken cancellationToken = default)
    {
        model = Snapshot(model);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            foreach (var file in model.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var stream = new FileStream(ResolveFile(model, file), FileMode.Open, FileAccess.Read,
                    FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length != file.SizeBytes)
                    return Result(ModelAvailability.Available, ModelIntegrity.Invalid, "SizeMismatch");
                var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!Convert.ToHexString(hash).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    return Result(ModelAvailability.Available, ModelIntegrity.Invalid, "ChecksumMismatch");
            }
            // Native format/runtime compatibility is checked later by the corresponding adapter.
            return Result(ModelAvailability.Available, ModelIntegrity.Valid, null);
        }
        catch (FileNotFoundException) { return Result(ModelAvailability.Missing, ModelIntegrity.Unknown, "FileMissing"); }
        catch (DirectoryNotFoundException) { return Result(ModelAvailability.Missing, ModelIntegrity.Unknown, "DirectoryMissing"); }
        catch (UnauthorizedAccessException) { return Result(ModelAvailability.Inaccessible, ModelIntegrity.Unknown, "AccessDenied"); }
        catch (IOException) { return Result(ModelAvailability.Inaccessible, ModelIntegrity.Unknown, "ReadFailure"); }
    }

    private static ModelValidationResult Result(ModelAvailability availability, ModelIntegrity integrity, string? code) =>
        new(availability, ModelCompatibility.Unknown, integrity, code);

    private static bool IsPresent(ModelDescriptor model) => model.Files.All(file => File.Exists(ResolveFile(model, file)));

    private static ModelDescriptor Snapshot(ModelDescriptor model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(model.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(model.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(model.DirectoryPath);
        if (!Path.IsPathFullyQualified(model.DirectoryPath))
            throw new ArgumentException("The model directory must be an absolute configured path.", nameof(model));
        ArgumentNullException.ThrowIfNull(model.Languages);
        ArgumentNullException.ThrowIfNull(model.Files);
        if (model.Files.Count == 0)
            throw new ArgumentException("A model must declare at least one file.", nameof(model));
        foreach (var file in model.Files)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentException.ThrowIfNullOrWhiteSpace(file.RelativePath);
            if (file.SizeBytes < 0 || file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new ArgumentException("Each model file requires its expected size and SHA-256.", nameof(model));
            _ = ResolveFile(model, file);
        }
        if (model.Files.Select(file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != model.Files.Count)
            throw new ArgumentException("Model file paths must be unique.", nameof(model));
        return model with
        {
            DirectoryPath = Path.GetFullPath(model.DirectoryPath),
            Languages = Array.AsReadOnly(model.Languages.ToArray()),
            Files = Array.AsReadOnly(model.Files.ToArray())
        };
    }

    private static string ResolveFile(ModelDescriptor model, ModelFile file)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(model.DirectoryPath));
        var path = Path.GetFullPath(Path.Combine(root, file.RelativePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Path.IsPathRooted(file.RelativePath) || !path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Model file paths must stay inside their configured directory.", nameof(model));
        return path;
    }
}
