using System.Collections.ObjectModel;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Conversations;

/// <summary>One application session, confined to its caller thread. No persistence or logging.</summary>
public sealed class ConversationSession : IDisposable
{
    private readonly List<ConversationMessage> _messages = [];
    private bool _disposed;
    public ReadOnlyCollection<ConversationMessage> Messages { get; }

    public ConversationSession() => Messages = _messages.AsReadOnly();

    public ConversationMessage Start(TextTranslationRequest request, MessageOrigin origin = MessageOrigin.Text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        var message = new ConversationMessage(request, origin);
        _messages.Add(message);
        return message;
    }

    public bool Contains(ConversationMessage message) => !_disposed && _messages.Contains(message);

    public bool Apply(ConversationMessage message, TranslationUpdate update) =>
        Contains(message) && message.Apply(update);

    public void Clear()
    {
        foreach (var message in _messages) message.Erase();
        _messages.Clear();
    }

    public void Dispose()
    {
        Clear();
        _disposed = true;
    }
}
