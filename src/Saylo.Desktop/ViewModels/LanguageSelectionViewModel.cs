using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Saylo.Desktop.Models;

namespace Saylo.Desktop.ViewModels;

public partial class LanguageSelectionViewModel : ViewModelBase
{
    private LanguageOption _sourceLanguage = LanguageOption.Spanish;
    private LanguageOption _targetLanguage = LanguageOption.English;

    public IReadOnlyList<LanguageOption> AvailableLanguages => LanguageOption.Supported;

    public LanguageOption SourceLanguage
    {
        get => _sourceLanguage;
        set
        {
            if (!IsSupported(value))
                return;

            SetPair(value, value == _targetLanguage ? _sourceLanguage : _targetLanguage);
        }
    }

    public LanguageOption TargetLanguage
    {
        get => _targetLanguage;
        set
        {
            if (!IsSupported(value))
                return;

            SetPair(value == _sourceLanguage ? _targetLanguage : _sourceLanguage, value);
        }
    }

    public string DirectionLabel => $"{SourceLanguage.Name} → {TargetLanguage.Name}";

    [RelayCommand]
    private void Swap() => SetPair(_targetLanguage, _sourceLanguage);

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
