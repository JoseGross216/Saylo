using System;
using System.Threading;
using System.Threading.Tasks;
using Saylo.Desktop.ViewModels;

namespace Saylo.Desktop.Services.Audio;

/// <summary>Serializes playback, preempts previous voices and invalidates queued requests on Stop.</summary>
public sealed class ChatPlaybackController(IAudioPlaybackAdapter adapter) : IAsyncDisposable
{
    private readonly SemaphoreSlim _switch = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _active;
    private Task _playing = Task.CompletedTask;
    private ConversationMessageViewModel? _current;
    private long _generation;
    private bool _disposed;
    public bool IsAvailable => adapter.IsAvailable;
    public bool IsPlaying => _current is not null;
    public event EventHandler? StateChanged;

    public void Autoplay(ConversationMessageViewModel message, CancellationToken token)
    {
        if (message.Entry.TryClaimAutoplay()) _ = PlayAsync(message, token);
    }

    public async Task PlayAsync(ConversationMessageViewModel message, CancellationToken token)
    {
        var generation = Interlocked.Read(ref _generation);
        try
        {
            await _switch.WaitAsync(token);
            Task playing;
            try
            {
                if (_disposed || generation != Interlocked.Read(ref _generation) || !message.CanPlay) return;
                await StopCoreAsync();
                if (_disposed || token.IsCancellationRequested || generation != Interlocked.Read(ref _generation) || !message.CanPlay) return;
                var audio = message.Audio!;
                var source = CancellationTokenSource.CreateLinkedTokenSource(token);
                lock (_sync) _active = source;
                _current = message;
                message.SetPlaybackState(ChatPlaybackState.Playing);
                StateChanged?.Invoke(this, EventArgs.Empty);
                playing = _playing = RunAsync(message, audio, source);
            }
            finally { _switch.Release(); }
            await playing;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            if (!message.Entry.IsErased) message.SetPlaybackState(ChatPlaybackState.Failed);
        }
    }

    private async Task RunAsync(ConversationMessageViewModel message,
        Saylo.Modules.Translation.Contracts.AudioData audio, CancellationTokenSource source)
    {
        try
        {
            await adapter.PlayAsync(audio, source.Token);
            if (!message.Entry.IsErased) message.SetPlaybackState(ChatPlaybackState.Ready);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            if (!message.Entry.IsErased) message.SetPlaybackState(ChatPlaybackState.Stopped);
        }
        catch
        {
            if (!message.Entry.IsErased) message.SetPlaybackState(ChatPlaybackState.Failed);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_active, source)) _active = null;
                source.Dispose();
            }
            if (ReferenceEquals(_current, message)) _current = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task StopCoreAsync()
    {
        Task cancel;
        lock (_sync) cancel = _active?.CancelAsync() ?? Task.CompletedTask;
        await cancel;
        try { await adapter.StopAsync(CancellationToken.None); }
        catch { if (_current is { } current) current.SetPlaybackState(ChatPlaybackState.Failed); }
        await _playing;
        _playing = Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Interlocked.Increment(ref _generation);
        await _switch.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _switch.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await StopAsync();
        try { await adapter.DisposeAsync(); } catch { }
    }
}
