using System.Collections.Generic;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Models;

/// <summary>A display label for a supported language, separate from the engine contract.</summary>
public sealed record LanguageOption(Language Language, string Name, string Tag)
{
    public static LanguageOption Spanish { get; } = new(Language.Spanish, "Español", "ES");
    public static LanguageOption English { get; } = new(Language.English, "Inglés", "EN");

    public static IReadOnlyList<LanguageOption> Supported { get; } =
        System.Array.AsReadOnly(new[] { Spanish, English });
}
