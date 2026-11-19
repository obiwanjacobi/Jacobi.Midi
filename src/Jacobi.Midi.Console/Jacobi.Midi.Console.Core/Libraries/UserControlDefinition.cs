using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Editing;
using Jacobi.Midi.Console.Core.Files;

namespace Jacobi.Midi.Console.Core.Libraries;

/// <summary>
/// A user control: a section of controls that is defined once (in a <see cref="ControlLibrary"/>) and placed on
/// dashboards as a copy. Its contract is a nodeDef: the schema node at its (full) base when placed has that nodeDef; the
/// placement gives the device and the base around the section (the section may keep one node of its own as its base,
/// e.g. <c>Compressor</c>). Self-contained: its bases and bindings are relative, it has no device of its
/// own, and its bindings use only the index variables declared inside it (<see cref="Check"/>).
/// </summary>
public sealed class UserControlDefinition
{
    /// <summary>The name of the user control: unique (case-insensitive) in its library.</summary>
    public required string Name { get; init; }

    /// <summary>The full name of the nodeDef of the contract: <c>document.nodeDef</c>, e.g. <c>Gx700.patch</c>.</summary>
    public required SchemaName NodeDef { get; init; }

    /// <summary>The section that holds the controls; placed as a copy.</summary>
    public required LayoutControl Section { get; init; }

    /// <summary>
    /// What does not fit a user control in the section: it must be a section without device, and its base (when it has
    /// one) is one node, relative to the placement; no control or binding has a device of its own (so its bases and
    /// bindings are relative), and the bindings and bases use only the index variables declared inside it. Empty when it
    /// fits.
    /// </summary>
    public static IReadOnlyList<string> Check(LayoutControl section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var problems = new List<string>();
        if (section.Kind != LayoutControlKind.Section)
            problems.Add($"a user control is a section, not a {FileNames.Of(section.Kind)}.");
        if (section.Base is { } sectionBase && sectionBase.Contains(SchemaPath.Separator))
            problems.Add($"the base of the section of a user control is one node, not '{sectionBase}': the placement gives the base around it.");
        Visit(section, new HashSet<string>(StringComparer.Ordinal));
        return problems;

        void Visit(DashboardControl control, HashSet<string> outer)
        {
            var name = ControlCatalog.NameOf(control);
            if (control.Device is { } device)
                problems.Add($"'{name}' has the device '{device}': the placement gives the device.");

            var declared = outer;
            if (control is LayoutControl { Variables.Count: > 0 } layout)
                declared = [.. outer, .. layout.Variables.Select(variable => variable.Name)];

            // a base without a device of its own is relative to the base around it (for the section: the placement)
            if (control.Base is { } basePath)
                CheckVariables(name, basePath, declared);

            if (control is ParameterControl parameter)
            {
                foreach (var (slot, binding) in parameter.Slots)
                {
                    if (binding.Device is not null)
                        problems.Add($"'{name}' ({slot}) is bound to '{binding}': the placement gives the device.");
                    else if (binding.Variable is { } variable)
                        CheckVariable(name, variable, declared);
                    else
                        CheckVariables(name, binding.Path, declared);
                }
            }

            foreach (var child in control.Children)
                Visit(child, declared);
        }

        void CheckVariables(string name, string path, HashSet<string> declared)
        {
            if (SchemaPaths.TryParse(path, _ => 0, out var parsed, out _))
            {
                foreach (var use in parsed.Variables)
                    CheckVariable(name, use.Variable, declared);
            }
        }

        void CheckVariable(string name, string variable, HashSet<string> declared)
        {
            var problem = $"'{name}' uses '{SchemaPaths.VariablePrefix}{variable}', which is not declared in the user control.";
            if (!declared.Contains(variable) && !problems.Contains(problem))
                problems.Add(problem);
        }
    }
}
