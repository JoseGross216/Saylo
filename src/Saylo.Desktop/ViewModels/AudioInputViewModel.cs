using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Saylo.Desktop.Services.Audio;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.ViewModels;

public enum AudioInputState { Idle, LoadingDevices, Starting, Recording, Stopping, Transcribing, Reviewing, Canceling, Error }

public partial class AudioInputViewModel : ViewModelBase, IAsyncDisposable
{
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(30);
    private readonly IAudioInputAdapter _adapter;
    private readonly ISpeechRecognitionEngine? _recognizer;
    private readonly Func<Language> _sourceLanguage;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _session;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _timer;
    private Task _monitor = Task.CompletedTask;
    private bool _disposed;
    private bool _cleanupFailed;
    private long _startedAt;

    public AudioInputViewModel(IAudioInputAdapter adapter, Func<Language> sourceLanguage,
        ISpeechRecognitionEngine? recognizer = null, TimeProvider? timeProvider = null)
    {
        _adapter = adapter;
        _sourceLanguage = sourceLanguage;
        _recognizer = recognizer;
        _time = timeProvider ?? TimeProvider.System;
    }

    public ObservableCollection<AudioInputDevice> Devices { get; } = new();
    public string PlatformName => _adapter.PlatformName;
    public event Action<string>? TranscriptAccepted;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private AudioInputDevice? _selectedDevice;

