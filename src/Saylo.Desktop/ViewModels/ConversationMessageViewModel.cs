using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.ViewModels;

/// <summary>Session-only visual projection; partial failures never remove delivered text.</summary>
public partial class ConversationMessageViewModel(TextTranslationRequest request) : ViewModelBase
{
    public string SourceText { get; } = request.Text;
    public string DirectionLabel { get; } = $"{request.SourceLanguage.Code.ToUpperInvariant()} → {request.TargetLanguage.Code.ToUpperInvariant()}";
    public Guid? OperationId { get; private set; }
    public AudioData? Audio { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTranslation))]
    private string _translatedText = string.Empty;
    [ObservableProperty] private string _pronunciationText = string.Empty;
    [ObservableProperty] private string _pronunciationLabel = "Pronunciación pendiente";
    [ObservableProperty] private string _audioStatus = "Voz pendiente";
    [ObservableProperty] private string _status = "Traduciendo…";

    public bool HasTranslation => !string.IsNullOrWhiteSpace(TranslatedText);

    public void Apply(TranslationUpdate update)
    {
        if (OperationId is { } id && id != update.OperationId) return;
        OperationId ??= update.OperationId;
        switch (update)
        {
            case TextTranslationCompleted text:
                TranslatedText = text.TranslatedText;
                Status = "Texto disponible";
                break;
            case PronunciationCompleted pronunciation:
                PronunciationText = pronunciation.Pronunciation.Text;
                PronunciationLabel = pronunciation.Pronunciation.Notation == PronunciationNotation.Ipa
                    ? "Pronunciación · IPA" : "Pronunciación · guía aproximada";
                break;
            case SpeechSynthesisCompleted speech:
                Audio = speech.Audio;
                AudioStatus = "Voz generada · reproducción pendiente de configurar";
                break;
            case TranslationStageFailed error when error.Stage == TranslationStage.Pronunciation:
                PronunciationLabel = "Pronunciación no disponible";
                break;
            case TranslationStageFailed error when error.Stage == TranslationStage.SpeechSynthesis:
                AudioStatus = "No se pudo generar la voz";
                break;
            case TranslationStageFailed:
                Status = "No se pudo traducir. Conserva el mensaje y reintenta.";
                break;
        }
    }

    public void Complete()
    {
        if (PronunciationLabel == "Pronunciación pendiente") PronunciationLabel = "Pronunciación no disponible";
        if (AudioStatus == "Voz pendiente") AudioStatus = "Voz no disponible";
        if (HasTranslation) Status = "Traducción disponible";
        else if (Status == "Traduciendo…") Status = "No se recibió una traducción. Reintenta.";
    }

    public void ReleaseAudio() => Audio = null;
}
