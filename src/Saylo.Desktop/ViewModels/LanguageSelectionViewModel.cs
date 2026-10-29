using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Saylo.Desktop.Models;

namespace Saylo.Desktop.ViewModels;

public partial class LanguageSelectionViewModel : ViewModelBase
{
    private LanguageOption _sourceLanguage = LanguageOption.Spanish;
    private LanguageOption _targetLanguage = LanguageOption.English;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionHint))]
    [NotifyCanExecuteChangedFor(nameof(SwapCommand))]
    private bool _isEnabled = true;

    public IReadOnlyList<LanguageOption> AvailableLanguages => LanguageOption.Supported;

    public LanguageOption SourceLanguage
    {
        get => _sourceLanguage;
        set
        {
            if (!IsEnabled || !IsSupported(value))
                return;

            SetPair(value, value == _targetLanguage ? _sourceLanguage : _targetLanguage);
        }
    }

    public LanguageOption TargetLanguage
    {
        get => _targetLanguage;
        set
        {
            if (!IsEnabled || !IsSupported(value))
                return;

            SetPair(value == _sourceLanguage ? _targetLanguage : _sourceLanguage, value);
        }
    }

    public string DirectionLabel => $"{SourceLanguage.Name} → {TargetLanguage.Name}";
    public string SelectionHint => IsEnabled
        ? "Los idiomas se aplican a la transcripción y a la traducción del mensaje."
        : "Los idiomas se conservan durante la operación activa.";

    [RelayCommand(CanExecute = nameof(IsEnabled))]
    private void Swap()
    {
        if (IsEnabled)
            SetPair(_targetLanguage, _sourceLanguage);
    }

    private static bool IsSupported(LanguageOption? value) =>
        value == LanguageOption.Spanish || value == LanguageOption.English;

    private void SetPair(LanguageOption source, LanguageOption target)
    {
        if (source == _sourceLanguage && target == _targetLanguage)
            return;

        // Update both values before notifying so observers always see a valid pair.
        _sourceLanguage = source;
        _targetLanguage = target;
        OnPropertyChanged(nameof(SourceLanguage));
        OnPropertyChanged(nameof(TargetLanguage));
        OnPropertyChanged(nameof(DirectionLabel));
    }
}
