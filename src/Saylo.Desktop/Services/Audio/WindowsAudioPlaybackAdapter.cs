using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Services.Audio;

/// <summary>Local PCM16 playback in memory, using the Windows default output device.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAudioPlaybackAdapter : IAudioPlaybackAdapter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _active;
    private bool _disposed;
    public bool IsAvailable => !_disposed;

    public async Task PlayAsync(AudioData audio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        await _gate.WaitAsync(cancellationToken);
        CancellationTokenSource? operation = null;
        try
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _active = operation;
            }
            await Task.Run(() => PlayPcm(audio, operation.Token), CancellationToken.None);
        }
        finally
        {
            lock (_sync)
            {
                _active = null;
                operation?.Dispose();
            }
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task cancel;
        lock (_sync) cancel = _active?.CancelAsync() ?? Task.CompletedTask;
        await cancel;
        await _gate.WaitAsync(cancellationToken);
        _gate.Release();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync) _disposed = true;
        await StopAsync(CancellationToken.None);
    }

    private static void PlayPcm(AudioData audio, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var format = new WaveFormat
        {
            FormatTag = 1,
            Channels = checked((ushort)audio.Channels),
            SamplesPerSecond = checked((uint)audio.SampleRate),
            BlockAlign = checked((ushort)(audio.Channels * 2)),
            BitsPerSample = 16,
            AverageBytesPerSecond = checked((uint)(audio.SampleRate * (long)audio.Channels * 2))
        };
        using var signal = new EventWaitHandle(false, EventResetMode.ManualReset);
        var bytes = audio.Data.ToArray();
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        var header = IntPtr.Zero;
        var device = IntPtr.Zero;
        var prepared = false;
        var closed = true;
        try
        {
            header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
            Check(waveOutOpen(out device, uint.MaxValue, ref format,
                signal.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, 0x00050000));
            closed = false;
            Marshal.StructureToPtr(new WaveHeader
            {
                Data = pinned.AddrOfPinnedObject(), BufferLength = checked((uint)bytes.Length)
            }, header, false);
            var size = (uint)Marshal.SizeOf<WaveHeader>();
            Check(waveOutPrepareHeader(device, header, size));
            prepared = true;
            signal.Reset();
            token.ThrowIfCancellationRequested();
            Check(waveOutWrite(device, header, size));
            var waits = new WaitHandle[] { signal, token.WaitHandle };
            while ((Marshal.PtrToStructure<WaveHeader>(header).Flags & 1) == 0)
            {
                token.ThrowIfCancellationRequested();
                signal.Reset();
                // Recheck after resetting so a completion event cannot be lost.
                if ((Marshal.PtrToStructure<WaveHeader>(header).Flags & 1) != 0) break;
                WaitHandle.WaitAny(waits);
            }
            token.ThrowIfCancellationRequested();
        }
        finally
        {
            if (device != IntPtr.Zero)
            {
                waveOutReset(device);
                if (prepared) waveOutUnprepareHeader(device, header, (uint)Marshal.SizeOf<WaveHeader>());
                closed = waveOutClose(device) == 0;
            }
            // Never free native-owned memory if a broken driver refuses to close its device.
            if (closed)
            {
                Array.Clear(bytes);
                if (header != IntPtr.Zero) Marshal.FreeHGlobal(header);
                pinned.Free();
            }
            if (!closed) throw new InvalidOperationException("Audio device cleanup failed.");
        }
    }

    private static void Check(uint result)
    {
        if (result != 0) throw new InvalidOperationException("Local audio playback failed.");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort FormatTag, Channels;
        public uint SamplesPerSecond, AverageBytesPerSecond;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength, BytesRecorded;
        public UIntPtr User;
        public uint Flags, Loops;
        public IntPtr Next;
        public UIntPtr Reserved;
    }

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutOpen(out IntPtr device, uint deviceId, ref WaveFormat format,
        IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutReset(IntPtr device);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutClose(IntPtr device);
}
