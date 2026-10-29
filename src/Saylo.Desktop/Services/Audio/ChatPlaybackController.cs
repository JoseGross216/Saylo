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
    private Task? _disposal;
    public bool IsAvailable => adapter.IsAvailable;
    public bool IsPlaying => _current is not null;
    public bool IsStopping { get; private set; }
    public bool HasError { get; private set; }
    public bool RequiresStopRecovery { get; private set; }
    public bool IsAutoplayEnabled { get; set; } = true;
    public event EventHandler? StateChanged;

    public void Autoplay(ConversationMessageViewModel message, CancellationToken token)
    {
        if (IsAutoplayEnabled && message.CanPlay && message.Entry.TryClaimAutoplay()) _ = PlayAsync(message, token);
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
                if (!await StopCoreAsync())
                {
                    message.SetPlaybackState(ChatPlaybackState.Failed);
                    return;
                }
                if (_disposed || token.IsCancellationRequested || generation != Interlocked.Read(ref _generation) || !message.CanPlay) return;
                var audio = message.Audio!;
                var source = CancellationTokenSource.CreateLinkedTokenSource(token);
                lock (_sync) _active = source;
                HasError = false;
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
            HasError = true;
            if (!message.Entry.IsErased) message.SetPlaybackState(ChatPlaybackState.Failed);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RunAsync(ConversationMessageViewModel message,
        Saylo.Modules.Translation.Contracts.AudioData audio, CancellationTokenSource source)
    {
        try
        {
            await adapter.PlayAsync(audio, source.Token);
            if (!message.Entry.IsErased) message.SetPlaybackState(source.IsCancellationRequested
                ? ChatPlaybackState.Stopped : ChatPlaybackState.Ready);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            if (!message.Entry.IsErased) message.SetPlaybackState(ChatPlaybackState.Stopped);
        }
        catch
        {
            HasError = true;
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

    private async Task<bool> StopCoreAsync()
    {
        // Cancellation can finish RunAsync and clear _current before the adapter reports a stop error.
        var current = _current;
        IsStopping = current is not null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        var stopped = true;
        try
        {
            Task cancel;
            lock (_sync) cancel = _active?.CancelAsync() ?? Task.CompletedTask;
            try { await cancel; }
            catch { stopped = false; }
            try { await adapter.StopAsync(CancellationToken.None); }
            catch { stopped = false; }
            await _playing;
            _playing = Task.CompletedTask;
            if (!stopped)
            {
                HasError = true;
                RequiresStopRecovery = true;
                current?.SetPlaybackState(ChatPlaybackState.StopFailed);
            }
            else
            {
                RequiresStopRecovery = false;
                if (current is null) HasError = false;
            }
            return stopped;
        }
        finally
        {
            IsStopping = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task StopAsync()
    {
        Interlocked.Increment(ref _generation);
        await _switch.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _switch.Release(); }
    }

    public ValueTask DisposeAsync() => new(_disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        await StopAsync();
        try { await adapter.DisposeAsync(); } catch { }
    }
}
