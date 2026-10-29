using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Services.Translation;

public enum DesktopTranslationMode { Unavailable, Local, Demonstration }

public sealed record TranslationExample(Language SourceLanguage, string SourceText, string TranslatedText, string Pronunciation)
{
    public Language TargetLanguage => SourceLanguage == Language.Spanish ? Language.English : Language.Spanish;
}

/// <summary>Desktop capabilities and ownership, separate from ViewModels and concrete providers.</summary>
public sealed class DesktopTranslationConnection : IAsyncDisposable
{
    private readonly IAsyncDisposable? _ownedResource;
    private Task? _disposal;
    public ITranslationService? Service { get; }
    public DesktopTranslationMode Mode { get; }
    public string Title { get; }
    public string Description { get; }
    public int MaximumTextLength { get; }
    public IReadOnlyList<TranslationExample> Examples { get; }

    public DesktopTranslationConnection(ITranslationService? service, DesktopTranslationMode mode,
        string title, string description, int maximumTextLength = 1000,
        IReadOnlyList<TranslationExample>? examples = null, IAsyncDisposable? ownedResource = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTextLength);
        Service = service;
        Mode = mode;
        Title = title;
        Description = description;
        MaximumTextLength = maximumTextLength;
        Examples = Array.AsReadOnly(examples is null ? Array.Empty<TranslationExample>() : new List<TranslationExample>(examples).ToArray());
        _ownedResource = ownedResource;
    }

    public ValueTask DisposeAsync() => new(_disposal ??= _ownedResource?.DisposeAsync().AsTask() ?? Task.CompletedTask);
}
