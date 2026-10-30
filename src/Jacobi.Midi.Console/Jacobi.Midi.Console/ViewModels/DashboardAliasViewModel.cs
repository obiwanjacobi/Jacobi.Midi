using Avalonia.Data;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Editing;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// A device alias the selected dashboard uses, for the dashboard properties of Edit mode: whether the rig has a device
/// with it, and renaming it throughout the dashboard (see <see cref="MainViewModel.RenameDashboardAlias"/>), e.g. after
/// the device got another alias in the rig. A new alias that cannot be taken is a validation error of the field.
/// </summary>
internal sealed class DashboardAliasViewModel(MainViewModel owner, Dashboard dashboard, string alias) : ViewModelBase
{
    public string Alias
    {
        get => alias;
        set
        {
            var newAlias = value?.Trim() ?? String.Empty;
            if (DeviceAliases.ProblemOf(dashboard, alias, newAlias) is { } problem)
                throw new DataValidationException(problem);
            owner.RenameDashboardAlias(dashboard, alias, newAlias);
        }
    }

    /// <summary>True when the rig has a device with the alias; otherwise the controls that use it do not work.</summary>
    public bool IsInRig => owner.IsDeviceOfRig(alias);
}
