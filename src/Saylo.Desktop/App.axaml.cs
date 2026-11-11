using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Saylo.Desktop.ViewModels;
using Saylo.Desktop.Views;
using Saylo.Desktop.Services;
using Saylo.Desktop.Services.Audio;
using Saylo.Desktop.Services.Translation;
using System;

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
            var connection = DesktopTranslationFactory.Create(desktop.Args ?? Array.Empty<string>());
            var audioInput = connection.Mode == DesktopTranslationMode.Demonstration
                ? AudioInputAdapterFactory.CreateUnconfigured()
                : AudioInputAdapterFactory.CreateDefault();
            window.DataContext = new MainViewModel(audioInput,
                playback: AudioPlaybackAdapterFactory.CreateDefault(),
                clipboard: new AvaloniaClipboardService(() => window.Clipboard),
                connection: connection);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
