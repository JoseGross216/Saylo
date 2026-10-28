namespace Saylo.Modules.Translation.Contracts;

/// <summary>Supported language tags for the initial bidirectional pipeline.</summary>
public sealed record Language
{
    public static Language Spanish { get; } = new("es");
    public static Language English { get; } = new("en");

    public string Code { get; }

    private Language(string code) => Code = code;

    public static Language FromCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return code.Trim().ToLowerInvariant() switch
        {
            "es" => Spanish,
            "en" => English,
            _ => throw new ArgumentException("Supported language codes are es and en.", nameof(code))
        };
    }

    public override string ToString() => Code;
}
