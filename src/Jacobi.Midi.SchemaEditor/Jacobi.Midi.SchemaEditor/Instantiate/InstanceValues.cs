using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// The values of a group of an instance tree (a group, repeat item or body of an open choice), copied to be pasted into
/// another instance of the same group (see <see cref="ShapeOf"/>). They travel as text (JSON), e.g. on the system clipboard,
/// so they can be pasted in another instantiate or match dialog.
/// Copied are the field values, the selected alternative of each choice and the items of each repeat; not the inclusion of the
/// parts of an address map nor the item of the address space an address map repeat shows (they decide what is sent where).
/// </summary>
internal sealed class InstanceValues
{
    // recognizes the copied values in a text
    private const string Format = "Jacobi.Midi.InstanceValues";

    // the members of a group are found by name, as the serializer does
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly JsonObject _values;

    private InstanceValues(string shape, string source, JsonObject values)
    {
        Shape = shape;
        Source = source;
        _values = values;
    }

    /// <summary>The groups the values fit (see <see cref="ShapeOf"/>).</summary>
    public string Shape { get; }

    /// <summary>The display path of the group the values were copied from, e.g. <c>Root.Patches[3]</c>.</summary>
    public string Source { get; }

    /// <summary>
    /// What the values of <paramref name="group"/> fit, as text: groups with the same shape take each other's values.
    /// <list type="bullet">
    /// <item>A group of a node def (or a root that conforms to one): <c>nodeDef:Document.Name</c>.</item>
    /// <item>A group in an instance of a node def: that node def and the path below it, <c>nodeDef:Document.Name/Common</c>.</item>
    /// <item>Any other group: its schema node, <c>schema:Document.Root.Path</c>; the same for all items of a repeat.</item>
    /// </list>
    /// </summary>
    public static string ShapeOf(InstanceGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        for (InstanceNode? node = group; node is not null; node = node.Parent)
        {
            if (node.Schema.NodeDef is { } nodeDef && ReferenceEquals(node, group))
            {
                return $"nodeDef:{nodeDef.Qualify(node.Document)}";
            }
            // (a repeat item has the path of its repeat: it is met first)
            if (node.Schema is { NodeDef: { } instanceOf, IsNodeDefInstance: true }
                && group.Path.Value.StartsWith($"{node.Path.Value}.", StringComparison.OrdinalIgnoreCase))
            {
                return $"nodeDef:{instanceOf.Qualify(node.Document)}/{group.Path.Value[(node.Path.Value.Length + 1)..]}";
            }
            // the path of the body of an open choice starts anew
            if (node is InstanceBody)
            {
                break;
            }
        }
        return $"schema:{group.Document}.{group.Path.Value}";
    }

