using Jacobi.Midi.Console.Core.Files;
using Jacobi.Midi.Console.Core.Schemas;

namespace Jacobi.Midi.Console.Core.Dashboards;

/// <summary>
/// Reads and writes dashboards as XML or JSON. The control tree is written as nested elements named after the
/// control kind (<c>row</c>, <c>section</c>, <c>knob</c>, ...); bindings as <c>[device:]path</c>. Content that does
/// not fit (unknown attributes or elements, invalid values) is reported with its location, never ignored.
/// </summary>
public static class DashboardFile
{
    /// <summary>The kind and version of the file, in its <c>format</c> attribute.</summary>
    public const string Format = "jacobi.midi.dashboard/1";

    private const string RootName = "dashboard";
    private const string DeviceName = "device";
    private const string SegmentName = "segment";

    public static Dashboard Read(string text, FileFormat format)
        => FromElement(ConsoleFile.Read(text, format, RootName));

    public static string Write(Dashboard dashboard, FileFormat format)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        return ConsoleFile.Write(ToElement(dashboard), format);
    }

    // writing

    private static FileElement ToElement(Dashboard dashboard)
    {
        var root = new FileElement(RootName)
            .Set(ConsoleFile.FormatAttribute, Format)
            .Set("name", dashboard.Name)
            .Set("cellSize", dashboard.CellSize)
            .Set("look", dashboard.Look);
        foreach (var device in dashboard.Devices)
        {
            root.Add(ConsoleFile.SetColors(new FileElement(DeviceName, FileRoles.Devices)
                .Set("alias", device.Alias)
                .Set("mapRoot", device.MapRoot), device.Colors));
        }
        ConsoleFile.AddSources(root, dashboard.Sources);
        root.Add(ToElement(dashboard.Layout, FileRoles.Layout));
        return root;
    }

    private static FileElement ToElement(DashboardControl control, FileRole role)
    {
        var name = control switch
        {
            LayoutControl layout => FileNames.Of(layout.Kind),
            ParameterControl parameter => FileNames.Of(parameter.Kind),
        };
        var element = new FileElement(name, role).Set("label", control.Label);
        if (control.Size is { } size)
            element.Set("size", size.Columns, size.Rows);
        if (control.At is { } at)
            element.Set("at", at.Column, at.Row);
        element.Set("grow", control.Grow);
        ConsoleFile.SetColors(element, control.Colors)
            .Set("device", control.Device)
            .Set("base", control.Base);

        switch (control)
        {
            case LayoutControl layout:
                if (layout.Gap != 0)
                    element.Set("gap", layout.Gap);
                element.SetFlag("wrap", layout.Wrap);
                foreach (var variable in layout.Variables)
                {
                    element.Add(new FileElement(FileRoles.Variables.ElementName!, FileRoles.Variables)
                        .Set("name", variable.Name)
                        .Set("value", variable.Value));
                }
                foreach (var child in layout.Children)
                    element.Add(ToElement(child, FileRoles.Children));
                break;

            case ParameterControl parameter:
                element
                    .Set("unit", parameter.Display.Unit)
                    .Set("decimals", parameter.Display.Decimals);
                AddSlots(element, parameter);
                break;
        }
        return element;
    }

    private static void AddSlots(FileElement element, ParameterControl control)
    {
        switch (control)
        {
            case ValueControl value:
                foreach (var (slot, binding) in value.Bindings)
                {
                    element.Add(new FileElement(FileRoles.Bind.ElementName!, FileRoles.Bind)
                        .Set(FileRoles.Bind.KeyAttribute!, slot)
                        .Set(FileRoles.Bind.ValueAttribute!, binding.ToString()));
                }
                break;

            case EnvelopeControl envelope:
                foreach (var segment in envelope.Segments)
                {
                    element.Add(new FileElement(SegmentName, FileRoles.Segments)
                        .Set("time", segment.Time?.ToString())
                        .Set("level", segment.Level?.ToString())
                        .SetFlag("hold", segment.Hold));
                }
                break;
        }
    }

    // reading

    private static Dashboard FromElement(FileElement root)
    {
        ConsoleFile.CheckFormat(root, Format);

        var devices = new List<DashboardDevice>();
        var assemblies = new List<string>();
        var schemas = new List<string>();
        LayoutControl? layout = null;
        foreach (var child in root.TakeChildren())
        {
            if (ConsoleFile.TryReadSource(child, assemblies, schemas))
            {
                continue;
            }
            if (child.Name == DeviceName)
            {
                var device = ReadDevice(child);
                if (devices.Any(other => String.Equals(other.Alias, device.Alias, StringComparison.OrdinalIgnoreCase)))
                    throw child.Error($"the device alias '{device.Alias}' is used more than once.");
                devices.Add(device);
            }
            else if (layout is not null)
            {
                throw child.Error("a dashboard has one layout control at its root.");
            }
            else
            {
                layout = ReadControl(child) as LayoutControl
                    ?? throw child.Error($"the root of a dashboard must be a layout control ({String.Join(", ", Enum.GetValues<LayoutControlKind>().Select(FileNames.Of))}).");
            }
        }

        var dashboard = new Dashboard
        {
            Name = root.GetRequired("name"),
            CellSize = ConsoleFile.GetInt(root, "cellSize", minimum: 1) ?? Dashboard.DefaultCellSize,
            Look = root.Get("look"),
            Devices = devices,
            Sources = new SchemaSources { Assemblies = assemblies, Schemas = schemas },
            Layout = layout ?? throw root.Error("the dashboard has no layout control."),
        };
        root.EnsureAllRead();
        return dashboard;
    }

    private static DashboardDevice ReadDevice(FileElement element)
    {
        var device = new DashboardDevice(element.GetRequired("alias"), element.GetRequired("mapRoot")) { Colors = ConsoleFile.GetColors(element) };
        element.EnsureAllRead();
        return device;
    }

    private static DashboardControl ReadControl(FileElement element)
    {
        // the attributes of every control
        var label = element.Get("label");
        var size = ReadSize(element);
        var at = ReadPosition(element);
        var grow = ConsoleFile.GetInt(element, "grow", minimum: 0);
        var colors = ConsoleFile.GetColors(element);
        var device = element.Get("device");
        var basePath = ReadBase(element);

        DashboardControl control;
        if (FileNames.TryParse<LayoutControlKind>(element.Name, out var layoutKind))
        {
            var children = element.TakeChildren();
            control = new LayoutControl
            {
                Kind = layoutKind,
                Label = label, Size = size, At = at, Grow = grow, Colors = colors, Device = device, Base = basePath,
                Gap = ConsoleFile.GetInt(element, "gap", minimum: 0) ?? 0,
                Wrap = element.GetFlag("wrap"),
                Variables = ReadVariables(element, children.Where(IsVariable)),
                Children = [.. children.Where(child => !IsVariable(child)).Select(ReadControl)],
            };
        }
        else if (FileNames.TryParse<ParameterControlKind>(element.Name, out var kind))
        {
            var display = new DisplaySettings
            {
                Unit = element.Get("unit"),
                Decimals = ConsoleFile.GetInt(element, "decimals", minimum: 0),
            };
            control = kind == ParameterControlKind.Envelope
                ? new EnvelopeControl
                {
                    Label = label, Size = size, At = at, Grow = grow, Colors = colors, Device = device, Base = basePath,
                    Display = display,
                    Segments = [.. element.TakeChildren().Select(ReadSegment)],
                }
                : new ValueControl(kind)
                {
                    Label = label, Size = size, At = at, Grow = grow, Colors = colors, Device = device, Base = basePath,
                    Display = display,
                    Bindings = ReadBindings(element, kind),
                };
        }
        else
        {
            throw element.Error($"'{element.Name}' is not a control.");
        }

        element.EnsureAllRead();
        return control;
    }

    private static bool IsVariable(FileElement element) => element.Name == FileRoles.Variables.ElementName;

    private static List<IndexVariable> ReadVariables(FileElement layout, IEnumerable<FileElement> elements)
    {
        var variables = new List<IndexVariable>();
        foreach (var element in elements)
        {
            var name = element.GetRequired("name");
            if (!SchemaPaths.IsVariableName(name))
                throw element.Error($"'name': '{name}' is not a variable name (a letter or '_', then letters, digits or '_').");
            if (variables.Any(variable => variable.Name == name))
                throw element.Error($"'{layout.Name}' declares the variable '{name}' more than once.");
            variables.Add(new IndexVariable(name, ConsoleFile.GetInt(element, "value", minimum: 0) ?? 0));
            element.EnsureAllRead();
        }
        return variables;
    }

    private static CellSize? ReadSize(FileElement element)
    {
        if (element.GetPair("size") is not { } size)
            return null;
        return size.First < 1 || size.Second < 1
            ? throw element.Error($"'size': {size.First},{size.Second} is smaller than one cell.")
            : new CellSize(size.First, size.Second);
    }

    private static CellPosition? ReadPosition(FileElement element)
    {
        if (element.GetPair("at") is not { } at)
            return null;
        return at.First < 0 || at.Second < 0
            ? throw element.Error($"'at': {at.First},{at.Second} is outside the grid.")
            : new CellPosition(at.First, at.Second);
    }

    private static string? ReadBase(FileElement element)
    {
        var text = element.Get("base");
        // the variables are checked when the bindings are resolved; any value will do to check the path
        return text is null || SchemaPaths.TryParse(text, _ => 0, out _, out var error)
            ? text
            : throw element.Error($"'base': {error}");
    }

    private static Dictionary<string, Binding> ReadBindings(FileElement element, ParameterControlKind kind)
    {
        var allowed = SlotNames.Of(kind);
        var bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
        foreach (var child in element.TakeChildren())
        {
            if (child.Name != FileRoles.Bind.ElementName)
                throw child.Error($"'{element.Name}' cannot contain '{child.Name}'.");

            var slot = child.GetRequired(FileRoles.Bind.KeyAttribute!);
            if (!allowed.Contains(slot))
                throw child.Error($"'{element.Name}' has no slot '{slot}' (slots: {String.Join(", ", allowed)}).");
            if (bindings.ContainsKey(slot))
                throw child.Error($"the slot '{slot}' is bound more than once.");
            bindings[slot] = ReadBinding(child, FileRoles.Bind.ValueAttribute!)
                ?? throw child.Error($"'{FileRoles.Bind.ValueAttribute}' is required.");
            child.EnsureAllRead();
        }
        return bindings;
    }

    private static EnvelopeSegment ReadSegment(FileElement element)
    {
        if (element.Name != SegmentName)
            throw element.Error($"an envelope contains '{SegmentName}' elements, not '{element.Name}'.");

        var segment = new EnvelopeSegment(ReadBinding(element, "time"), ReadBinding(element, "level"), element.GetFlag("hold"));
        element.EnsureAllRead();
        return segment;
    }

    private static Binding? ReadBinding(FileElement element, string attribute)
    {
        if (element.Get(attribute) is not { } text)
            return null;
        return Binding.TryParse(text, out var binding, out var error)
            ? binding
            : throw element.Error($"'{attribute}': {error}");
    }
}