    [ObservableProperty]
    private string _statusMessage = "Actualiza la lista para consultar los micrófonos disponibles.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseTranscript))]
    [NotifyCanExecuteChangedFor(nameof(AcceptTranscriptCommand))]
    private string? _transcript = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationLabel))]
    [NotifyPropertyChangedFor(nameof(ElapsedSeconds))]
    private TimeSpan _elapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseDevice))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshDevicesCommand))]
    private bool _isEnabled = true;

    public AudioInputState State { get; private set; } = AudioInputState.Idle;
    public bool IsSessionActive => State is AudioInputState.Starting or AudioInputState.Recording or
        AudioInputState.Stopping or AudioInputState.Transcribing or AudioInputState.Reviewing or AudioInputState.Canceling;
    public bool CanChooseDevice => CanRefresh && Devices.Count > 0;
    public bool IsRecording => State == AudioInputState.Recording;
    public bool IsReviewing => State == AudioInputState.Reviewing;
    public bool IsBusy => State is AudioInputState.LoadingDevices or AudioInputState.Starting or
        AudioInputState.Stopping or AudioInputState.Transcribing or AudioInputState.Canceling;
    public bool CanUseTranscript => IsReviewing && !string.IsNullOrWhiteSpace(Transcript);
    public double ElapsedSeconds => Elapsed.TotalSeconds;
    public string DurationLabel => $@"{Elapsed:mm\:ss} / 00:30";
    private bool CanStart => IsEnabled && CanChooseDevice && !_cleanupFailed &&
        SelectedDevice is not null && Devices.Contains(SelectedDevice);
    private bool CanRefresh => IsEnabled && !_disposed && !IsSessionActive && State != AudioInputState.LoadingDevices;
    private bool CanStop => IsRecording;
    private bool CanCancel => !_disposed && IsSessionActive && State != AudioInputState.Canceling;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshDevicesAsync()
    {
        if (!CanRefresh) return;
        ChangeState(AudioInputState.LoadingDevices);
        var previousId = SelectedDevice?.Id;
        try
        {
            if (_cleanupFailed)
            {
                await ReleaseCaptureAsync();
                if (_cleanupFailed)
                {
                    ShowCleanupError();
                    return;
                }
            }
            var devices = await _adapter.GetDevicesAsync(_lifetime.Token);
            if (_disposed) return;
            Devices.Clear();
            foreach (var device in devices)
                Devices.Add(device);
            SelectedDevice = null;
            foreach (var device in Devices)
                if (device.Id == previousId) SelectedDevice = device;
            SelectedDevice ??= Devices.Count > 0 ? Devices[0] : null;
            StatusMessage = Devices.Count == 0 ? "No se encontró un micrófono. Conecta uno y actualiza la lista." :
                "Micrófono listo. La grabación comienza solo cuando pulses Grabar.";
            ChangeState(AudioInputState.Idle);
        }
        catch (Exception error)
        {
            if (_disposed) return;
            Devices.Clear();
            SelectedDevice = null;
            ShowError(error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (!CanStart) return;
        var device = SelectedDevice!;
        var language = _sourceLanguage();
        _session?.Dispose();
        var session = _session = new CancellationTokenSource();
        Transcript = string.Empty;
        Elapsed = TimeSpan.Zero;
        ChangeState(AudioInputState.Starting);
        StatusMessage = "Preparando el micrófono…";
        await _gate.WaitAsync();
        try
        {
            await _adapter.StartAsync(device, MaximumDuration, session.Token);
            session.Token.ThrowIfCancellationRequested();
            _recordedLanguage = language;
            _startedAt = _time.GetTimestamp();
            ChangeState(AudioInputState.Recording);
            StatusMessage = "Grabando tu micrófono. Detén la grabación cuando termines.";
            _timer?.Dispose();
            _timer = new CancellationTokenSource();
            _monitor = MonitorDurationAsync(_timer.Token);
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        catch (Exception error)
        {
            await ReleaseCaptureAsync();
            if (State != AudioInputState.Canceling) ShowError(error);
        }
        finally { _gate.Release(); }
    }

    private Language _recordedLanguage = Language.Spanish;

    private async Task MonitorDurationAsync(CancellationToken token)
    {
        try
        {
            using var ticks = new PeriodicTimer(TimeSpan.FromMilliseconds(100), _time);
            while (await ticks.WaitForNextTickAsync(token))
            {
                if (!IsRecording) return;
                var elapsed = _time.GetElapsedTime(_startedAt);
                Elapsed = elapsed < MaximumDuration ? elapsed : MaximumDuration;
                if (elapsed >= MaximumDuration)
                {
                    await StopCommand.ExecuteAsync(null);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (!CanStop) return;
        var session = _session!;
        _timer?.Cancel();
        ChangeState(AudioInputState.Stopping);
        StatusMessage = "Finalizando la grabación…";
        await _gate.WaitAsync();
        var released = false;
        try
        {
            var audio = await _adapter.StopAsync(session.Token);
            // AudioData owns its bytes; release the microphone before running inference.
            await ReleaseCaptureAsync();
            released = !_cleanupFailed;
            if (!released)
            {
                ShowCleanupError();
                return;
            }
            session.Token.ThrowIfCancellationRequested();
            var seconds = audio.Data.Length / (audio.SampleRate * (double)audio.Channels * 2);
            if (seconds > MaximumDuration.TotalSeconds)
                throw new AudioInputException(AudioInputError.CaptureFailed);
            if (_recognizer is null)
            {
                StatusMessage = "La transcripción aún no está configurada. Puedes escribir el mensaje.";
                ChangeState(AudioInputState.Error);
                return;
            }
            ChangeState(AudioInputState.Transcribing);
            StatusMessage = "Transcribiendo la grabación en este equipo…";
            var text = await _recognizer.TranscribeAsync(audio, _recordedLanguage, session.Token);
            session.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text))
                throw new AudioInputException(AudioInputError.CaptureFailed);
            Transcript = text;
            StatusMessage = "Revisa y corrige la transcripción antes de usarla.";
            ChangeState(AudioInputState.Reviewing);
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (State != AudioInputState.Canceling) ShowError(error);
        }
        finally
        {
            if (!released)
            {
                await ReleaseCaptureAsync();
                if (_cleanupFailed) ShowCleanupError();
            }
            _gate.Release();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        if (!CanCancel) return;
        ChangeState(AudioInputState.Canceling);
        await CancelSessionAsync();
        Transcript = string.Empty;
        Elapsed = TimeSpan.Zero;
        if (_cleanupFailed)
        {
            ShowCleanupError();
            return;
        }
        StatusMessage = "Grabación descartada. Puedes grabar de nuevo o escribir.";
        ChangeState(AudioInputState.Idle);
    }

    public string TakeTranscript()
    {
        if (!CanUseTranscript) throw new InvalidOperationException("No reviewed transcript is available.");
        var text = Transcript!;
        Transcript = string.Empty;
        StatusMessage = "Transcripción añadida al mensaje. Puedes editarla antes de traducir.";
        ChangeState(AudioInputState.Idle);
        return text;
    }

    [RelayCommand(CanExecute = nameof(CanUseTranscript))]
    private void AcceptTranscript()
    {
        if (CanUseTranscript) TranscriptAccepted?.Invoke(TakeTranscript());
    }

    private async Task CancelSessionAsync()
    {
        _timer?.Cancel();
        if (_session is not null) await _session.CancelAsync();
        await _gate.WaitAsync();
        try { await ReleaseCaptureAsync(); }
        finally { _gate.Release(); }
    }

    private async Task ReleaseCaptureAsync()
    {
        try
        {
            await _adapter.CancelAsync(CancellationToken.None);
            _cleanupFailed = false;
        }
        catch { _cleanupFailed = true; }
    }

    private void ShowCleanupError()
    {
        StatusMessage = "No se pudo liberar el micrófono. Pulsa Actualizar para reintentar o cierra la aplicación.";
        ChangeState(AudioInputState.Error);
    }

    private void ShowError(Exception error)
    {
        StatusMessage = (error as AudioInputException)?.Code switch
        {
            AudioInputError.NotConfigured => $"Captura de audio pendiente de configurar en {PlatformName}.",
            AudioInputError.PermissionDenied => "Permiso de micrófono denegado. Revisa los permisos del sistema y reintenta.",
            AudioInputError.NoDevice => "No hay micrófono disponible. Conecta uno y actualiza la lista.",
            AudioInputError.DeviceDisconnected => "El micrófono se desconectó. Selecciona uno disponible y reintenta.",
            _ => "No se pudo completar la captura o transcripción. Reintenta o escribe el mensaje."
        };
        ChangeState(AudioInputState.Error);
    }

    private void ChangeState(AudioInputState state)
    {
        State = state;
        foreach (var name in new[] { nameof(State), nameof(IsSessionActive), nameof(CanChooseDevice),
            nameof(IsRecording), nameof(IsReviewing), nameof(IsBusy), nameof(CanUseTranscript) })
            OnPropertyChanged(name);
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RefreshDevicesCommand.NotifyCanExecuteChanged();
        AcceptTranscriptCommand.NotifyCanExecuteChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _lifetime.CancelAsync();
        ChangeState(AudioInputState.Canceling);
        await CancelSessionAsync();
        await _monitor;
        if (CancelCommand.ExecutionTask is { } cancel) await cancel;
        if (StartCommand.ExecutionTask is { } start) await start;
        if (StopCommand.ExecutionTask is { } stop) await stop;
        if (RefreshDevicesCommand.ExecutionTask is { } refresh) await refresh;
        _session?.Dispose();
        _timer?.Dispose();
        _lifetime.Dispose();
        Transcript = string.Empty;
        try { await _adapter.DisposeAsync(); }
        catch { /* Shutdown must not surface provider details or retain conversation data. */ }
    }
}