    /// <summary>True when the values can be pasted into <paramref name="target"/>: it has the same <see cref="Shape"/>.</summary>
    public bool Fits(InstanceGroup target) => String.Equals(Shape, ShapeOf(target), StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies the values of <paramref name="group"/>.</summary>
    public static InstanceValues Copy(InstanceGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new(ShapeOf(group), group.DisplayPath, CopyGroup(group));
    }

    private static JsonObject CopyGroup(InstanceGroup group)
    {
        var values = new JsonObject(NodeOptions);
        foreach (var member in group.Members)
        {
            values[member.Name] = CopyNode(member);
        }
        return values;
    }

    private static JsonNode? CopyNode(InstanceNode node) => node switch
    {
        InstanceField field => JsonValue.Create(field.Value),
        InstanceGroup group => CopyGroup(group),
        InstanceRepeat repeat => new JsonArray([.. repeat.Items.Select(CopyGroup)]),
        InstanceChoice choice => new JsonObject(NodeOptions)
        {
            ["selected"] = choice.SelectedOption?.Name,
            ["value"] = choice.Selected is { } selected ? CopyNode(selected) : null
        },
        _ => null
    };

    /// <summary>The values as text (JSON), recognized by <see cref="TryParse"/>.</summary>
    public string ToText()
    {
        var text = new JsonObject
        {
            ["format"] = Format,
            ["shape"] = Shape,
            ["source"] = Source,
            ["values"] = _values.DeepClone()
        };
        return text.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Reads the values from <paramref name="text"/> (see <see cref="ToText"/>).</summary>
    /// <returns>False when the text holds no copied values.</returns>
    public static bool TryParse(string? text, out InstanceValues? values)
    {
        values = null;
        if (String.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        try
        {
            if (JsonNode.Parse(text, NodeOptions) is JsonObject root
                && root["format"]?.GetValue<string>() == Format
                && root["shape"]?.GetValue<string>() is { } shape
                && root["values"] is JsonObject copied)
            {
                values = new InstanceValues(shape, root["source"]?.GetValue<string>() ?? String.Empty, copied);
                return true;
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            // not JSON, or JSON that is not copied values
        }
        return false;
    }

    /// <summary>
    /// Pastes the values into <paramref name="target"/>, which must fit them (see <see cref="Fits"/>): sets the fields that are
    /// edited (not a constant nor supplied by an address map), selects the alternative of each choice and gives each open repeat
    /// the number of items that was copied.
    /// </summary>
    /// <param name="problems">Receives what could not be pasted, e.g. a field of the target that has no copied value.</param>
    /// <returns>The number of fields that received a value.</returns>
    /// <exception cref="InvalidOperationException">The values do not fit <paramref name="target"/>.</exception>
    public int PasteInto(InstanceGroup target, ICollection<string> problems)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(problems);
        if (!Fits(target))
        {
            throw new InvalidOperationException(
                $"The values of '{Source}' ({Shape}) do not fit '{target.DisplayPath}' ({ShapeOf(target)}).");
        }
        var paster = new Paster(problems);
        paster.Paste(target, _values);
        return paster.Count;
    }

    /// <summary>Pastes the copied values into the nodes of a tree, counting the fields and collecting the problems.</summary>
    private sealed class Paster(ICollection<string> problems)
    {
        public int Count { get; private set; }

        public void Paste(InstanceNode node, JsonNode? value)
        {
            switch (node, value)
            {
                case (InstanceField field, JsonValue text) when text.TryGetValue<string>(out var copied):
                    PasteField(field, copied);
                    break;
                case (InstanceGroup group, JsonObject members):
                    PasteGroup(group, members);
                    break;
                case (InstanceRepeat repeat, JsonArray items):
                    PasteRepeat(repeat, items);
                    break;
                case (InstanceChoice choice, JsonObject selection):
                    PasteChoice(choice, selection);
                    break;
                default:
                    problems.Add($"{node.DisplayPath}: the copied value does not fit.");
                    break;
            }
        }

        private void PasteField(InstanceField field, string value)
        {
            if (!field.IsEditable)
            {
                if (!String.Equals(field.Value, value, StringComparison.Ordinal))
                {
                    problems.Add($"{field.DisplayPath}: kept '{field.Value}' ({field.RoleSupply ?? "a constant"}), the copied value is '{value}'.");
                }
                return;
            }
            field.Value = value;
            Count++;
        }

        private void PasteGroup(InstanceGroup group, JsonObject members)
        {
            foreach (var member in group.Members)
            {
                if (members.TryGetPropertyValue(member.Name, out var value))
                {
                    Paste(member, value);
                }
                else
                {
                    problems.Add($"{member.DisplayPath}: has no copied value.");
                }
            }
            var names = group.Members.Select(member => member.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, _) in members.Where(member => !names.Contains(member.Key)))
            {
                problems.Add($"{group.DisplayPath}.{name}: the copied value has no field or group here.");
            }
        }

        private void PasteRepeat(InstanceRepeat repeat, JsonArray items)
        {
            if (repeat.IsOpen)
            {
                repeat.Truncate(items.Count);
                while (repeat.Items.Count < items.Count)
                {
                    repeat.AddNewItem();
                }
            }
            else if (repeat.Items.Count != items.Count)
            {
                problems.Add($"{repeat.DisplayPath}: {items.Count} item(s) copied into {repeat.Items.Count} ({repeat.CountText}).");
            }
            for (var index = 0; index < Math.Min(items.Count, repeat.Items.Count); index++)
            {
                Paste(repeat.Items[index], items[index]);
            }
        }

        private void PasteChoice(InstanceChoice choice, JsonObject selection)
        {
            if (selection["selected"]?.GetValue<string>() is not { } selected)
            {
                return;
            }
            if (!String.Equals(choice.SelectedOption?.Name, selected, StringComparison.OrdinalIgnoreCase))
            {
                var option = choice.Options.FirstOrDefault(option => String.Equals(option.Name, selected, StringComparison.OrdinalIgnoreCase));
                if (option is null || !choice.CanSelect)
                {
                    problems.Add(option is null
                        ? $"{choice.DisplayPath}: has no alternative '{selected}'."
                        : $"{choice.DisplayPath}: the alternative is fixed to '{choice.SelectedOption?.Name}', '{selected}' was copied.");
                    return;
                }
                choice.SelectedOption = option;
            }
            if (choice.Selected is { } body)
            {
                Paste(body, selection["value"]);
            }
        }
    }
}
