using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Saylo.Desktop.ViewModels;
using Saylo.Desktop.Views;
using Saylo.Desktop.Services;
using Saylo.Desktop.Services.Audio;

namespace Saylo.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            window.DataContext = new MainViewModel(AudioInputAdapterFactory.CreateDefault(),
                playback: AudioPlaybackAdapterFactory.CreateDefault(),
                clipboard: new AvaloniaClipboardService(() => window.Clipboard));
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
