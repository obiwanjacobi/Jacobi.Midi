using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Jacobi.Midi.Console.Looks;
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
    /// Null when the platform has none; the console then works offline only.
    /// </summary>
    public IMidiApi? MidiApi { get; init; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ConsoleLooks.Apply(this, ConsoleLooks.Default);
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
                DataContext = CreateMainViewModel()
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = CreateMainViewModel() };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = CreateMainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // the console starts empty: the user opens a rig and dashboards
    private MainViewModel CreateMainViewModel() => new(MidiApi);
}
