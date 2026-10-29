using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Saylo.Desktop.Services.Audio;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.ViewModels;

/// <summary>UI composition over local service contracts; concrete engines are injected at startup.</summary>
public partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly ITranslationService? _translation;
    private bool _disposed;

    public MainViewModel() : this(AudioInputAdapterFactory.CreateDefault()) { }

    public MainViewModel(IAudioInputAdapter audioInput, ISpeechRecognitionEngine? recognizer = null,
        ITranslationService? translation = null, TimeProvider? timeProvider = null)
    {
        _translation = translation;
        AudioInput = new(audioInput, () => Languages.SourceLanguage.Language, recognizer, timeProvider);
        AudioInput.PropertyChanged += OnAudioChanged;
        AudioInput.TranscriptAccepted += UseTranscript;
        TextInput.PropertyChanged += OnTextChanged;
        StatusMessage = translation is null ? "Traducción pendiente de configuración local." : "Listo para traducir.";
    }

    public LanguageSelectionViewModel Languages { get; } = new();
    public TextInputViewModel TextInput { get; } = new();
    public AudioInputViewModel AudioInput { get; }
    public ObservableCollection<ConversationMessageViewModel> Messages { get; } = new();
    public bool IsConversationEmpty => Messages.Count == 0;
    public bool HasMessages => !IsConversationEmpty;

    [ObservableProperty] private bool _isAudioPanelOpen;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isTranslating;

    private bool CanTranslate => !_disposed && _translation is not null && TextInput.HasText &&
        !IsTranslating && !AudioInput.IsSessionActive && !AudioInput.IsBusy;

    public Task InitializeAsync() => AudioInput.RefreshDevicesCommand.ExecuteAsync(null);

    [RelayCommand(CanExecute = nameof(CanTranslate), IncludeCancelCommand = true)]
    private async Task TranslateAsync(CancellationToken cancellationToken)
    {
        if (!CanTranslate) return;
        var request = TextInput.CreateRequest(Languages.SourceLanguage.Language, Languages.TargetLanguage.Language);
        var message = new ConversationMessageViewModel(request);
        Messages.Add(message);
        OnPropertyChanged(nameof(IsConversationEmpty));
        OnPropertyChanged(nameof(HasMessages));
        IsTranslating = true;
        UpdateInteraction();
        StatusMessage = "Traduciendo el mensaje en este equipo…";
        try
        {
            await foreach (var update in _translation!.TranslateTextAsync(request, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposed) break;
                message.Apply(update);
            }
            message.Complete();
            StatusMessage = message.Status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            message.Complete();
            message.Status = "Operación cancelada";
            StatusMessage = "Traducción cancelada. El texto disponible se conserva.";
        }
        catch
        {
            message.Complete();
            message.Status = "La operación falló. El contenido disponible se conserva.";
            StatusMessage = "No se pudo completar la traducción. Puedes reintentar.";
        }
        finally
        {
            IsTranslating = false;
            UpdateInteraction();
        }
    }

    private void UseTranscript(string text)
    {
        // Appending preserves any message the employee was already preparing.
        TextInput.Text = TextInput.HasText ? $"{TextInput.Text}{Environment.NewLine}{text}" : text;
        StatusMessage = "Transcripción añadida. Revisa el mensaje antes de traducir.";
    }

    private void OnTextChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TextInput.HasText)) TranslateCommand.NotifyCanExecuteChanged();
    }

    private void OnAudioChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AudioInput.State)) UpdateInteraction();
    }

    private void UpdateInteraction()
    {
        Languages.IsEnabled = !_disposed && !IsTranslating && !AudioInput.IsSessionActive && !AudioInput.IsBusy;
        AudioInput.IsEnabled = !_disposed && !IsTranslating;
        TranslateCommand.NotifyCanExecuteChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        TranslateCommand.Cancel();
        await AudioInput.DisposeAsync();
        if (TranslateCommand.ExecutionTask is { } pending) await pending;
        AudioInput.PropertyChanged -= OnAudioChanged;
        AudioInput.TranscriptAccepted -= UseTranscript;
        TextInput.PropertyChanged -= OnTextChanged;
        foreach (var message in Messages) message.ReleaseAudio();
        Messages.Clear();
        TextInput.Text = string.Empty;
    }
}
