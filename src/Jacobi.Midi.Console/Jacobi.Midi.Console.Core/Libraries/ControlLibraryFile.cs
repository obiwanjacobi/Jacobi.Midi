using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Files;

namespace Jacobi.Midi.Console.Core.Libraries;

/// <summary>
/// Reads and writes libraries of user controls as XML or JSON: a <c>userControl</c> element per user control, with its
/// <c>name</c>, its contract (<c>nodeDef</c>, the full name <c>document.nodeDef</c>) and its section, written as in a
/// dashboard (<see cref="DashboardFile"/>). Content that does not fit (unknown attributes or elements, invalid values,
/// a section that does not fit a user control: <see cref="UserControlDefinition.Check"/>) is reported with its
/// location, never ignored.
/// </summary>
public static class ControlLibraryFile
{
    /// <summary>The kind and version of the file, in its <c>format</c> attribute.</summary>
    public const string Format = "jacobi.midi.library/1";

    private const string RootName = "library";

    public static ControlLibrary Read(string text, FileFormat format)
        => FromElement(ConsoleFile.Read(text, format, RootName));

    public static string Write(ControlLibrary library, FileFormat format)
    {
        ArgumentNullException.ThrowIfNull(library);
        return ConsoleFile.Write(ToElement(library), format);
    }

    // writing

    private static FileElement ToElement(ControlLibrary library)
    {
        var root = new FileElement(RootName).Set(ConsoleFile.FormatAttribute, Format);
        foreach (var control in library.Controls)
        {
            root.Add(new FileElement(FileRoles.UserControls.ElementName!, FileRoles.UserControls)
                    .Set("name", control.Name)
                    .Set("nodeDef", control.NodeDef.FullName))
                .Add(DashboardFile.ToElement(control.Section, FileRoles.Layout));
        }
        return root;
    }

    // reading

    private static ControlLibrary FromElement(FileElement root)
    {
        ConsoleFile.CheckFormat(root, Format);

        var library = new ControlLibrary();
        foreach (var child in root.TakeChildren())
        {
            if (child.Name != FileRoles.UserControls.ElementName)
                throw child.Error($"a library cannot contain '{child.Name}'.");
            var control = ReadUserControl(child);
            if (library.Put(control))
                throw child.Error($"the user control '{control.Name}' is in the library more than once.");
        }

        root.EnsureAllRead();
        return library;
    }

    private static UserControlDefinition ReadUserControl(FileElement element)
    {
        var name = element.GetRequired("name");
        var nodeDef = new SchemaName(element.GetRequired("nodeDef"));
        if (String.IsNullOrEmpty(nodeDef.Namespace) || String.IsNullOrEmpty(nodeDef.Name))
            throw element.Error($"'nodeDef': '{nodeDef}' is not the full name of a nodeDef ('document{SchemaName.Separator}nodeDef').");

        var children = element.TakeChildren();
        if (children.Count != 1)
            throw element.Error($"the user control '{name}' has one section.");
        var section = DashboardFile.ReadControl(children[0]) as LayoutControl
            ?? throw children[0].Error($"the user control '{name}' is a section, not a '{children[0].Name}'.");
        if (UserControlDefinition.Check(section) is [var problem, ..])
            throw children[0].Error(problem);

        element.EnsureAllRead();
        return new UserControlDefinition { Name = name, NodeDef = nodeDef, Section = section };
    }
}
