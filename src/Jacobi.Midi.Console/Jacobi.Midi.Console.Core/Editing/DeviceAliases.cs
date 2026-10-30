using Jacobi.Midi.Console.Core.Dashboards;

namespace Jacobi.Midi.Console.Core.Editing;

/// <summary>
/// The device aliases of a dashboard: where it uses them (its <see cref="Dashboard.Devices"/>, the
/// <see cref="DashboardControl.Device"/> of its controls and the <see cref="Binding.Device"/> of their bindings), and
/// renaming one throughout it, e.g. after the device got another alias in the rig. Aliases compare case-insensitively.
/// Nothing is changed yet: a rename is planned as edits.
/// </summary>
public static class DeviceAliases
{
    /// <summary>True when the text can be an alias: not empty and without the <see cref="Binding.DeviceSeparator"/>.</summary>
    public static bool IsValid(string? alias)
        => !String.IsNullOrWhiteSpace(alias) && !alias.Contains(Binding.DeviceSeparator, StringComparison.Ordinal);

    /// <summary>The aliases the dashboard uses, each once (as first written): its devices first, then the controls in order.</summary>
    public static IReadOnlyList<string> Of(Dashboard dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        var aliases = new List<string>();
        foreach (var alias in dashboard.Devices.Select(device => device.Alias).Concat(Controls(dashboard.Layout).SelectMany(AliasesOf)))
        {
            if (!aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                aliases.Add(alias);
        }
        return aliases;
    }

    /// <summary>
    /// The edits that rename the alias wherever the dashboard uses it; none when it does not use it or the new alias is
    /// the same.
    /// </summary>
    /// <exception cref="ArgumentException">The new alias cannot be taken (see <see cref="ProblemOf"/>).</exception>
    public static IReadOnlyList<IEdit> Rename(Dashboard dashboard, string alias, string newAlias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        if (ProblemOf(dashboard, alias, newAlias) is { } problem)
            throw new ArgumentException(problem, nameof(newAlias));

        var edits = new List<IEdit>();
        if (dashboard.Devices.Any(device => Is(device.Alias, alias)))
        {
            IReadOnlyList<DashboardDevice> devices = [.. dashboard.Devices.Select(device => Is(device.Alias, alias) ? device with { Alias = newAlias } : device)];
            edits.Add(new ValueEdit<IReadOnlyList<DashboardDevice>>("devices", () => dashboard.Devices, value => dashboard.Devices = value, devices));
        }
        foreach (var control in Controls(dashboard.Layout))
        {
            if (Is(control.Device, alias))
                edits.Add(new ValueEdit<string?>("device", () => control.Device, value => control.Device = value, newAlias));
            if (control is ParameterControl parameter && parameter.Slots.Values.Any(binding => Is(binding.Device, alias)))
            {
                IReadOnlyDictionary<string, Binding> slots = parameter.Slots.ToDictionary(
                    slot => slot.Key, slot => Is(slot.Value.Device, alias) ? slot.Value with { Device = newAlias } : slot.Value);
                edits.Add(new ValueEdit<IReadOnlyDictionary<string, Binding>>("bindings", () => parameter.Slots, value => parameter.Slots = value, slots));
            }
        }
        // only the case changes: still an edit; the same alias: none
        return alias == newAlias ? [] : edits;
    }

    /// <summary>
    /// Why the alias cannot be renamed to the new one in the dashboard: the new one is not valid, or the dashboard uses
    /// it already for another device (merging two aliases is not a rename); null when it can.
    /// </summary>
    public static string? ProblemOf(Dashboard dashboard, string alias, string? newAlias)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        if (!IsValid(newAlias))
            return $"An alias is not empty and has no '{Binding.DeviceSeparator}'.";
        if (!Is(alias, newAlias) && Of(dashboard).Any(used => Is(used, newAlias)))
            return $"The dashboard uses '{newAlias}' already.";
        return null;
    }

    private static bool Is(string? alias, string? other) => String.Equals(alias, other, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> AliasesOf(DashboardControl control)
    {
        if (control.Device is { } device)
            yield return device;
        if (control is ParameterControl parameter)
        {
            foreach (var binding in parameter.Slots.Values)
            {
                if (binding.Device is { } alias)
                    yield return alias;
            }
        }
    }

    // the control and all controls inside it, depth first in order
    private static IEnumerable<DashboardControl> Controls(DashboardControl control)
    {
        yield return control;
        if (control is LayoutControl layout)
        {
            foreach (var inner in layout.Children.SelectMany(Controls))
                yield return inner;
        }
    }
}
