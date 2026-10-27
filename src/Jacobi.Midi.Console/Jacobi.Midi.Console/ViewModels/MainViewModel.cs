namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The view model of the main view. For now it only holds the title; the rig, the open dashboards and the
/// Edit/Play mode come with the dashboard canvas.
/// </summary>
internal sealed class MainViewModel : ViewModelBase
{
    public string Title => "MIDI Console";
}
