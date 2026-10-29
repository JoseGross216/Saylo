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
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string? _text = string.Empty;

    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public bool CanClear => CharacterCount > 0;
    public int CharacterCount => Text?.Length ?? 0;
    public string CharacterCountLabel => CharacterCount == 1 ? "1 carácter" : $"{CharacterCount} caracteres";

    /// <summary>Creates the validated request for the future translation command.</summary>
    public TextTranslationRequest CreateRequest(Language source, Language target) =>
        new(Text!, source, target);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear() => Text = string.Empty;
}
