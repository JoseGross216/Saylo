using System;
using System.Threading;
using System.Threading.Tasks;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Services.Audio;

/// <summary>Play ends after playback finishes. Cancellation/Stop must stop sound and release buffers.</summary>
public interface IAudioPlaybackAdapter : IAsyncDisposable
{
    bool IsAvailable { get; }
    Task PlayAsync(AudioData audio, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public sealed class UnavailableAudioPlaybackAdapter : IAudioPlaybackAdapter
{
    public bool IsAvailable => false;
    public Task PlayAsync(AudioData audio, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException("Local playback is unavailable."));
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public static class AudioPlaybackAdapterFactory
{
    public static IAudioPlaybackAdapter CreateDefault() => OperatingSystem.IsWindows()
        ? new WindowsAudioPlaybackAdapter() : new UnavailableAudioPlaybackAdapter();
}
