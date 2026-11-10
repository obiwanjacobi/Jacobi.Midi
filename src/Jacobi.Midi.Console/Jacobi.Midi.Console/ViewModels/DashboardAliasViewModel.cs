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

    /// <summary>
    /// The exchange the dashboard receives the device's values with in Play mode with MIDI (e.g. <c>requestData</c>, see
    /// <see cref="DashboardDevice.Receive"/>); empty: none, the values are not received.
    /// </summary>
    public string? Receive
    {
        get => dashboard.FindDevice(alias)?.Receive;
        set
        {
            try
            {
                owner.SetDashboardReceive(dashboard, alias, value);
            }
            catch (ArgumentException exception)
            {
                throw new DataValidationException(exception.Message);
            }
        }
    }

    /// <summary>
    /// The exchange the dashboard sends the changes of the device's values with in Play mode with MIDI (e.g.
    /// <c>sendData</c>, see <see cref="DashboardDevice.Send"/>); empty: none, changes stay in the console.
    /// </summary>
    public string? Send
    {
        get => dashboard.FindDevice(alias)?.Send;
        set
        {
            try
            {
                owner.SetDashboardSend(dashboard, alias, value);
            }
            catch (ArgumentException exception)
            {
                throw new DataValidationException(exception.Message);
            }
        }
    }

    /// <summary>
    /// The exchanges to choose from for <see cref="Receive"/> and <see cref="Send"/>: those of the exchange file of the
    /// rig device's map root. Another name can still be typed.
    /// </summary>
    public IReadOnlyList<string> ExchangeNames { get; } = owner.ExchangeNamesOf(alias);

    /// <summary>True when the rig has a device with the alias; otherwise the controls that use it do not work.</summary>
    public bool IsInRig => owner.IsDeviceOfRig(alias);
}
