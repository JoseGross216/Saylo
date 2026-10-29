using System;
using System.Threading.Tasks;
using Avalonia.Input.Platform;

namespace Saylo.Desktop.Services;

public interface IClipboardService
{
    bool IsAvailable { get; }
    Task SetTextAsync(string text);
}

public sealed class AvaloniaClipboardService(Func<IClipboard?> getClipboard) : IClipboardService
{
    public bool IsAvailable => getClipboard() is not null;
    public Task SetTextAsync(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var clipboard = getClipboard() ?? throw new InvalidOperationException("Clipboard unavailable.");
        return clipboard.SetTextAsync(text);
    }
}

public sealed class UnavailableClipboardService : IClipboardService
{
    public bool IsAvailable => false;
    public Task SetTextAsync(string text) => Task.FromException(new InvalidOperationException("Clipboard unavailable."));
}
