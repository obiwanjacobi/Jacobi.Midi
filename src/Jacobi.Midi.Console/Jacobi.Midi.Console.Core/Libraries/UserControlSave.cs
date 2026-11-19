using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Editing;
using Jacobi.Midi.Console.Core.Schemas;

namespace Jacobi.Midi.Console.Core.Libraries;

/// <summary>
/// Makes a user control of a section of a dashboard ("Save as user control"): a copy of the section whose bases and
/// bindings are made relative to the section's (full) base, without the device and position of the section (the
/// placement gives them). A section with a base of its own keeps the last node of its (full) base, as a base relative to
/// the placement (e.g. <c>Compressor</c> of <c>AddressMap.Temporary.Compressor</c>). The section keeps its label, which
/// is also the user control's name. The contract is the
/// nodeDef of the schema node at the section's base. What does not fit a user control (a control or binding of another device, a base or
/// binding outside the section's base, an index variable declared outside it: see
/// <see cref="UserControlDefinition.Check"/>) is reported; then there is no user control. Nothing is changed.
/// </summary>
public static class UserControlSave
{
    /// <param name="dashboard">The dashboard the section is in.</param>
    /// <param name="section">The section to make a user control of.</param>
    /// <param name="library">The schemas, to find the nodeDef of the section's base.</param>
    /// <exception cref="ArgumentException">The section is not in the dashboard.</exception>
    public static UserControlSavePlan Plan(Dashboard dashboard, LayoutControl section, SchemaLibrary library)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(library);
        var problems = new List<string>();
        var sectionName = ControlCatalog.NameOf(section);

        if (section.Kind != LayoutControlKind.Section)
            problems.Add($"'{sectionName}' is not a section: a user control is a section.");
        if (section.Label is null)
            problems.Add("the section has no label: its label is the name of the user control.");
        var outer = (GridLayout.ParentOf(dashboard.Layout, section) is { } parent ? BindingScope.Of(dashboard.Layout, parent) : null)
            ?? throw new ArgumentException("The section is the root or not in the dashboard.", nameof(section));
        var scope = outer.Inside(section);
        if (scope.Device is not { } device || scope.Base is not { } basePath)
        {
            problems.Add($"'{sectionName}' has no device and base: the nodeDef of its base is the contract of the user control.");
            return new UserControlSavePlan(null, problems);
        }

        SchemaName? nodeDef = null;
        if (dashboard.FindDevice(device) is not { } dashboardDevice)
            problems.Add($"the dashboard does not list the device '{device}'.");
        else if (!SchemaPaths.TryParse(basePath, _ => 0, out var parsed, out var error))
            problems.Add(error);
        else if ((nodeDef = library.NodeDefOf(dashboardDevice.Schema, parsed.Path)) is null)
            problems.Add($"the base '{basePath}' is not a node with a nodeDef in '{dashboardDevice.Schema}': the nodeDef is the contract of the user control.");

        var copy = (LayoutControl)DashboardFile.Copy(section);
        copy.At = null;
        copy.Device = null;
        copy.Base = section.Base is null ? null : LastNodeOf(basePath);
        foreach (var (original, copied) in section.Children.Zip(copy.Children))
            Rewrite(original, copied, scope);

        if (problems.Count == 0)
            problems.AddRange(UserControlDefinition.Check(copy));
        return problems.Count > 0 || nodeDef is null
            ? new UserControlSavePlan(null, problems)
            : new UserControlSavePlan(new UserControlDefinition { Name = section.Label!, NodeDef = nodeDef.Value, Section = copy }, problems);

        // the copy of a control in the section: no device, its base and bindings relative to the base around it
        void Rewrite(DashboardControl original, DashboardControl copied, BindingScope around)
        {
            var name = ControlCatalog.NameOf(original);
            if (original.Device is { } own && !String.Equals(own, device, StringComparison.OrdinalIgnoreCase))
                problems.Add($"'{name}' has the device '{own}': a user control has the device of its section ('{device}').");
            copied.Device = null;

            var inside = around.Inside(original);
            if (original.Base is not null)
            {
                if (RelativeTo(around.Base!, inside.Base!) is { } relative)
                    copied.Base = relative;
                else
                    problems.Add($"'{name}' has the base '{inside.Base}', which is not below the base around it ('{around.Base}').");
            }

            if (original is ParameterControl parameter)
            {
                var slots = new Dictionary<string, Binding>(StringComparer.Ordinal);
                foreach (var (slot, binding) in parameter.Slots)
                {
                    slots[slot] = binding;
                    if (binding.Variable is not null)
                        continue;
                    if (inside.FullPathOf(binding) is not (string bindingDevice, string path) || !String.Equals(bindingDevice, device, StringComparison.OrdinalIgnoreCase))
                        problems.Add($"'{name}' ({slot}) is bound to '{binding}', which is not a parameter of the device of the section ('{device}').");
                    else if (RelativeTo(inside.Base!, path) is { } relative)
                        slots[slot] = new Binding(relative);
                    else
                        problems.Add($"'{name}' ({slot}) is bound to '{path}', which is not below the base around it ('{inside.Base}').");
                }
                ((ParameterControl)copied).Slots = slots;
            }

            foreach (var (child, copiedChild) in original.Children.Zip(copied.Children))
                Rewrite(child, copiedChild, inside);
        }
    }

    // the last node of a full base (with its repeat index): the base the section of a user control keeps
    private static string LastNodeOf(string basePath) => basePath[(basePath.LastIndexOf(SchemaPath.Separator) + 1)..];

    // the part of a full path below a full base; null when it is not below it
    private static string? RelativeTo(string basePath, string path)
    {
        var prefix = basePath + SchemaName.Separator;
        return path.StartsWith(prefix, StringComparison.Ordinal) && path.Length > prefix.Length ? path[prefix.Length..] : null;
    }
}

/// <summary>
/// What "Save as user control" gives: the user control, or null when the section does not fit one; the problems tell
/// why.
/// </summary>
public sealed record UserControlSavePlan(UserControlDefinition? Control, IReadOnlyList<string> Problems);
