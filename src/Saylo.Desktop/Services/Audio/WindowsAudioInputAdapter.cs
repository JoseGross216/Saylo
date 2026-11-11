using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Services.Audio;

[SupportedOSPlatform("windows")]
public sealed class WindowsAudioInputAdapter : IAudioInputAdapter
{
    private const int SampleRate = 16000;
    private const int BytesPerFrame = sizeof(short);
    private const int MaximumSeconds = 30;
    private const long BufferDuration = TimeSpan.TicksPerMillisecond * 100;
    private const int EndpointNotFound = unchecked((int)0x80070490);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile CaptureSession? _active;
    private bool _disposed;

    public string PlatformName => "Windows";
    public bool HasCaptureEnded => _active?.Completion.IsCompleted == true;

    public Task<IReadOnlyList<AudioInputDevice>> GetDevicesAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<AudioInputDevice>>(() =>
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = new List<AudioInputDevice>();
                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    using (endpoint)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        devices.Add(new AudioInputDevice(endpoint.ID, endpoint.FriendlyName));
                    }
                }
                if (devices.Count > 1)
                {
                    try
                    {
                        using var preferred = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                        var index = devices.FindIndex(device => device.Id == preferred.ID);
                        if (index > 0)
                        {
                            var device = devices[index];
                            devices.RemoveAt(index);
                            devices.Insert(0, device);
                        }
                    }
                    catch (COMException error) when (error.HResult == EndpointNotFound)
                    {
                        // Use an active endpoint when Windows has no default endpoint.
                    }
                }
                return devices;
            }
            catch (COMException error) { throw MapError(error); }
            catch (UnauthorizedAccessException) { throw new AudioInputException(AudioInputError.PermissionDenied); }
        }, cancellationToken);

    public async Task StartAsync(AudioInputDevice device, TimeSpan maximumDuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        var frames = maximumDuration.TotalSeconds * SampleRate;
        if (frames < 1 || frames > MaximumSeconds * SampleRate)
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active is not null) throw new AudioInputException(AudioInputError.CaptureFailed);
            var session = new CaptureSession(device.Id, (int)frames, maximumDuration, cancellationToken);
            _active = session;
            try
            {
                var completed = await Task.WhenAny(session.Started.Task, session.Completion).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                await EndSessionAsync(discard: true, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<AudioData> StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var audio = await EndSessionAsync(discard: false, cancellationToken).ConfigureAwait(false);
            return audio ?? throw new AudioInputException(AudioInputError.CaptureFailed);
        }
        finally { _gate.Release(); }
    }

    public async Task CancelAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await EndSessionAsync(discard: true, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            await EndSessionAsync(discard: true, CancellationToken.None).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<AudioData?> EndSessionAsync(bool discard, CancellationToken cancellationToken)
    {
        var session = _active;
        if (session is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        try
        {
            session.StopSignal.Cancel();
            var length = await session.Completion.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (discard) return null;
            if (length == 0) throw new AudioInputException(AudioInputError.CaptureFailed);
            return new AudioData(session.Buffer.AsMemory(0, length), AudioFormat.Pcm16, SampleRate, channels: 1);
        }
        catch (Exception error) when (discard && error is AudioInputException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        finally
        {
            Array.Clear(session.Buffer);
            session.StopSignal.Dispose();
            _active = null;
        }
    }

    private static int Record(CaptureSession session, string deviceId, TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDevice(deviceId);
            if (endpoint.DataFlow != DataFlow.Capture || endpoint.State != DeviceState.Active)
                throw new AudioInputException(AudioInputError.DeviceDisconnected);

            using var audioReady = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var client = endpoint.AudioClient;
            var format = new WaveFormat(SampleRate, 16, 1);
            // In shared mode, Windows converts the microphone format to the Whisper input format.
            client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.EventCallback |
                AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality |
                AudioClientStreamFlags.NoPersist, BufferDuration, 0, format, Guid.Empty);
            client.SetEventHandle(audioReady.SafeWaitHandle.DangerousGetHandle());
            var capture = client.AudioCaptureClient;
            cancellationToken.ThrowIfCancellationRequested();
            client.Start();
            try
            {
                var elapsed = Stopwatch.StartNew();
                session.Started.SetResult();
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.StopSignal.Token);
                WaitHandle[] signals = [audioReady, stop.Token.WaitHandle];
                var offset = 0;
                while (offset < session.Buffer.Length && elapsed.Elapsed < duration)
                {
                    var remaining = Math.Clamp((int)(duration - elapsed.Elapsed).TotalMilliseconds, 0, 100);
                    WaitHandle.WaitAny(signals, remaining);
                    cancellationToken.ThrowIfCancellationRequested();
                    // A stop still consumes the packets queued before the client is closed.
                    while (offset < session.Buffer.Length && capture.GetNextPacketSize() > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        offset += CopyPacket(capture, session.Buffer, offset);
                    }
                    if (stop.IsCancellationRequested) break;
                }
                cancellationToken.ThrowIfCancellationRequested();
                return offset;
            }
            finally { client.Stop(); }
        }
        catch (COMException error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw MapError(error);
        }
        catch (UnauthorizedAccessException) { throw new AudioInputException(AudioInputError.PermissionDenied); }
    }

    private static int CopyPacket(AudioCaptureClient capture, byte[] buffer, int offset)
    {
        var data = capture.GetBuffer(out var frames, out var flags);
        try
        {
            var bytes = Math.Min(frames * BytesPerFrame, buffer.Length - offset);
            if (bytes > 0 && (flags & AudioClientBufferFlags.Silent) == 0)
                Marshal.Copy(data, buffer, offset, bytes);
            return bytes;
        }
        finally { capture.ReleaseBuffer(frames); }
    }

    private static AudioInputException MapError(COMException error) => new(error.HResult switch
    {
        unchecked((int)0x80070005) => AudioInputError.PermissionDenied, // E_ACCESSDENIED
        unchecked((int)0x88890004) or // AUDCLNT_E_DEVICE_INVALIDATED
        unchecked((int)0x88890026) or // AUDCLNT_E_RESOURCES_INVALIDATED
        unchecked((int)0x80070490) => AudioInputError.DeviceDisconnected, // ERROR_NOT_FOUND
        _ => AudioInputError.CaptureFailed
    });

    private sealed class CaptureSession
    {
        public CaptureSession(string deviceId, int frames, TimeSpan duration, CancellationToken cancellationToken)
        {
            Buffer = new byte[frames * BytesPerFrame];
            // Keep GetBuffer, ReleaseBuffer, and the COM objects on the same worker thread.
            Completion = Task.Run(() => Record(this, deviceId, duration, cancellationToken));
        }

        public byte[] Buffer { get; }
        public CancellationTokenSource StopSignal { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<int> Completion { get; }
    }
}
