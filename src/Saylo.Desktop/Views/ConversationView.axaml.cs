using Avalonia.Controls;
using Avalonia.Threading;
using Saylo.Desktop.ViewModels;
using System.Collections.Specialized;

namespace Saylo.Desktop.Views;

public partial class ConversationView : UserControl
{
    private INotifyCollectionChanged? _messages;
    private readonly ScrollViewer _scroll;
    private bool _followLatest;

    public ConversationView()
    {
        InitializeComponent();
        _scroll = this.FindControl<ScrollViewer>("MessageScroll")!;
        _scroll.ScrollChanged += OnScrollChanged;
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
        _followLatest = false;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Add)
        {
            _followLatest = true;
            FollowLatest();
        }
        else if (args.Action == NotifyCollectionChangedAction.Reset)
            _followLatest = false;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (args.ExtentDelta.Y != 0 || args.ViewportDelta.Y != 0)
            FollowLatest();
        else if (args.OffsetDelta.Y != 0)
            _followLatest = _scroll.Offset.Y >= _scroll.Extent.Height - _scroll.Viewport.Height - 1;
    }

    private void FollowLatest()
    {
        if (!_followLatest) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_messages is not null && _followLatest) _scroll.ScrollToEnd();
        }, DispatcherPriority.Background);
    }
}
