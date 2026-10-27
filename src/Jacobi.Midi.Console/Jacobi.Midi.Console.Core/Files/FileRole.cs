namespace Jacobi.Midi.Console.Core.Files;

/// <summary>
/// The role of child elements in their parent: how they appear in JSON. In XML child elements are always plain
/// child elements; JSON groups them in a property by role.
/// </summary>
/// <param name="JsonName">The JSON property that holds the elements.</param>
/// <param name="ElementName">The name of the elements; null when the elements differ (controls), then JSON gives it in <c>type</c>.</param>
/// <param name="KeyAttribute">For <see cref="FileRoleShape.Map"/>: the attribute that is the JSON key.</param>
/// <param name="ValueAttribute">For <see cref="FileRoleShape.Map"/> and <see cref="FileRoleShape.Values"/>: the attribute that is the JSON value.</param>
internal sealed record FileRole(string JsonName, FileRoleShape Shape, string? ElementName = null,
    string? KeyAttribute = null, string? ValueAttribute = null);

/// <summary>How the elements of a role are written in JSON.</summary>
internal enum FileRoleShape
{
    /// <summary>One element: an object.</summary>
    Single,
    /// <summary>Elements: an array of objects.</summary>
    List,
    /// <summary>Elements with a key and a value attribute: an object <c>{ key: value }</c>.</summary>
    Map,
    /// <summary>Elements with only a value attribute: an array of values.</summary>
    Values,
}

/// <summary>
/// All roles of the console's files, by JSON property name: the single description of how the files are shaped in
/// JSON, used by both reading and writing.
/// </summary>
internal static class FileRoles
{
    public static FileRole Assemblies { get; } = new("assemblies", FileRoleShape.Values, "assembly", ValueAttribute: "path");
    public static FileRole Schemas { get; } = new("schemas", FileRoleShape.Values, "schema", ValueAttribute: "path");
    public static FileRole Devices { get; } = new("devices", FileRoleShape.List, "device");
    public static FileRole Variables { get; } = new("variables", FileRoleShape.List, "variable");
    public static FileRole DeviceValues { get; } = new("deviceValues", FileRoleShape.Map, "deviceValue", "name", "value");
    public static FileRole Endpoint { get; } = new("endpoint", FileRoleShape.Single, "endpoint");
    public static FileRole Layout { get; } = new("layout", FileRoleShape.Single);
    public static FileRole Children { get; } = new("children", FileRoleShape.List);
    public static FileRole Bind { get; } = new("bind", FileRoleShape.Map, "bind", "slot", "parameter");
    public static FileRole Segments { get; } = new("segments", FileRoleShape.List, "segment");

    private static readonly Dictionary<string, FileRole> ByJsonName =
        new[] { Assemblies, Schemas, Devices, Variables, DeviceValues, Endpoint, Layout, Children, Bind, Segments }
            .ToDictionary(role => role.JsonName, StringComparer.Ordinal);

    public static bool TryGet(string jsonName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FileRole? role)
        => ByJsonName.TryGetValue(jsonName, out role);
}
