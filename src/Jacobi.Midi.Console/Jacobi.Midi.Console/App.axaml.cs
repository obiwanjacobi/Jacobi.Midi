using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Jacobi.Midi.Console.Faces;
using Jacobi.Midi.Console.ViewModels;
using Jacobi.Midi.Console.Views;
using Jacobi.Midi.Core;

namespace Jacobi.Midi.Console;

/// <summary>
/// The MIDI Console application, shared by the desktop and browser hosts.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The platform MIDI backend, supplied by the host, e.g.
    /// <c>AppBuilder.Configure(() => new App { MidiApi = new WindowsMidiApi() })</c>.
    /// Null when the platform has none; the console then works offline. Not used yet: synchronisation with the
    /// devices is a later milestone.
    /// </summary>
    public IMidiApi? MidiApi { get; init; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // the faces of the current look; a look switch replaces this resource
        Resources[Kits.ResourceKey] = Kits.Flat;
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = new MainViewModel() };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
