using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// Checks processor properties against the <see cref="PropertyDescriptor"/>s the processor publishes:
/// the name must be published, a read-only property cannot be given a value, the value must be valid for the
/// data type of the property and the required properties must be present.
/// Produces warnings only. Property names are short ('bytes') or prefixed with the processor key ('sys.align.bytes').
/// </summary>
internal static class ProcessorPropertyValidator
{
    /// <summary>
    /// The descriptor of the property <paramref name="name"/> of the processor <paramref name="processorKey"/>; null when it is not published.
    /// </summary>
    public static PropertyDescriptor? Find(string processorKey, IReadOnlyList<PropertyDescriptor> descriptors, string name)
    {
        var prefix = processorKey + ProcessorKey.Separator;
        var shortName = name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name[prefix.Length..] : name;
        return descriptors.FirstOrDefault(descriptor => String.Equals(descriptor.Name, shortName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns a warning when the property <paramref name="name"/> with <paramref name="value"/> does not match the
    /// properties published by the processor <paramref name="processorKey"/>; otherwise null.
    /// </summary>
    /// <param name="document">The document the property is in; its data type defs are known for the value check.</param>
    /// <param name="dataTypes">The registered data types, including those of the processor factories.</param>
    public static string? Validate(string processorKey, IReadOnlyList<PropertyDescriptor> descriptors, string name, string? value,
        EditDocument? document, DataTypeRegistry dataTypes)
    {
        if (String.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        if (Find(processorKey, descriptors, name.Trim()) is not { } descriptor)
        {
            return $"'{name}' is not a property of {processorKey}.";
        }
        if (descriptor.IsReadOnly && !String.IsNullOrWhiteSpace(value))
        {
            return $"'{descriptor.Name}' is read-only for {processorKey}.";
        }
        return DataTypeValueValidator.Validate(document is null ? [] : [document], document?.Name ?? String.Empty,
            descriptor.DataType.FullName, value ?? String.Empty, dataTypes);
    }

    /// <summary>
    /// Returns a warning naming the required properties of the processor <paramref name="processorKey"/> that are not
    /// among <paramref name="names"/>; null when all are present.
    /// </summary>
    public static string? ValidateRequired(string processorKey, IReadOnlyList<PropertyDescriptor> descriptors, IEnumerable<string> names)
    {
        var present = names.Select(name => Find(processorKey, descriptors, name)).OfType<PropertyDescriptor>().ToHashSet();
        var missing = descriptors.Where(descriptor => descriptor.IsRequired && !present.Contains(descriptor)).Select(descriptor => $"'{descriptor.Name}'").ToList();
        return missing.Count switch
        {
            0 => null,
            1 => $"The required property {missing[0]} of {processorKey} is missing.",
            _ => $"The required properties {String.Join(", ", missing)} of {processorKey} are missing."
        };
    }

    /// <summary>
    /// Splits a property name prefixed with a processor key ('namespace.id.name') into the key and the short name.
    /// Returns false when the name has fewer than three parts.
    /// </summary>
    public static bool TrySplitPrefixed(string name, out string processorKey, out string shortName)
    {
        processorKey = shortName = String.Empty;
        var index = name.LastIndexOf(ProcessorKey.Separator);
        if (index <= 0 || index == name.Length - 1 || !name[..index].Contains(ProcessorKey.Separator))
        {
            return false;
        }
        processorKey = name[..index];
        shortName = name[(index + 1)..];
        return true;
    }
}
