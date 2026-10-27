using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Jacobi.Midi.Console;

/// <summary>
/// The browser (WebAssembly) host of the MIDI Console. There is no MIDI backend in the browser (yet): the console
/// works offline.
/// </summary>
internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
