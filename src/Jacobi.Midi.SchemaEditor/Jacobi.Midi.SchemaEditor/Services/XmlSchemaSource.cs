using System.Xml.Linq;
using Jacobi.BinarySerializer.Schema.Editing;

namespace Jacobi.Midi.SchemaEditor.Services;

internal sealed class XmlSchemaSource
{
    private readonly string _text;
    private readonly string _newLine;
    private readonly XDocument _source;
    private readonly XDocument _baseline;
    private readonly Dictionary<XElement, EditNode> _baselineNodes;

    public XmlSchemaSource(string source, string canonical, EditDocument document)
    {
        _text = source;
        _newLine = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        _source = XDocument.Parse(source, LoadOptions.PreserveWhitespace);
        _baseline = XDocument.Parse(canonical);
        _baselineNodes = BindNodes(_baseline.Root!, document);
    }

    public string Serialize(EditDocument document, string canonical)
    {
        var updated = XDocument.Parse(canonical);
        if (XNode.DeepEquals(_baseline, updated))
        {
            return _text;
        }
        var output = new XDocument(_source);
        Merge(output.Root!, _baseline.Root!, updated.Root!, BindNodes(updated.Root!, document));
        var text = output.ToString(SaveOptions.DisableFormatting);
        if (output.Declaration is not null)
        {
            text = output.Declaration + text;
        }
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", _newLine, StringComparison.Ordinal);
    }

