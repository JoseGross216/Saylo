using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Services.Audio;

public sealed record AudioInputDevice(string Id, string Name);

public enum AudioInputError { NotConfigured, PermissionDenied, NoDevice, DeviceDisconnected, CaptureFailed }

public sealed class AudioInputException(AudioInputError code) : Exception
{
    public AudioInputError Code { get; } = code;
}

/// <summary>
/// Native Windows/macOS/Linux implementations belong behind this boundary. Enumerating must
/// not activate the microphone or request permission. Start requests permission on explicit use.
/// Capture only the selected microphone, never loopback. Enforce maximumDuration in the native
/// layer too; return nonempty PCM16 in memory. Honor cancellation, including device removal.
/// Cancel and Dispose must release devices and discard buffers, and be safe after a failed start.
/// </summary>
public interface IAudioInputAdapter : IAsyncDisposable
{
    string PlatformName { get; }
    Task<IReadOnlyList<AudioInputDevice>> GetDevicesAsync(CancellationToken cancellationToken);
    Task StartAsync(AudioInputDevice device, TimeSpan maximumDuration, CancellationToken cancellationToken);
    Task<AudioData> StopAsync(CancellationToken cancellationToken);
    Task CancelAsync(CancellationToken cancellationToken);
}
