using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Saylo.Desktop.Services;
using Saylo.Desktop.Services.Audio;
using Saylo.Modules.Conversations;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly ITranslationService? _translation;
    private readonly IClipboardService _clipboard;
    private readonly ConversationSession _session = new();
    private readonly ObservableCollection<ConversationMessageViewModel> _messages = new();
    private readonly ChatPlaybackController _playback;
    private bool _disposed;
    private bool _applyingTranscript;
    private MessageOrigin _draftOrigin;

    public MainViewModel() : this(AudioInputAdapterFactory.CreateDefault()) { }

    public MainViewModel(IAudioInputAdapter audioInput, ISpeechRecognitionEngine? recognizer = null,
        ITranslationService? translation = null, TimeProvider? timeProvider = null,
        IAudioPlaybackAdapter? playback = null, IClipboardService? clipboard = null)
    {
        _translation = translation;
        _clipboard = clipboard ?? new UnavailableClipboardService();
        Messages = new(_messages);
        AudioInput = new(audioInput, () => Languages.SourceLanguage.Language, recognizer, timeProvider);
        _playback = new(playback ?? new UnavailableAudioPlaybackAdapter());
        _playback.StateChanged += OnPlaybackChanged;
        AudioInput.PropertyChanged += OnAudioChanged;
        AudioInput.TranscriptAccepted += UseTranscript;
        TextInput.PropertyChanged += OnTextChanged;
        StatusMessage = translation is null ? "Traducción pendiente de configuración local." : "Listo para traducir.";
    }

    public LanguageSelectionViewModel Languages { get; } = new();
    public TextInputViewModel TextInput { get; } = new();
    public AudioInputViewModel AudioInput { get; }
    public ReadOnlyObservableCollection<ConversationMessageViewModel> Messages { get; }
    public bool IsConversationEmpty => Messages.Count == 0;
    public bool HasMessages => !IsConversationEmpty;

    [ObservableProperty] private bool _isAudioPanelOpen;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isTranslating;
    [ObservableProperty] private bool _isClearConfirmationOpen;
    [ObservableProperty] private bool _isClearing;

    private bool CanTranslate => !_disposed && !IsClearing && _translation is not null && TextInput.HasText &&
        !IsTranslating && !AudioInput.IsSessionActive && !AudioInput.IsBusy;
    private bool CanClear => !_disposed && !IsClearing && HasMessages;
    private bool CanConfirmClear => CanClear && IsClearConfirmationOpen;

    public Task InitializeAsync() => AudioInput.RefreshDevicesCommand.ExecuteAsync(null);

    [RelayCommand(CanExecute = nameof(CanTranslate), IncludeCancelCommand = true)]
    private async Task TranslateAsync(CancellationToken cancellationToken)
    {
        if (!CanTranslate) return;
        var request = TextInput.CreateRequest(Languages.SourceLanguage.Language, Languages.TargetLanguage.Language);
        var entry = _session.Start(request, _draftOrigin);
        var message = new ConversationMessageViewModel(entry, _clipboard, _playback,
            () => !_disposed && !IsClearing && !AudioInput.IsSessionActive && !AudioInput.IsBusy && _session.Contains(entry));
        _messages.Add(message);
        NotifyMessagesChanged();
        IsTranslating = true;
        UpdateInteraction();
        StatusMessage = "Traduciendo el mensaje en este equipo…";
        try
        {
            await _playback.StopAsync();
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var update in _translation!.TranslateTextAsync(request, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_session.Contains(entry)) break;
                if (!_session.Apply(entry, update)) continue;
                message.Refresh();
                if (update is SpeechSynthesisCompleted) _playback.Autoplay(message, cancellationToken);
            }
            if (_session.Contains(entry))
            {
                entry.Complete();
                message.Refresh();
                StatusMessage = message.Status;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (_session.Contains(entry))
            {
                entry.Cancel();
                message.Refresh();
                StatusMessage = "Traducción cancelada. El texto disponible se conserva.";
            }
        }
        catch
        {
            if (_session.Contains(entry))
            {
                entry.Fail();
                message.Refresh();
                StatusMessage = "No se pudo completar la traducción. Puedes reintentar.";
            }
        }
        finally
        {
            IsTranslating = false;
            UpdateInteraction();
        }
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void RequestClear()
    {
        if (!CanClear) return;
        IsClearConfirmationOpen = true;
        ConfirmClearCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void KeepConversation()
    {
        IsClearConfirmationOpen = false;
        ConfirmClearCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanConfirmClear))]
    private async Task ConfirmClearAsync()
    {
        if (!CanConfirmClear) return;
        IsClearConfirmationOpen = false;
        IsClearing = true;
        TranslateCommand.Cancel();
        _session.Clear();
        foreach (var message in _messages) message.Refresh();
        _messages.Clear();
        NotifyMessagesChanged();
        UpdateInteraction();
        try
        {
            await _playback.StopAsync();
            if (AudioInput.CancelCommand.CanExecute(null)) await AudioInput.CancelCommand.ExecuteAsync(null);
            if (TranslateCommand.ExecutionTask is { } pending) await pending;
            StatusMessage = "Conversación eliminada. El borrador se conserva.";
        }
        finally
        {
            IsClearing = false;
            UpdateInteraction();
        }
    }

    private void NotifyMessagesChanged()
    {
        OnPropertyChanged(nameof(IsConversationEmpty));
        OnPropertyChanged(nameof(HasMessages));
        RequestClearCommand.NotifyCanExecuteChanged();
        ConfirmClearCommand.NotifyCanExecuteChanged();
    }

    private void UseTranscript(string text)
    {
        var hadText = TextInput.HasText;
        _applyingTranscript = true;
        TextInput.Text = hadText ? $"{TextInput.Text}{Environment.NewLine}{text}" : text;
        _applyingTranscript = false;
        _draftOrigin = hadText ? MessageOrigin.Mixed : MessageOrigin.ReviewedVoice;
        StatusMessage = "Transcripción añadida. Revisa el mensaje antes de traducir.";
    }

    private void OnTextChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TextInput.Text) && !_applyingTranscript)
            _draftOrigin = !TextInput.HasText ? MessageOrigin.Text :
                _draftOrigin == MessageOrigin.Text ? MessageOrigin.Text : MessageOrigin.Mixed;
        if (args.PropertyName == nameof(TextInput.HasText)) TranslateCommand.NotifyCanExecuteChanged();
    }

    private void OnAudioChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AudioInput.State)) UpdateInteraction();
    }

    private void OnPlaybackChanged(object? sender, EventArgs args) => UpdateInteraction();

    private void UpdateInteraction()
    {
        Languages.IsEnabled = !_disposed && !IsClearing && !IsTranslating && !AudioInput.IsSessionActive && !AudioInput.IsBusy;
        AudioInput.IsEnabled = !_disposed && !IsClearing && !IsTranslating && !_playback.IsPlaying;
        TranslateCommand.NotifyCanExecuteChanged();
        RequestClearCommand.NotifyCanExecuteChanged();
        ConfirmClearCommand.NotifyCanExecuteChanged();
        foreach (var message in _messages) message.Refresh();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        IsClearConfirmationOpen = false;
        TranslateCommand.Cancel();
        _session.Dispose();
        foreach (var message in _messages) message.Refresh();
        await _playback.DisposeAsync();
        await AudioInput.DisposeAsync();
        if (TranslateCommand.ExecutionTask is { } pending) await pending;
        _playback.StateChanged -= OnPlaybackChanged;
        AudioInput.PropertyChanged -= OnAudioChanged;
        AudioInput.TranscriptAccepted -= UseTranscript;
        TextInput.PropertyChanged -= OnTextChanged;
        _messages.Clear();
        TextInput.Text = string.Empty;
        NotifyMessagesChanged();
        UpdateInteraction();
    }
}
