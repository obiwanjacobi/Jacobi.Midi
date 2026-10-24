using Jacobi.BinarySerializer.Processor;
using Jacobi.Midi.SchemaEditor.Instantiate;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.Workbench;

/// <summary>
/// The outcome of reading the message of one capture with the target schema: the value of each field (by path) and,
/// when the read failed, why. The values read up to a failure remain.
/// </summary>
/// <param name="Capture">The capture that was read.</param>
/// <param name="Values">The value text of each field, by its path (e.g. 'Message1.Level').</param>
/// <param name="Error">Why reading failed; null when it succeeded.</param>
/// <param name="Remark">A note on the read (one per line), e.g. trailing bytes that were not read or a message that is read only partly into an address map; null when there is none.</param>
internal sealed record CaptureRead(Capture Capture, IReadOnlyDictionary<string, string> Values, string? Error, string? Remark);

/// <summary>
/// Reads the messages of the captures with the (draft) target schema, the same way Match does,
/// so the workbench shows the logical value of a field in every capture while the field is being defined.
/// </summary>
internal static class WorkbenchReader
{
    /// <summary>
    /// Reads the message of <paramref name="slot"/> of each capture with <paramref name="root"/>.
    /// </summary>
    /// <param name="instantiator">The compiled target schema.</param>
    /// <param name="root">The root group of the slot.</param>
    /// <param name="processors">The processors the schema uses.</param>
    public static IReadOnlyList<CaptureRead> Read(SchemaInstantiator instantiator, EditGroup root, int slot,
        IReadOnlyList<Capture> captures, IProcessorProvider processors)
    {
        ArgumentNullException.ThrowIfNull(instantiator);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(captures);
        ArgumentNullException.ThrowIfNull(processors);
        var rootName = instantiator.FindRootName(root) ?? throw new InvalidOperationException($"'{root.Name}' is not a root of the schema.");
        return captures.Select(capture => Read(instantiator, rootName, root.Name, slot, capture, processors)).ToList();
    }

    private static CaptureRead Read(SchemaInstantiator instantiator, Jacobi.BinarySerializer.Schema.SchemaName rootName, string root,
        int slot, Capture capture, IProcessorProvider processors)
    {
        if (slot >= capture.Messages.Count)
        {
            return new CaptureRead(capture, new Dictionary<string, string>(), "The capture has no message in this slot.", null);
        }

        var bytes = capture.Messages[slot];
        var instance = instantiator.CreateRoot(root);
        string? error = null;
        var remarks = new List<string>();
        try
        {
            var read = instantiator.Deserialize(rootName, instance, bytes, processors, remarks);
            if (read < bytes.Length)
            {
                remarks.Add($"{bytes.Length - read} trailing byte(s) were not read.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // the engine and the processors can fail with their own exception types
            error = exception.Message;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in instance.Fields())
        {
            values[field.Path.Value] = field.Value;
        }
        return new CaptureRead(capture, values, error, remarks.Count > 0 ? String.Join(Environment.NewLine, remarks) : null);
    }
}