    private void Merge(XElement source, XElement baseline, XElement updated, Dictionary<XElement, EditNode> updatedNodes)
    {
        if (XNode.DeepEquals(baseline, updated))
        {
            return;
        }
        foreach (var attribute in baseline.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
        {
            if (updated.Attribute(attribute.Name) is null)
            {
                source.Attribute(attribute.Name)?.Remove();
            }
        }
        foreach (var attribute in updated.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
        {
            if (baseline.Attribute(attribute.Name)?.Value != attribute.Value)
            {
                source.SetAttributeValue(attribute.Name, attribute.Value);
            }
        }
        MergeProperties(source, baseline.Element("properties"), updated.Element("properties"), updatedNodes);
        var oldChildren = baseline.Elements().Where(element => element.Name.LocalName != "properties").ToList();
        var remaining = new HashSet<XElement>(oldChildren);
        var sourceChildren = oldChildren.ToDictionary(element => element, element => FindSourceChild(source, element));
        var orderedChildren = new List<XElement>();
        foreach (var child in updated.Elements().Where(element => element.Name.LocalName != "properties"))
        {
            var previous = updatedNodes.TryGetValue(child, out var node)
                ? oldChildren.FirstOrDefault(candidate => remaining.Contains(candidate)
                    && _baselineNodes.TryGetValue(candidate, out var oldNode) && ReferenceEquals(node, oldNode))
                : oldChildren.FirstOrDefault(candidate => remaining.Contains(candidate) && SameEntry(candidate, child));
            if (previous is null)
            {
                var added = new XElement(child);
                Append(source, added);
                orderedChildren.Add(added);
                continue;
            }
            remaining.Remove(previous);
            if (XNode.DeepEquals(previous, child))
            {
                if (sourceChildren[previous] is { } existing)
                {
                    orderedChildren.Add(existing);
                }
                continue;
            }
            var target = sourceChildren[previous];
            if (target is null && child.Name.LocalName == "processors" && source.Elements("processor").Any())
            {
                target = new XElement("processors");
                var entries = source.Elements("processor").ToList();
                entries[0].AddBeforeSelf(target);
                entries.Remove();
                target.Add(entries);
            }
            if (target is null)
            {
                target = new XElement(child);
                Append(source, target);
            }
            else
            {
                Merge(target, previous, child, updatedNodes);
            }
            orderedChildren.Add(target);
        }
        foreach (var removed in remaining)
        {
            sourceChildren[removed]?.Remove();
            if (sourceChildren[removed] is null && removed.Name.LocalName == "processors")
            {
                source.Elements("processor").Remove();
            }
        }
        if (updated.Elements().Any(updatedNodes.ContainsKey)
            && !source.Elements().Where(orderedChildren.Contains).SequenceEqual(orderedChildren))
        {
            ReorderWithComments(source, orderedChildren);
        }
    }

    private static void ReorderWithComments(XElement source, IReadOnlyList<XElement> elements)
    {
        var bundles = elements.Select(element =>
        {
            var nodes = new List<XNode> { element };
            for (var previous = element.PreviousNode; previous is not null && previous is not XElement; previous = previous.PreviousNode)
            {
                nodes.Insert(0, previous);
            }
            return nodes;
        }).ToList();
        var first = source.Elements().First(elements.Contains);
        var firstBundle = bundles.First(bundle => ReferenceEquals(bundle[^1], first));
        var anchor = new XComment(String.Empty);
        firstBundle[0].AddBeforeSelf(anchor);
        foreach (var bundle in bundles)
        {
            bundle.Remove();
        }
        foreach (var bundle in bundles)
        {
            anchor.AddBeforeSelf(bundle);
        }
        anchor.Remove();
    }

    private void MergeProperties(XElement source, XElement? baseline, XElement? updated, Dictionary<XElement, EditNode> updatedNodes)
    {
        if (XNode.DeepEquals(baseline, updated))
        {
            return;
        }
        var oldProperties = baseline?.Elements("property").ToList() ?? [];
        var newProperties = updated?.Elements("property").ToList() ?? [];
        foreach (var property in oldProperties.Concat(newProperties).DistinctBy(property => (string?)property.Attribute("name")))
        {
            var name = (string?)property.Attribute("name");
            if (name is null)
            {
                continue;
            }
            var previous = oldProperties.FirstOrDefault(candidate => (string?)candidate.Attribute("name") == name);
            var current = newProperties.FirstOrDefault(candidate => (string?)candidate.Attribute("name") == name);
            if (XNode.DeepEquals(previous, current))
            {
                continue;
            }
            var explicitProperty = source.Element("properties")?.Elements("property")
                .FirstOrDefault(candidate => (string?)candidate.Attribute("name") == name);
            var inlineAttribute = source.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name);
            var inlineElement = source.Elements().FirstOrDefault(element => element.Name.LocalName == name);
            if (current is null)
            {
                explicitProperty?.Remove();
                inlineAttribute?.Remove();
                inlineElement?.Remove();
            }
            else if (explicitProperty is not null && previous is not null)
            {
                Merge(explicitProperty, previous, current, updatedNodes);
            }
            else if (inlineAttribute is not null && current.Attribute("value") is { } value)
            {
                inlineAttribute.Value = value.Value;
            }
            else if (inlineElement is not null && current.Attribute("value") is { } elementValue)
            {
                inlineElement.Value = elementValue.Value;
            }
            else
            {
                inlineAttribute?.Remove();
                inlineElement?.Remove();
                var container = source.Element("properties");
                if (container is null)
                {
                    container = new XElement("properties");
                    Append(source, container);
                }
                Append(container, new XElement(current));
            }
        }
        var properties = source.Element("properties");
        if (properties is not null && !properties.HasElements && !properties.Nodes().OfType<XComment>().Any())
        {
            properties.Remove();
        }
    }

    private static bool SameEntry(XElement previous, XElement current)
        => previous.Name == current.Name
        && (string?)previous.Attribute("name") == (string?)current.Attribute("name")
        && (string?)previous.Attribute("schema") == (string?)current.Attribute("schema");

    private static XElement? FindSourceChild(XElement source, XElement canonical)
    {
        var index = canonical.ElementsBeforeSelf(canonical.Name).Count();
        var candidates = source.Elements(canonical.Name);
        if (canonical.Name.LocalName == "processor")
        {
            candidates = candidates.Concat(source.Elements("processors").Elements("processor"));
        }
        return candidates.ElementAtOrDefault(index);
    }

    private void Append(XElement parent, XElement child)
    {
        var indent = new String(' ', parent.Ancestors().Count() * 2 + 2);
        var trailing = parent.LastNode as XText;
        if (trailing is not null && String.IsNullOrWhiteSpace(trailing.Value))
        {
            trailing.AddBeforeSelf(new XText(_newLine + indent), child);
        }
        else
        {
            parent.Add(new XText(_newLine + indent), child, new XText(_newLine + indent[..^2]));
        }
    }

    private static Dictionary<XElement, EditNode> BindNodes(XElement root, EditDocument document)
    {
        var nodes = new Dictionary<XElement, EditNode>();
        BindNode(root, document, nodes);
        foreach (var (element, definition) in (root.Element("nodeDefs")?.Elements("nodeDef") ?? []).Zip(document.NodeDefs))
        {
            BindNode(element, definition, nodes);
        }
        return nodes;
    }

    private static void BindNode(XElement element, EditNode node, Dictionary<XElement, EditNode> nodes)
    {
        nodes.Add(element, node);
        IReadOnlyList<EditNode> members = node switch
        {
            EditGroup group => group.Members,
            EditNodeDef definition => definition.Members,
            _ => []
        };
        foreach (var (child, member) in (element.Element("members")?.Elements() ?? []).Zip(members))
        {
            BindNode(child, member, nodes);
        }
    }
}