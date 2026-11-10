using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Services.Audio;

/// <summary>Composition seam for native adapters; no fictitious devices or silent recordings.</summary>
public static class AudioInputAdapterFactory
{
    public static IAudioInputAdapter CreateDefault() => new UnconfiguredAudioInputAdapter(
        OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" :
        OperatingSystem.IsLinux() ? "Linux" : "este sistema");
}

public sealed class UnconfiguredAudioInputAdapter(string platformName) : IAudioInputAdapter
{
    public string PlatformName { get; } = platformName;
    public Task<IReadOnlyList<AudioInputDevice>> GetDevicesAsync(CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<AudioInputDevice>>(new AudioInputException(AudioInputError.NotConfigured));
    public Task StartAsync(AudioInputDevice device, TimeSpan maximumDuration, CancellationToken cancellationToken) =>
        Task.FromException(new AudioInputException(AudioInputError.NotConfigured));
    public Task<AudioData> StopAsync(CancellationToken cancellationToken) =>
        Task.FromException<AudioData>(new AudioInputException(AudioInputError.NotConfigured));
    public Task CancelAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
