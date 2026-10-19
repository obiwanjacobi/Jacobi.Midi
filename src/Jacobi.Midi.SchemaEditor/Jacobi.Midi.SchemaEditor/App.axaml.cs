using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Jacobi.Midi.Core;
using Jacobi.Midi.SchemaEditor.Midi;
using Jacobi.Midi.SchemaEditor.ViewModels;
using Jacobi.Midi.SchemaEditor.Views;

namespace Jacobi.Midi.SchemaEditor;

public partial class App : Application
{
    /// <summary>
    /// The platform MIDI backend, supplied by the host, e.g.
    /// <c>AppBuilder.Configure(() => new App { MidiApi = new WindowsMidiApi() })</c>.
    /// When null, the MIDI bar is disabled.
    /// </summary>
    public IMidiApi? MidiApi { get; init; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
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

    private MainViewModel CreateMainViewModel()
        => new() { MidiBar = new MidiBarViewModel(MidiApi) };
}
