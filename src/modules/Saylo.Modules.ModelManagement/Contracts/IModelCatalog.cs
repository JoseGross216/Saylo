namespace Saylo.Modules.ModelManagement.Contracts;

/// <summary>
/// Describes local model discovery and validation without loading an AI engine.
/// Implementations must honor cancellation and must not download or install models.
/// </summary>
public interface IModelCatalog
{
    /// <summary>
    /// Lists models discovered locally. Discovery does not imply successful validation.
    /// </summary>
    Task<IReadOnlyList<ModelDescriptor>> GetInstalledModelsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locates an exact model ID and version, or returns null when not installed.
    /// </summary>
    Task<ModelDescriptor?> LocateAsync(
        string modelId,
        string version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks local availability, file integrity and compatibility with the current runtime.
    /// Does not load the model. Unknown means the corresponding check has not completed.
    /// </summary>
    Task<ModelValidationResult> ValidateAsync(
        ModelDescriptor model,
        CancellationToken cancellationToken = default);
}
