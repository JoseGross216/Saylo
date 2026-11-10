using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.ViewModels;

public partial class TextInputViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    [NotifyPropertyChangedFor(nameof(CharacterCount))]
    [NotifyPropertyChangedFor(nameof(CharacterCountLabel))]
    [NotifyPropertyChangedFor(nameof(CanClear))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string? _text = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CharacterCountLabel))]
    private int? _maximumLength;

    [ObservableProperty] private string _placeholder = "Escribe el mensaje que quieres traducir…";

    partial void OnMaximumLengthChanging(int? value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
    }

    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public bool CanClear => CharacterCount > 0;
    public int CharacterCount => Text?.Length ?? 0;
    public bool IsValid => HasText && (MaximumLength is null || CharacterCount <= MaximumLength);
    public bool HasValidationError => CharacterCount > 0 && !IsValid;
    public string ValidationMessage => !HasText ? "Escribe un mensaje con contenido para traducir." :
        MaximumLength is { } maximum && CharacterCount > maximum ? $"Este motor admite hasta {maximum} caracteres. Reduce el mensaje; tu texto se conserva completo." : string.Empty;
    public string CharacterCountLabel => MaximumLength is { } maximum ? $"{CharacterCount} / {maximum} caracteres" :
        CharacterCount == 1 ? "1 carácter" : $"{CharacterCount} caracteres";

    /// <summary>Creates the validated request for the future translation command.</summary>
    public TextTranslationRequest CreateRequest(Language source, Language target)
    {
        if (!IsValid) throw new ArgumentException(ValidationMessage, nameof(Text));
        return new(Text!, source, target);
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear() => Text = string.Empty;
}
