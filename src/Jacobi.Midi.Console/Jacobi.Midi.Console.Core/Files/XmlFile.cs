using System.Xml;
using System.Xml.Linq;

namespace Jacobi.Midi.Console.Core.Files;

/// <summary>
/// The XML form of a <see cref="FileElement"/> tree: elements with attributes and child elements, no namespace.
/// </summary>
internal static class XmlFile
{
    public static FileElement Read(string text, string rootName)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(text);
        }
        catch (XmlException exception)
        {
            throw new FileFormatException($"Invalid XML: {exception.Message}", exception);
        }

        var root = document.Root!;
        if (root.Name != XName.Get(rootName))
            throw new FileFormatException($"The root element is '{root.Name}', not '{rootName}'.");
        return ToElement(root);
    }

    public static string Write(FileElement root)
        => new XDocument(ToXml(root)).ToString();

    private static FileElement ToElement(XElement xml)
    {
        var element = new FileElement(xml.Name.LocalName);
        if (xml.Name.Namespace != XNamespace.None)
            throw element.Error($"elements in a namespace ('{xml.Name.NamespaceName}') are not supported.");

        foreach (var attribute in xml.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
            element.Set(attribute.Name.LocalName, attribute.Value);
        foreach (var child in xml.Elements())
            element.Add(ToElement(child));
        return element;
    }

    private static XElement ToXml(FileElement element)
        => new(element.Name,
            element.Attributes.Select(attribute => new XAttribute(attribute.Name, attribute.Value)),
            element.Children.Select(ToXml));
}
