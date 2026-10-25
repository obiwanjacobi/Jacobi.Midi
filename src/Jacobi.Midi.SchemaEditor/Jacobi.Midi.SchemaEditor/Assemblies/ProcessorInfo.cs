using System.Globalization;
using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.SchemaEditor.Assemblies;

/// <summary>
/// What a processor of an opened assembly publishes, shown in the processor namespace details of the assembly view:
/// its key, descriptive name, pipeline stage and the properties it supports. Read-only.
/// </summary>
/// <param name="Key">The processor key as used in schemas ('namespace.id').</param>
/// <param name="Name">The descriptive name of the processor; empty when it could not be created.</param>
/// <param name="Stage">The pipeline stage the processor operates in; null when it could not be created.</param>
/// <param name="Properties">The properties the processor publishes.</param>
/// <param name="Problem">Why the processor could not be created; null when it was.</param>
internal sealed record ProcessorInfo(string Key, string Name, PipelineStage? Stage, IReadOnlyList<ProcessorPropertyInfo> Properties, string? Problem)
{
    public string StageText => Stage?.ToString() ?? String.Empty;
    public bool HasProperties => Properties.Count > 0;
    public bool HasNoProperties => Problem is null && Properties.Count == 0;
    public bool HasProblem => Problem is not null;

    /// <summary>
    /// Creates the info of the processor <paramref name="id"/> of <paramref name="factory"/>.
    /// The data types of the properties are looked up in <paramref name="dataTypes"/> for their options and range.
    /// </summary>
    public static ProcessorInfo Create(IProcessorFactory factory, string id, IDataTypeRegistry dataTypes)
    {
        var key = new ProcessorKey(factory.Namespace, id).ToString();
        try
        {
            if (factory.CreateProcessor(id) is not { } processor)
            {
                return new(key, String.Empty, null, [], "The factory did not create the processor.");
            }
            var properties = processor.Properties
                .Select(descriptor => ProcessorPropertyInfo.Create(descriptor, dataTypes))
                .ToList();
            return new(key, processor.Name, processor.Stage, properties, null);
        }
        catch (Exception exception) when (exception is NotImplementedException or NotSupportedException
            or InvalidOperationException or ArgumentException)
        {
            return new(key, String.Empty, null, [], exception.Message);
        }
    }
}

/// <summary>
/// A property a processor publishes (<see cref="PropertyDescriptor"/>), shown in the assembly view.
/// </summary>
/// <param name="Name">The (short) property name.</param>
/// <param name="DataType">The data type of the property value.</param>
/// <param name="Flags">Whether the property is required, read-only or published; empty when none applies.</param>
/// <param name="Description">The description of the property; may be empty.</param>
/// <param name="Values">The allowed values of the data type (its options or range); empty when not known.</param>
internal sealed record ProcessorPropertyInfo(string Name, string DataType, string Flags, string Description, string Values)
{
    public bool HasFlags => Flags.Length > 0;
    public bool HasDescription => Description.Length > 0;
    public bool HasValues => Values.Length > 0;

    public static ProcessorPropertyInfo Create(PropertyDescriptor descriptor, IDataTypeRegistry dataTypes)
    {
        var flags = new List<string>();
        if (descriptor.IsRequired)
        {
            flags.Add("required");
        }
        if (descriptor.IsReadOnly)
        {
            flags.Add("read-only");
        }
        if (descriptor.IsPublished)
        {
            flags.Add("published");
        }

        var values = String.Empty;
        if (dataTypes.TryGet(descriptor.DataType, out var dataType))
        {
            if (dataType.Options is { Count: > 0 } options)
            {
                values = "Options: " + String.Join(", ", options.OrderBy(option => option.Key).Select(option => $"{Format(option.Value)} ({Format(option.Key)})"));
            }
            else if (dataType.ClrType.IsEnum)
            {
                // an enum data type (DataTypeDescriptor.ForEnum) takes the member names, case-insensitive
                values = "Values: " + String.Join(", ", Enum.GetNames(dataType.ClrType));
            }
            else if (dataType.Min is not null || dataType.Max is not null)
            {
                values = $"Range: {Format(dataType.Min)} to {Format(dataType.Max)}";
            }
        }
        return new(descriptor.Name, descriptor.DataType.FullName, String.Join(", ", flags), descriptor.Description, values);
    }

    private static string Format(object? value) => value is null ? "…" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
}
