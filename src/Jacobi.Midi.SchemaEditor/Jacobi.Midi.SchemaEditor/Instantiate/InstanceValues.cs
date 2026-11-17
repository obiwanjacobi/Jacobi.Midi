using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// The values of a group of an instance tree (a group, repeat item or body of an open choice), copied to be pasted into
/// any group: the members are matched by name, the values of the members both have are pasted (see <see cref="PasteInto"/>).
/// They travel as text (JSON), e.g. on the system clipboard, so they can be pasted in another instantiate or match dialog.
/// Copied are the values of the edited fields (not a constant nor a value supplied by an address map), the selected
/// alternative of each choice and the items of each repeat; not the inclusion of the parts of an address map nor the item of
/// the address space an address map repeat shows (they decide what is sent where).
/// </summary>
internal sealed class InstanceValues
{
    // recognizes the copied values in a text
    private const string Format = "Jacobi.Midi.InstanceValues";

    // the members of a group are found by name, as the serializer does
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly JsonObject _values;

    private InstanceValues(string source, int fieldCount, JsonObject values)
    {
        Source = source;
        FieldCount = fieldCount;
        _values = values;
    }

    /// <summary>The display path of the group the values were copied from, e.g. <c>Root.Patches[3]</c>.</summary>
    public string Source { get; }

    /// <summary>The number of field values that were copied.</summary>
    public int FieldCount { get; }

    /// <summary>Copies the values of <paramref name="group"/>.</summary>
    public static InstanceValues Copy(InstanceGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var fieldCount = 0;
        var values = CopyGroup(group, ref fieldCount);
        return new(group.DisplayPath, fieldCount, values);
    }

    private static JsonObject CopyGroup(InstanceGroup group, ref int fieldCount)
    {
        var values = new JsonObject(NodeOptions);
        foreach (var member in group.Members)
        {
            // a constant or a supplied value is not the user's: it is not copied
            if (member is InstanceField { IsEditable: false })
            {
                continue;
            }
            values[member.Name] = CopyNode(member, ref fieldCount);
        }
        return values;
    }

    private static JsonNode? CopyNode(InstanceNode node, ref int fieldCount)
    {
        switch (node)
        {
            case InstanceField field:
                fieldCount++;
                return JsonValue.Create(field.Value);
            case InstanceGroup group:
                return CopyGroup(group, ref fieldCount);
            case InstanceRepeat repeat:
                var items = new JsonArray();
                foreach (var item in repeat.Items)
                {
                    items.Add(CopyGroup(item, ref fieldCount));
                }
                return items;
            case InstanceChoice choice:
                return new JsonObject(NodeOptions)
                {
                    ["selected"] = choice.SelectedOption?.Name,
                    ["value"] = choice.Selected is { } selected ? CopyNode(selected, ref fieldCount) : null
                };
            default:
                return null;
        }
    }

    /// <summary>The values as text (JSON), recognized by <see cref="TryParse"/>.</summary>
    public string ToText()
    {
        var text = new JsonObject
        {
            ["format"] = Format,
            ["source"] = Source,
            ["fields"] = FieldCount,
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
                && root["fields"]?.GetValue<int>() is { } fieldCount
                && root["values"] is JsonObject copied)
            {
                values = new InstanceValues(root["source"]?.GetValue<string>() ?? String.Empty, fieldCount, copied);
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
    /// True when pasting into <paramref name="target"/> would give a value to at least one of its fields (a member both have,
    /// by name); nothing is changed.
    /// </summary>
    public bool Matches(InstanceGroup target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var paster = new Paster(apply: false);
        paster.Paste(target, _values);
        return paster.Count > 0;
    }

    /// <summary>
    /// Pastes the values into <paramref name="target"/>: the members are matched by name, at every level; a member the target
    /// does not have, or a member without a copied value, is skipped. Sets the edited fields (not a constant nor supplied by
    /// an address map), selects the alternative of each choice (when it has it and the alternative is not fixed) and gives each
    /// open repeat the number of items that was copied.
    /// </summary>
    /// <returns>The number of fields that received a value (of <see cref="FieldCount"/>).</returns>
    public int PasteInto(InstanceGroup target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var paster = new Paster(apply: true);
        paster.Paste(target, _values);
        return paster.Count;
    }

    /// <summary>
    /// Pastes the copied values into the nodes of a tree and counts the fields that receive one; with <c>apply</c> false only
    /// counts (an alternative or repeat items that would have to be created count as one field).
    /// </summary>
    private sealed class Paster(bool apply)
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
                    // another kind of node with the same name: nothing to paste
            }
        }

        private void PasteField(InstanceField field, string value)
        {
            if (!field.IsEditable)
            {
                return;
            }
            if (apply)
            {
                field.Value = value;
            }
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
            }
        }

        private void PasteRepeat(InstanceRepeat repeat, JsonArray items)
        {
            if (repeat.IsOpen && repeat.Items.Count != items.Count)
            {
                if (!apply)
                {
                    // the items are made to measure on a paste: they take the values
                    Count += items.Count > 0 ? 1 : 0;
                    return;
                }
                repeat.Truncate(items.Count);
                while (repeat.Items.Count < items.Count)
                {
                    repeat.AddNewItem();
                }
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
                    return;
                }
                if (!apply)
                {
                    // the alternative is selected on a paste: its body takes the values
                    Count++;
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
