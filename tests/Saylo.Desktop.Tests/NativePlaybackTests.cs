using Saylo.Desktop.Services.Audio;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Tests;

/// <summary>Opt-in hardware checks use zero-valued PCM, never a tone or speech.</summary>
public sealed class NativePlaybackTests
{
    [WindowsPlaybackFact]
    public async Task WindowsBackendPlaysSilentPcmAndReleasesItsDevice()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var adapter = new WindowsAudioPlaybackAdapter();
        var silence = new AudioData(new byte[320], AudioFormat.Pcm16, 16000, 1);

        await adapter.PlayAsync(silence, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await adapter.StopAsync(CancellationToken.None);
        await adapter.PlayAsync(silence, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [WindowsPlaybackFact]
    public async Task WindowsBackendStopCancelsPlaybackAndAllowsReuse()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var adapter = new WindowsAudioPlaybackAdapter();
        var run = adapter.PlayAsync(new AudioData(new byte[160000], AudioFormat.Pcm16, 16000, 1), CancellationToken.None);
        await Task.Delay(100);

        await adapter.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        await adapter.PlayAsync(new AudioData(new byte[320], AudioFormat.Pcm16, 16000, 1), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
    }
}

public sealed class WindowsPlaybackFactAttribute : FactAttribute
{
    public WindowsPlaybackFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("SAYLO_NATIVE_AUDIO_TESTS") != "1")
            Skip = "Opt-in Windows output-device test: set SAYLO_NATIVE_AUDIO_TESTS=1. Uses silent PCM only.";
    }
}
