using Avalonia.Controls;
using Avalonia.Threading;
using Saylo.Desktop.ViewModels;

namespace Saylo.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _closing;
    private bool _shutdownRequested;

    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is MainViewModel model) await model.InitializeAsync();
        };
        Closing += async (_, args) =>
        {
            if (_closing || DataContext is not MainViewModel model) return;
            args.Cancel = true;
            if (_shutdownRequested) return;
            _shutdownRequested = true;
            IsEnabled = false;
            await model.DisposeAsync();
            _closing = true;
            // Leave the original Closing event before requesting the final close,
            // including when every cleanup task completed synchronously.
            Dispatcher.UIThread.Post(Close);
        };
    }
}
