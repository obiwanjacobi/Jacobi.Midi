using System;
using Avalonia;
using Jacobi.Midi.Core;

namespace Jacobi.Midi.SchemaEditor.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure(() => new App { MidiApi = CreateMidiApi() })
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    // Selects the MIDI backend for the platform this build targets.
    // Returns null when there is no backend for the platform; the MIDI bar is disabled then.
    private static IMidiApi? CreateMidiApi()
    {
#if WINDOWS
        return new Jacobi.Midi.Windows.WindowsMidiApi();
#else
        // if (OperatingSystem.IsLinux())  return new Jacobi.Midi.Linux.AlsaMidiApi();      // later
        // if (OperatingSystem.IsMacOS())  return new Jacobi.Midi.MacOS.CoreMidiApi();      // later
        return null;
#endif
        throw new PlatformNotSupportedException("MacOS and Linux are planned.");
    }
}
