namespace Saylo.Modules.ModelManagement.Contracts;

/// <summary>
/// Independent validation outcomes. Unknown must never be treated as success.
/// DiagnosticCode is an optional machine-readable code, without conversation content.
/// </summary>
public sealed record ModelValidationResult(
    ModelAvailability Availability,
    ModelCompatibility Compatibility,
    ModelIntegrity Integrity,
    string? DiagnosticCode = null);

public enum ModelAvailability
{
    Unknown = 0,
    Available,
    Missing,
    Inaccessible
}

public enum ModelCompatibility
{
    Unknown = 0,
    Compatible,
    Incompatible
}

public enum ModelIntegrity
{
    Unknown = 0,
    Valid,
    Invalid
}
