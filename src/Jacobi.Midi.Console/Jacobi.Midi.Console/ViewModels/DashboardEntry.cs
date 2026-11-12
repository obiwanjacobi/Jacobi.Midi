using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Parameters;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// A dashboard open in the console: the dashboard, where it was read from (and whether it changed since) and its
/// session on the open rig. The dashboard and its origin stay while it is open; the session is null without a rig and
/// is a new one each time the rig is opened again.
/// </summary>
internal sealed class DashboardEntry(Dashboard dashboard, FileOrigin origin) : ObservableObject
{
    public Dashboard Dashboard { get; } = dashboard;

    public FileOrigin Origin { get; } = origin;

    public DashboardSession? Session { get; set; }

    /// <summary>The name of the dashboard, observable (for the list of open dashboards); see <see cref="Refresh"/>.</summary>
    public string Name => Dashboard.Name;

    /// <summary>
    /// The dashboard was edited (or an edit undone): what is shown of it here is told again.
    /// </summary>
    public void Refresh() => OnPropertyChanged(nameof(Name));
}
