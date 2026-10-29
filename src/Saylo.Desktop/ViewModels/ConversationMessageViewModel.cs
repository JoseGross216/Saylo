using System;
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

public enum ChatPlaybackState { Ready, Playing, Stopped, Failed }

public partial class ConversationMessageViewModel : ViewModelBase
{
    private readonly IClipboardService _clipboard;
    private readonly ChatPlaybackController _playback;
    private readonly Func<bool> _canInteract;
    private ChatPlaybackState _playbackState;
    private readonly Func<ConversationMessage, CancellationToken, Task>? _retry;
    private readonly Func<bool>? _canRetry;
    public bool IsDemonstration { get; }
    public ConversationMessage Entry { get; }

    public ConversationMessageViewModel(ConversationMessage entry, IClipboardService clipboard,
        ChatPlaybackController playback, Func<bool> canInteract, bool isDemonstration = false,
        Func<ConversationMessage, CancellationToken, Task>? retry = null, Func<bool>? canRetry = null)
    {
        Entry = entry;
        _clipboard = clipboard;
        _playback = playback;
        _canInteract = canInteract;
        IsDemonstration = isDemonstration;
        _retry = retry;
        _canRetry = canRetry;
    }

    public string SourceText => Entry.SourceText;
    public string TranslatedText => Entry.TranslatedText;
    public string DirectionLabel => $"{Entry.SourceLanguage.Code.ToUpperInvariant()} → {Entry.TargetLanguage.Code.ToUpperInvariant()}";
    public string OriginLabel => Entry.Origin switch
    {
        MessageOrigin.ReviewedVoice => "Voz revisada",
        MessageOrigin.Mixed => "Texto y voz revisada",
        _ => "Texto escrito"
    };
    public Guid? OperationId => Entry.OperationId;
    public AudioData? Audio => Entry.Audio;
    public bool HasTranslation => Entry.HasTranslation;
    public string OriginalLabel => $"ORIGINAL · {LanguageName(Entry.SourceLanguage)}";
    public string TranslationLabel => $"TRADUCCIÓN · {LanguageName(Entry.TargetLanguage)}";
    private static string LanguageName(Language language) => language == Language.Spanish ? "Español" : "Inglés";
    public string PronunciationText => Entry.Pronunciation?.Text ?? string.Empty;
    public string PronunciationLabel => Entry.PronunciationState switch
    {
        ResultState.Ready when Entry.Pronunciation?.Notation == PronunciationNotation.Ipa => "Pronunciación · IPA",
        ResultState.Ready => IsDemonstration ? "Pronunciación · guía aproximada de ejemplo" : "Pronunciación · guía aproximada",
        ResultState.Pending => "Preparando pronunciación…",
        _ => "Pronunciación no disponible"
    };
    public string Status => Entry.State switch
    {
        MessageState.Translating => "Traduciendo…",
        MessageState.Enriching => "Texto disponible",
        MessageState.Ready => "Traducción disponible",
        MessageState.Canceled => "Operación cancelada",
        _ when HasTranslation => "La operación falló. El contenido disponible se conserva.",
        _ => TranslationFailureMessages.For(Entry.FailureCode, IsDemonstration)
    };
    public string AudioStatus => Entry.AudioState switch
    {
        _ when IsDemonstration => "La demostración no genera audio.",
        ResultState.Pending => "Generando voz…",
        ResultState.Failed => "No se pudo generar la voz",
        ResultState.Ready when !_playback.IsAvailable => "Reproducción local no disponible. El texto se conserva.",
        ResultState.Ready when _playbackState == ChatPlaybackState.Playing => "Reproduciendo voz local…",
        ResultState.Ready when _playbackState == ChatPlaybackState.Failed => "No se pudo reproducir. Pulsa Repetir audio para reintentar.",
        ResultState.Ready when _playbackState == ChatPlaybackState.Stopped => "Audio detenido · disponible para repetir",
        ResultState.Ready => "Voz lista · disponible para repetir",
        _ => "Voz no disponible"
    };
    public bool IsPlaying => _playbackState == ChatPlaybackState.Playing && !Entry.IsErased;
    public bool CanPlay => Audio is not null && !Entry.IsErased && _canInteract() && _playback.IsAvailable;
    private bool CanRepeat => CanPlay && !IsPlaying;
    private bool CanStop => IsPlaying;
    private bool CanCopy => HasTranslation && !Entry.IsErased && _canInteract() && _clipboard.IsAvailable;
    public bool ShowRetry => !Entry.IsErased && Entry.State == MessageState.Failed && !HasTranslation && _retry is not null &&
        !(IsDemonstration && Entry.FailureCode == TranslationErrorCode.EngineFailure);
    private bool CanRetry => ShowRetry && _canInteract() && (_canRetry?.Invoke() ?? false);

    [ObservableProperty] private string _copyStatus = string.Empty;

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private async Task CopyAsync()
    {
        if (!CanCopy) return;
        var text = TranslatedText;
        try
        {
            await _clipboard.SetTextAsync(text);
            if (!Entry.IsErased) CopyStatus = "Traducción copiada";
        }
        catch { if (!Entry.IsErased) CopyStatus = "No se pudo copiar. Puedes reintentar."; }
    }

    [RelayCommand(CanExecute = nameof(CanRepeat))]
    private Task RepeatAsync() => CanRepeat ? _playback.PlayAsync(this, CancellationToken.None) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAudioAsync() => _playback.StopAsync();

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryTranslationAsync(CancellationToken cancellationToken) =>
        CanRetry ? _retry!(Entry, cancellationToken) : Task.CompletedTask;

    public void SetPlaybackState(ChatPlaybackState state)
    {
        if (Entry.IsErased) return;
        _playbackState = state;
        Refresh();
    }

    public void Refresh()
    {
        if (Entry.IsErased) CopyStatus = string.Empty;
        foreach (var name in new[] { nameof(SourceText), nameof(TranslatedText), nameof(HasTranslation),
            nameof(PronunciationText), nameof(PronunciationLabel), nameof(AudioStatus), nameof(Status),
            nameof(Audio), nameof(OperationId), nameof(IsPlaying), nameof(ShowRetry) }) OnPropertyChanged(name);
        CopyCommand.NotifyCanExecuteChanged();
        RepeatCommand.NotifyCanExecuteChanged();
        StopAudioCommand.NotifyCanExecuteChanged();
        RetryTranslationCommand.NotifyCanExecuteChanged();
    }
}
