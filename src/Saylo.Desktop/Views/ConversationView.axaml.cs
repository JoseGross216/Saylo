using Avalonia.Controls;
using Avalonia;
using Avalonia.Threading;
using Saylo.Desktop.ViewModels;
using System.Collections.Specialized;

namespace Saylo.Desktop.Views;

public partial class ConversationView : UserControl
{
    private INotifyCollectionChanged? _messages;
    private readonly ScrollViewer _scroll;

    public ConversationView()
    {
        InitializeComponent();
        _scroll = this.FindControl<ScrollViewer>("MessageScroll")!;
        DataContextChanged += (_, _) => WatchMessages();
        AttachedToVisualTree += (_, _) => WatchMessages();
        DetachedFromVisualTree += (_, _) => UnwatchMessages();
    }

    private void WatchMessages()
    {
        UnwatchMessages();
        if (DataContext is not MainViewModel model) return;
        _messages = model.Messages;
        _messages.CollectionChanged += OnMessagesChanged;
    }

    private void UnwatchMessages()
    {
        if (_messages is not null) _messages.CollectionChanged -= OnMessagesChanged;
        _messages = null;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Add)
        {
            var startOfNewMessage = _scroll.Extent.Height;
            Dispatcher.Post(() =>
            {
                if (_messages is not null)
                    _scroll.Offset = new Vector(0, System.Math.Max(0, System.Math.Min(startOfNewMessage,
                        _scroll.Extent.Height - _scroll.Viewport.Height)));
            }, DispatcherPriority.Background);
        }
    }
}
