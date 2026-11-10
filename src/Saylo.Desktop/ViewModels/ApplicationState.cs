namespace Saylo.Desktop.ViewModels;

public enum ApplicationState
{
    Ready,
    LoadingDevices,
    PreparingMicrophone,
    Recording,
    StoppingMicrophone,
    Transcribing,
    ReviewingTranscript,
    Translating,
    PreparingResults,
    Playing,
    StoppingPlayback,
    Clearing,
    RecoverableError,
    Closing
}
