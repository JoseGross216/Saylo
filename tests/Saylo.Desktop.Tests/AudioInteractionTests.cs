using Saylo.Desktop.Services.Audio;
using Saylo.Desktop.ViewModels;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Tests;

public sealed class AudioInteractionTests
{
    [Fact]
    public async Task DefaultCompositionHasNoFictitiousDevicesAndKeepsTextUsable()
    {
        await using var model = new MainViewModel();
        await model.InitializeAsync();

        Assert.Empty(model.AudioInput.Devices);
        Assert.False(model.AudioInput.StartCommand.CanExecute(null));
        Assert.Contains("pendiente de configurar", model.AudioInput.StatusMessage);
        Assert.True(model.Languages.IsEnabled);
        model.TextInput.Text = "Buenos días";
        Assert.True(model.TextInput.HasText);
        Assert.False(model.TranslateCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshKeepsTheSelectedDeviceByIdAndDoesNotStartCapture()
    {
        var adapter = new TestAudioAdapter();
        await using var model = new MainViewModel(adapter);
        await model.InitializeAsync();
        model.AudioInput.SelectedDevice = model.AudioInput.Devices[1];
        adapter.Devices = [new("usb", "USB actualizado"), new("internal", "Interno actualizado")];

        await model.AudioInput.RefreshDevicesCommand.ExecuteAsync(null);

        Assert.Equal("internal", model.AudioInput.SelectedDevice!.Id);
        Assert.Equal("Interno actualizado", model.AudioInput.SelectedDevice.Name);
        Assert.Equal(0, adapter.Starts);
    }

    [Fact]
    public async Task CaptureUsesSelectedDeviceAndLanguageAndRequiresReviewBeforeUsingText()
    {
        var adapter = new TestAudioAdapter();
        var recognizer = new TestRecognizer();
        await using var model = new MainViewModel(adapter, recognizer);
        await model.InitializeAsync();
        model.Languages.SwapCommand.Execute(null);
        model.TextInput.Text = "Existing draft";
        model.AudioInput.SelectedDevice = model.AudioInput.Devices[1];

        await model.AudioInput.StartCommand.ExecuteAsync(null);
        Assert.False(model.Languages.IsEnabled);
        Assert.False(model.Languages.SwapCommand.CanExecute(null));
        Assert.False(model.AudioInput.CanChooseDevice);
        model.Languages.SourceLanguage = Saylo.Desktop.Models.LanguageOption.Spanish;
        Assert.Equal(Language.English, model.Languages.SourceLanguage.Language);
        await model.AudioInput.StopCommand.ExecuteAsync(null);

        Assert.Equal("internal", adapter.StartedDevice!.Id);
        Assert.Equal(TimeSpan.FromSeconds(30), adapter.Limit);
        Assert.Equal(Language.English, recognizer.Language);
        Assert.Equal(AudioInputState.Reviewing, model.AudioInput.State);
        Assert.Equal("Existing draft", model.TextInput.Text);
        model.AudioInput.Transcript = "Corrected transcription";
        model.AudioInput.AcceptTranscriptCommand.Execute(null);

        Assert.Equal($"Existing draft{Environment.NewLine}Corrected transcription", model.TextInput.Text);
        Assert.Empty(model.AudioInput.Transcript!);
        Assert.True(model.Languages.IsEnabled);
        Assert.Equal(AudioInputState.Idle, model.AudioInput.State);
    }

    [Fact]
    public async Task CancelDuringStartReleasesTheAdapterAndDoesNotBeginRecordingLater()
    {
        var adapter = new TestAudioAdapter { HoldStart = true };
        await using var model = new MainViewModel(adapter, new TestRecognizer());
        await model.InitializeAsync();
        var start = model.AudioInput.StartCommand.ExecuteAsync(null);
        await adapter.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await model.AudioInput.CancelCommand.ExecuteAsync(null);
        await start;

        Assert.Equal(AudioInputState.Idle, model.AudioInput.State);
        Assert.True(adapter.Cancels > 0);
        Assert.Equal(0, adapter.Stops);
        Assert.True(model.Languages.IsEnabled);
    }

    [Fact]
    public async Task CancelDuringTranscriptionDiscardsAudioAndLateText()
    {
        var adapter = new TestAudioAdapter();
        var recognizer = new TestRecognizer { HoldRecognition = true };
        await using var model = new MainViewModel(adapter, recognizer);
        await model.InitializeAsync();
        await model.AudioInput.StartCommand.ExecuteAsync(null);
        var stop = model.AudioInput.StopCommand.ExecuteAsync(null);
        await recognizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await model.AudioInput.CancelCommand.ExecuteAsync(null);
        await stop;

        Assert.Equal(AudioInputState.Idle, model.AudioInput.State);
        Assert.Empty(model.AudioInput.Transcript!);
        Assert.False(model.AudioInput.AcceptTranscriptCommand.CanExecute(null));
        Assert.False(model.TextInput.HasText);
    }

    [Theory]
    [InlineData(AudioInputError.PermissionDenied, "Permiso")]
    [InlineData(AudioInputError.DeviceDisconnected, "desconectó")]
    public async Task NativeFailuresHaveRecoverableMessagesAndUnlockLanguages(AudioInputError code, string expected)
    {
        var adapter = new TestAudioAdapter { StartError = code };
        await using var model = new MainViewModel(adapter);
        await model.InitializeAsync();

        await model.AudioInput.StartCommand.ExecuteAsync(null);

        Assert.Equal(AudioInputState.Error, model.AudioInput.State);
        Assert.Contains(expected, model.AudioInput.StatusMessage);
        Assert.True(model.Languages.IsEnabled);
        Assert.True(adapter.Cancels > 0);
        Assert.True(model.AudioInput.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task ThirtySecondLimitStopsAndTranscribesWithoutWaitingForARealClock()
    {
        var clock = new ManualRecordingClock();
        var adapter = new TestAudioAdapter();
        await using var model = new MainViewModel(adapter, new TestRecognizer(), timeProvider: clock);
        await model.InitializeAsync();
        await model.AudioInput.StartCommand.ExecuteAsync(null);
        var reviewed = WaitForState(model.AudioInput, AudioInputState.Reviewing);

        clock.Advance(TimeSpan.FromSeconds(30));
        await reviewed;

        Assert.Equal(1, adapter.Stops);
        Assert.Equal("00:30 / 00:30", model.AudioInput.DurationLabel);
        Assert.False(model.AudioInput.IsRecording);
    }

    [Fact]
    public async Task ClosingDuringStartCancelsAndDisposesTheAdapter()
    {
        var adapter = new TestAudioAdapter { HoldStart = true };
        var model = new MainViewModel(adapter);
        await model.InitializeAsync();
        var start = model.AudioInput.StartCommand.ExecuteAsync(null);
        await adapter.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await model.DisposeAsync();
        await start;

        Assert.True(adapter.Disposed);
        Assert.False(model.AudioInput.StartCommand.CanExecute(null));
        Assert.Empty(model.Messages);
    }

    [Fact]
    public async Task EmptyEditedTranscriptCannotBeAccepted()
    {
        await using var model = new MainViewModel(new TestAudioAdapter(), new TestRecognizer());
        await model.InitializeAsync();
        await model.AudioInput.StartCommand.ExecuteAsync(null);
        await model.AudioInput.StopCommand.ExecuteAsync(null);
        model.AudioInput.Transcript = "   ";

        Assert.False(model.AudioInput.AcceptTranscriptCommand.CanExecute(null));
        Assert.Throws<InvalidOperationException>(() => model.AudioInput.TakeTranscript());
    }

    [Fact]
    public async Task CleanupFailureBlocksNewRecordingUntilARefreshReleasesTheDevice()
    {
        var adapter = new TestAudioAdapter();
        await using var model = new MainViewModel(adapter);
        await model.InitializeAsync();
        await model.AudioInput.StartCommand.ExecuteAsync(null);
        adapter.FailCleanup = true;

        await model.AudioInput.CancelCommand.ExecuteAsync(null);

        Assert.Equal(AudioInputState.Error, model.AudioInput.State);
        Assert.Contains("liberar", model.AudioInput.StatusMessage);
        Assert.False(model.AudioInput.StartCommand.CanExecute(null));
        adapter.FailCleanup = false;
        await model.AudioInput.RefreshDevicesCommand.ExecuteAsync(null);
        Assert.True(model.AudioInput.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeviceRemovalOnRefreshClearsAStaleSelection()
    {
        var adapter = new TestAudioAdapter();
        await using var model = new MainViewModel(adapter);
        await model.InitializeAsync();
        adapter.Devices = [];

        await model.AudioInput.RefreshDevicesCommand.ExecuteAsync(null);

        Assert.Null(model.AudioInput.SelectedDevice);
        Assert.False(model.AudioInput.StartCommand.CanExecute(null));
        Assert.Contains("No se encontró", model.AudioInput.StatusMessage);
    }

    [Fact]
    public async Task MicrophoneIsReleasedBeforeTranscriptionAndReviewAreEnabled()
    {
        var adapter = new TestAudioAdapter { HoldCleanup = true };
        var recognizer = new TestRecognizer();
        await using var model = new MainViewModel(adapter, recognizer);
        await model.InitializeAsync();
        await model.AudioInput.StartCommand.ExecuteAsync(null);
        var stop = model.AudioInput.StopCommand.ExecuteAsync(null);
        await adapter.CleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal(AudioInputState.Stopping, model.AudioInput.State);
            Assert.False(recognizer.Entered.Task.IsCompleted);
            Assert.False(model.AudioInput.CanUseTranscript);
        }
        finally { adapter.ReleaseCleanup.TrySetResult(); }
        await stop;
        Assert.Equal(AudioInputState.Reviewing, model.AudioInput.State);
    }

    private static async Task WaitForState(AudioInputViewModel model, AudioInputState state)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (model.State == state) completed.TrySetResult();
        }
        model.PropertyChanged += Changed;
        try
        {
            if (model.State == state) return;
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { model.PropertyChanged -= Changed; }
    }
}

internal sealed class TestAudioAdapter : IAudioInputAdapter
{
    public string PlatformName => "Test platform";
    internal IReadOnlyList<AudioInputDevice> Devices { get; set; } = [new("usb", "USB"), new("internal", "Interno")];
    internal int Starts, Stops, Cancels;
    internal bool Disposed, HoldStart;
    internal bool FailCleanup;
    internal bool HoldCleanup;
    internal TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal AudioInputError? StartError;
    internal AudioInputDevice? StartedDevice;
    internal TimeSpan Limit;
    internal TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<IReadOnlyList<AudioInputDevice>> GetDevicesAsync(CancellationToken cancellationToken) => Task.FromResult(Devices);
    public async Task StartAsync(AudioInputDevice device, TimeSpan maximumDuration, CancellationToken cancellationToken)
    {
        Starts++;
        StartedDevice = device;
        Limit = maximumDuration;
        StartEntered.TrySetResult();
        if (StartError is { } error) throw new AudioInputException(error);
        if (HoldStart) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
    public Task<AudioData> StopAsync(CancellationToken cancellationToken)
    {
        Stops++;
        return Task.FromResult(new AudioData(new byte[3200], AudioFormat.Pcm16, 16000, 1));
    }
    public async Task CancelAsync(CancellationToken cancellationToken)
    {
        Cancels++;
        CleanupEntered.TrySetResult();
        if (FailCleanup) throw new InvalidOperationException("provider detail");
        if (HoldCleanup) await ReleaseCleanup.Task.WaitAsync(cancellationToken);
    }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}

internal sealed class TestRecognizer : ISpeechRecognitionEngine
{
    internal bool HoldRecognition;
    internal Language? Language;
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<string> TranscribeAsync(AudioData audio, Language sourceLanguage, CancellationToken cancellationToken = default)
    {
        Language = sourceLanguage;
        Entered.TrySetResult();
        if (HoldRecognition) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return "Recognized text";
    }
}

internal sealed class ManualRecordingClock : TimeProvider
{
    private long _timestamp;
    private readonly List<ManualTimer> _timers = [];
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state);
        _timers.Add(timer);
        return timer;
    }
    internal void Advance(TimeSpan elapsed)
    {
        _timestamp += elapsed.Ticks;
        foreach (var timer in _timers.ToArray()) timer.Tick();
    }
    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        internal void Tick() { if (!_disposed) callback(state); }
    }
}
