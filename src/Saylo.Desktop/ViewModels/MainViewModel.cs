using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Saylo.Desktop.Services;
using Saylo.Desktop.Services.Audio;
using Saylo.Desktop.Services.Translation;
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
    private readonly DesktopTranslationConnection? _connection;
    private CancellationTokenSource? _requestCancellation;
    private Task _activeTranslation = Task.CompletedTask;
    private bool _recoverableError;
    private bool _hasCurrentTranslation;

    public MainViewModel() : this(AudioInputAdapterFactory.CreateDefault()) { }

    public MainViewModel(IAudioInputAdapter audioInput, ISpeechRecognitionEngine? recognizer = null,
        ITranslationService? translation = null, TimeProvider? timeProvider = null,
        IAudioPlaybackAdapter? playback = null, IClipboardService? clipboard = null,
        DesktopTranslationConnection? connection = null)
    {
        if (translation is not null && connection is not null)
            throw new ArgumentException("Provide a connection or a translation service, not both.");
        _connection = connection;
        _translation = connection?.Service ?? translation;
        TextInput.MaximumLength = connection?.MaximumTextLength;
        _clipboard = clipboard ?? new UnavailableClipboardService();
        Messages = new(_messages);
        AudioInput = new(audioInput, () => Languages.SourceLanguage.Language, recognizer, timeProvider);
        _playback = new(playback ?? new UnavailableAudioPlaybackAdapter());
        _playback.StateChanged += OnPlaybackChanged;
        AudioInput.PropertyChanged += OnAudioChanged;
        AudioInput.TranscriptAccepted += UseTranscript;
        TextInput.PropertyChanged += OnTextChanged;
        Languages.PropertyChanged += OnLanguagesChanged;
        RefreshLanguageContext();
        StatusMessage = _translation is null ? "Traducción pendiente de configuración local." : "Listo para traducir.";
    }

    public LanguageSelectionViewModel Languages { get; } = new();
    public TextInputViewModel TextInput { get; } = new();
    public AudioInputViewModel AudioInput { get; }
    public ReadOnlyObservableCollection<ConversationMessageViewModel> Messages { get; }
    public bool IsConversationEmpty => Messages.Count == 0;
    public bool HasMessages => !IsConversationEmpty;
    public bool IsDemonstration => _connection?.Mode == DesktopTranslationMode.Demonstration;
    public string BackendBadge => IsDemonstration ? "Demostración" : _translation is null ? "Sin configurar" : "Procesamiento local";
    public string BackendTitle => _connection?.Title ?? "Tu asistente de idiomas";
    public string BackendDescription => _connection?.Description ?? "Traducción mediante servicios locales.";
    public IReadOnlyList<TranslationExample> AvailableExamples => _connection?.Examples
        .Where(example => example.SourceLanguage == Languages.SourceLanguage.Language).ToArray() ?? Array.Empty<TranslationExample>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadExampleCommand))]
    private TranslationExample? _selectedExample;

    [ObservableProperty] private bool _isAudioPanelOpen;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isTranslating;
    [ObservableProperty] private bool _isClearConfirmationOpen;
    [ObservableProperty] private bool _isClearing;
    [ObservableProperty] private bool _isAutoplayEnabled = true;

    partial void OnIsAutoplayEnabledChanged(bool value) => _playback.IsAutoplayEnabled = value;

    public bool IsPlaying => _playback.IsPlaying;
    public bool ShowStopPlayback => IsPlaying || _playback.RequiresStopRecovery;
    public string StopPlaybackLabel => _playback.RequiresStopRecovery ? "Reintentar detención" : "Detener voz";
    public bool HasRecoverableError => _recoverableError || _playback.HasError;
    public ApplicationState State => _disposed ? ApplicationState.Closing : IsClearing ? ApplicationState.Clearing :
        _playback.IsStopping ? ApplicationState.StoppingPlayback :
        AudioInput.State switch
        {
            AudioInputState.LoadingDevices => ApplicationState.LoadingDevices,
            AudioInputState.Starting => ApplicationState.PreparingMicrophone,
            AudioInputState.Recording => ApplicationState.Recording,
            AudioInputState.Stopping or AudioInputState.Canceling => ApplicationState.StoppingMicrophone,
            AudioInputState.Transcribing => ApplicationState.Transcribing,
            AudioInputState.Reviewing => ApplicationState.ReviewingTranscript,
            _ when IsPlaying => ApplicationState.Playing,
            _ when IsTranslating => _hasCurrentTranslation ?
                ApplicationState.PreparingResults : ApplicationState.Translating,
            _ when HasRecoverableError => ApplicationState.RecoverableError,
            _ => ApplicationState.Ready
        };
    public string StateLabel => State switch
    {
        ApplicationState.LoadingDevices => "Consultando micrófonos…",
        ApplicationState.PreparingMicrophone => "Preparando el micrófono…",
        ApplicationState.Recording => "Grabando · máximo 30 segundos",
        ApplicationState.StoppingMicrophone => "Liberando el micrófono…",
        ApplicationState.Transcribing => "Transcribiendo…",
        ApplicationState.ReviewingTranscript => "Revisa la transcripción",
        ApplicationState.Translating => "Traduciendo…",
        ApplicationState.PreparingResults => "Texto listo · preparando pronunciación y voz…",
        ApplicationState.Playing => "Reproduciendo voz…",
        ApplicationState.StoppingPlayback => "Deteniendo voz…",
        ApplicationState.Clearing => "Eliminando conversación…",
        ApplicationState.RecoverableError => "Revisa el aviso · puedes seguir usando Saylo",
        ApplicationState.Closing => "Cerrando la sesión…",
        _ => "Listo"
    };
    private bool CanStopPlayback => !_disposed && ShowStopPlayback && !_playback.IsStopping;

    [RelayCommand(CanExecute = nameof(CanStopPlayback))]
    private Task StopPlaybackAsync() => _playback.StopAsync();

    private bool CanStartRequest => !_disposed && !IsClearing && _translation is not null &&
        !IsTranslating && !AudioInput.IsSessionActive && !AudioInput.IsBusy && !_playback.IsStopping;
    private bool CanTranslate => CanStartRequest && TextInput.IsValid;
    private bool CanClear => !_disposed && !IsClearing && HasMessages;
    private bool CanConfirmClear => CanClear && IsClearConfirmationOpen;

    public Task InitializeAsync() => AudioInput.RefreshDevicesCommand.ExecuteAsync(null);

    [RelayCommand(CanExecute = nameof(CanTranslate), IncludeCancelCommand = true)]
    private Task TranslateAsync(CancellationToken cancellationToken)
    {
        if (!CanTranslate) return Task.CompletedTask;
        var request = TextInput.CreateRequest(Languages.SourceLanguage.Language, Languages.TargetLanguage.Language);
        return BeginTranslation(request, _draftOrigin, cancellationToken);
    }

    private Task BeginTranslation(TextTranslationRequest request, MessageOrigin origin, CancellationToken cancellationToken)
    {
        if (!CanStartRequest) return Task.CompletedTask;
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _requestCancellation = source;
        _recoverableError = false;
        _hasCurrentTranslation = false;
        IsTranslating = true;
        UpdateInteraction();
        return _activeTranslation = RunTranslationAsync(request, origin, source);
    }

    private async Task RunTranslationAsync(TextTranslationRequest request, MessageOrigin origin, CancellationTokenSource source)
    {
        var cancellationToken = source.Token;
        ConversationMessage? entry = null;
        ConversationMessageViewModel? message = null;
        try
        {
            entry = _session.Start(request, origin);
            var currentEntry = entry;
            message = new ConversationMessageViewModel(entry, _clipboard, _playback,
                () => !_disposed && !IsClearing && !AudioInput.IsSessionActive && !AudioInput.IsBusy && _session.Contains(currentEntry),
                IsDemonstration, RetryMessageAsync, () => CanStartRequest);
            _messages.Add(message);
            NotifyMessagesChanged();
            StatusMessage = "Traduciendo el mensaje en este equipo…";
            await _playback.StopAsync();
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var update in _translation!.TranslateTextAsync(request, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_session.Contains(entry)) break;
                if (!_session.Apply(entry, update)) continue;
                _hasCurrentTranslation = entry.HasTranslation;
                message.Refresh();
                NotifyStateChanged();
                if (update is SpeechSynthesisCompleted) _playback.Autoplay(message, cancellationToken);
            }
            if (_session.Contains(entry))
            {
                entry.Complete();
                message.Refresh();
                _recoverableError = entry.State == MessageState.Failed || (!IsDemonstration && entry.AudioState == ResultState.Failed) ||
                    entry.PronunciationState == ResultState.Failed;
                StatusMessage = message.Status;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (entry is not null && _session.Contains(entry))
            {
                entry.Cancel();
                message?.Refresh();
                StatusMessage = "Traducción cancelada. El texto disponible se conserva.";
            }
        }
        catch (Exception error)
        {
            if (entry is not null && _session.Contains(entry))
            {
                entry.Fail(TranslationFailureMessages.CodeFor(error));
                _recoverableError = true;
                message?.Refresh();
                StatusMessage = message?.Status ?? TranslationFailureMessages.For(entry.FailureCode, IsDemonstration);
            }
            else if (!_disposed && !IsClearing)
            {
                _recoverableError = true;
                StatusMessage = TranslationFailureMessages.For(TranslationFailureMessages.CodeFor(error), IsDemonstration);
            }
        }
        finally
        {
            if (ReferenceEquals(_requestCancellation, source)) _requestCancellation = null;
            source.Dispose();
            IsTranslating = false;
            UpdateInteraction();
        }
    }

    private Task RetryMessageAsync(ConversationMessage message, CancellationToken cancellationToken)
    {
        if (!CanStartRequest || !_session.Contains(message) || message.HasTranslation || message.State != MessageState.Failed)
            return Task.CompletedTask;
        return BeginTranslation(new TextTranslationRequest(message.SourceText, message.SourceLanguage, message.TargetLanguage),
            message.Origin, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(IsTranslating))]
    private void CancelActiveTranslation() => _requestCancellation?.Cancel();

    private bool CanLoadExample() => IsDemonstration && SelectedExample is not null && !_disposed && !IsClearing &&
        !IsTranslating && !AudioInput.IsSessionActive && !AudioInput.IsBusy;

    [RelayCommand(CanExecute = nameof(CanLoadExample))]
    private void LoadExample()
    {
        if (CanLoadExample()) TextInput.Text = SelectedExample!.SourceText;
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
        _requestCancellation?.Cancel();
        _session.Clear();
        foreach (var message in _messages) message.Refresh();
        _messages.Clear();
        NotifyMessagesChanged();
        UpdateInteraction();
        try
        {
            await _playback.StopAsync();
            if (AudioInput.CancelCommand.CanExecute(null)) await AudioInput.CancelCommand.ExecuteAsync(null);
            await _activeTranslation;
            _activeTranslation = Task.CompletedTask;
            StatusMessage = "Conversación eliminada. El borrador se conserva.";
            _recoverableError = false;
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
        if (args.PropertyName is nameof(TextInput.HasText) or nameof(TextInput.IsValid)) TranslateCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguagesChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Languages.SourceLanguage)) RefreshLanguageContext();
    }

    private void RefreshLanguageContext()
    {
        TextInput.Placeholder = Languages.SourceLanguage.Language == Language.Spanish ?
            "Escribe el mensaje en español…" : "Write your message in English…";
        OnPropertyChanged(nameof(AvailableExamples));
        SelectedExample = AvailableExamples.FirstOrDefault();
    }

    private void OnAudioChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AudioInput.StatusMessage) && !_disposed && !IsClearing)
            StatusMessage = AudioInput.StatusMessage;
        if (args.PropertyName != nameof(AudioInput.State)) return;
        if (AudioInput.State == AudioInputState.Error)
        {
            _recoverableError = true;
            StatusMessage = AudioInput.StatusMessage;
        }
        else if (AudioInput.State is AudioInputState.LoadingDevices or AudioInputState.Starting or AudioInputState.Idle)
            _recoverableError = false;
        UpdateInteraction();
    }

    private void OnPlaybackChanged(object? sender, EventArgs args)
    {
        if (_playback.IsPlaying && !_playback.HasError) _recoverableError = false;
        if (_playback.HasError && !_disposed && !IsClearing)
            StatusMessage = "Hubo un problema con la reproducción. Revisa el aviso del mensaje y la salida de audio.";
        UpdateInteraction();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(HasRecoverableError));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(ShowStopPlayback));
        OnPropertyChanged(nameof(StopPlaybackLabel));
        StopPlaybackCommand.NotifyCanExecuteChanged();
    }

    private void UpdateInteraction()
    {
        NotifyStateChanged();
        Languages.IsEnabled = !_disposed && !IsClearing && !IsTranslating && !AudioInput.IsSessionActive && !AudioInput.IsBusy;
        AudioInput.IsEnabled = !_disposed && !IsClearing && !IsTranslating && !_playback.IsPlaying &&
            !_playback.IsStopping && !_playback.RequiresStopRecovery;
        TranslateCommand.NotifyCanExecuteChanged();
        CancelActiveTranslationCommand.NotifyCanExecuteChanged();
        LoadExampleCommand.NotifyCanExecuteChanged();
        RequestClearCommand.NotifyCanExecuteChanged();
        ConfirmClearCommand.NotifyCanExecuteChanged();
        foreach (var message in _messages) message.Refresh();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        UpdateInteraction();
        IsClearConfirmationOpen = false;
        TranslateCommand.Cancel();
        _requestCancellation?.Cancel();
        _session.Dispose();
        foreach (var message in _messages) message.Refresh();
        await _playback.DisposeAsync();
        await AudioInput.DisposeAsync();
        await _activeTranslation;
        _activeTranslation = Task.CompletedTask;
        if (_connection is not null)
        {
            try { await _connection.DisposeAsync(); } catch { }
        }
        _playback.StateChanged -= OnPlaybackChanged;
        AudioInput.PropertyChanged -= OnAudioChanged;
        AudioInput.TranscriptAccepted -= UseTranscript;
        TextInput.PropertyChanged -= OnTextChanged;
        Languages.PropertyChanged -= OnLanguagesChanged;
        _messages.Clear();
        TextInput.Text = string.Empty;
        NotifyMessagesChanged();
        UpdateInteraction();
    }
}
