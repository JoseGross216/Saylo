namespace Saylo.Desktop.ViewModels;

/// <summary>Composes the initial desktop workspace without depending on engine implementations.</summary>
public sealed class MainViewModel : ViewModelBase
{
    public LanguageSelectionViewModel Languages { get; } = new();
    public TextInputViewModel TextInput { get; } = new();
}
