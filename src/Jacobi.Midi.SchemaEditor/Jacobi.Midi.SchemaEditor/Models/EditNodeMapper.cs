using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.SchemaEditor.Models;

internal static class EditNodeMapper
{
    public static EditNode ToEditNode(SchemaNode node, EditGroup? parent)
    {
        ArgumentNullException.ThrowIfNull(node);
        EditNode edit = node switch
        {
            SchemaDocument document => ToEditDocument(document),
            SchemaField field => new EditField(parent)
            {
                DataType = field.DataType,
                Value = Copy(field.Value),
                ByteLength = Copy(field.ByteLength),
                ByteOffset = field.ByteOffset,
                Processors = [],
                Properties = [],
            },
            SchemaRepeat repeat => new EditRepeat(parent)
            {
                Count = Copy(repeat.Count),
                Members = [],
                Processors = [],
                Properties = [],
                ValueProcessors = [],
            },
            SchemaChoice choice => new EditChoice(parent)
            {
                SelectedIndex = Copy(choice.SelectedIndex),
                MemberNodeDef = choice.MemberNodeDef,
                Pipeline = choice.PipelineOverride,
                Members = [],
                Processors = [],
                Properties = [],
                ValueProcessors = [],
            },
            SchemaGroup when node.GetType() == typeof(SchemaGroup) => new EditGroup(parent)
            {
                Members = [],
                Processors = [],
                Properties = [],
            },
            _ => throw new NotSupportedException($"Unsupported schema node type: {node.GetType().Name}.")
        };
        if (node is SchemaDocument)
        {
            return edit;
        }
        edit.Name = node.Name;
        edit.NodeDef = node.NodeDef;
        CopyProperties(node.Properties, edit.Properties);
        switch (node, edit)
        {
            case (SchemaField field, EditField target):
                CopyProcessors(field.Processors, target.Processors);
                break;
            case (SchemaGroup group, EditGroup target):
                ReadGroup(group, target);
                break;
        }
        if (node is SchemaRepeat sourceRepeat && edit is EditRepeat editRepeat)
        {
            CopyProcessors(sourceRepeat.ValueProcessors, editRepeat.ValueProcessors);
        }
        if (node is SchemaChoice sourceChoice && edit is EditChoice editChoice)
        {
            CopyProcessors(sourceChoice.ValueProcessors, editChoice.ValueProcessors);
        }
        return edit;
    }

    public static EditDocument ToEditDocument(SchemaDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.IsCompiled)
        {
            throw new ArgumentException("Edit the authored document before compilation, not an expanded runtime schema.", nameof(document));
        }
        var edit = new EditDocument
        {
            Name = document.Name,
            NodeDef = document.NodeDef,
            DataTypeDefs = [],
            Includes = [],
            Members = [],
            NodeDefs = [],
            ProcessorDefs = [],
            Processors = [],
            Properties = [],
        };
        CopyProperties(document.Properties, edit.Properties);
        ReadGroup(document, edit);
        foreach (var definition in document.NodeDefs)
        {
            edit.NodeDefs.Add(ToEditNode(definition, null));
        }
        foreach (var definition in document.DataTypeDefs)
        {
            var target = new EditDataTypeDef
            {
                Name = definition.Name,
                BasedOn = definition.BasedOn,
                Min = definition.Min,
                Max = definition.Max,
                Scale = definition.Scale,
                Shift = definition.Shift,
                Options = definition.Options?.ToDictionary(pair => pair.Key, pair => pair.Value)
            };
            CopyProperties(definition.Properties, target.Properties);
            CopyProcessors(definition.Processors, target.Processors);
            edit.DataTypeDefs.Add(target);
        }
        foreach (var definition in document.ProcessorDefs)
        {
            var target = new EditProcessorDef { Name = definition.Name, Processor = definition.Processor.ToString() };
            CopyProperties(definition.Properties, target.Properties);
            edit.ProcessorDefs.Add(target);
        }
        foreach (var include in document.Includes)
        {
            edit.Includes.Add(new EditDocumentRef { Schema = include.Schema, Path = include.Path });
        }
        return edit;
    }

    public static SchemaNode ToSchemaNode(EditNode edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit is EditDocument document)
        {
            return ToSchemaDocument(document);
        }
        return new SchemaDocumentBuilder(edit.Name)
            .NodeDefs(nodes => WriteNode(edit, nodes))
            .Build().NodeDefs.Single();
    }

    public static SchemaDocument ToSchemaDocument(EditDocument edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var builder = new SchemaDocumentBuilder(edit.Name, edit.NodeDef?.ToString(), Copy(edit.ByteSize),
            WriteProperties(edit.Properties), processors => WriteProcessors(edit.Processors, processors));
        builder.Members(nodes => WriteNodes(edit.Members, nodes));
        builder.NodeDefs(nodes => WriteNodes(edit.NodeDefs, nodes));
        foreach (var definition in edit.DataTypeDefs)
        {
            builder.DataTypeDef(definition.Name, definition.BasedOn.ToString(),
                definition.Min, definition.Max, definition.Scale, definition.Shift,
                definition.Options?.ToDictionary(pair => pair.Key, pair => pair.Value),
                processors => WriteProcessors(definition.Processors, processors), WriteProperties(definition.Properties));
        }
        foreach (var definition in edit.ProcessorDefs)
        {
            builder.ProcessorDef(definition.Name, definition.Processor,
                definition.Properties.Select(property => (property.Name, property.Value)).ToArray());
        }
        foreach (var include in edit.Includes)
        {
            builder.Include(include.Schema, include.Path);
        }
        return builder.Build();
    }

    private static void ReadGroup(SchemaGroup source, EditGroup target)
    {
        target.ByteSize = Copy(source.ByteSize);
        foreach (var member in source.Members)
        {
            target.Members.Add(ToEditNode(member, target));
        }
        CopyProcessors(source.Processors, target.Processors);
    }

    private static void WriteNodes(IEnumerable<EditNode> nodes, SchemaDocumentBuilder.GroupBuilder builder)
    {
        foreach (var node in nodes)
        {
            WriteNode(node, builder);
        }
    }

    private static void WriteNode(EditNode edit, SchemaDocumentBuilder.GroupBuilder builder)
    {
        var nodeDef = edit.NodeDef?.ToString();
        var properties = WriteProperties(edit.Properties);
        switch (edit)
        {
            case EditField field:
                builder.Field(edit.Name, field.DataType?.ToString(), nodeDef,
                    processors => WriteProcessors(field.Processors, processors),
                    Copy(field.Value), Copy(field.ByteLength), field.ByteOffset, properties);
                break;
            case EditRepeat repeat:
                builder.Repeat(edit.Name, Copy(repeat.Count), nodes => WriteNodes(repeat.Members, nodes), nodeDef,
                    processors => WriteProcessors(repeat.Processors, processors), Copy(repeat.ByteSize),
                    processors => WriteProcessors(repeat.ValueProcessors, processors), properties);
                break;
            case EditChoice choice:
                builder.Choice(edit.Name, Copy(choice.SelectedIndex), nodes => WriteNodes(choice.Members, nodes), nodeDef,
                    processors => WriteProcessors(choice.Processors, processors), Copy(choice.ByteSize),
                    processors => WriteProcessors(choice.ValueProcessors, processors), choice.Pipeline,
                    choice.MemberNodeDef?.ToString(), properties);
                break;
            case EditGroup group when edit.GetType() == typeof(EditGroup):
                builder.Group(edit.Name, nodes => WriteNodes(group.Members, nodes), nodeDef,
                    processors => WriteProcessors(group.Processors, processors), Copy(group.ByteSize), properties);
                break;
            default:
                throw new NotSupportedException($"Unsupported edit node type: {edit.GetType().Name}.");
        }
    }

    private static void CopyProperties(IEnumerable<SchemaProperty> source, ICollection<EditProperty> target)
    {
        foreach (var property in source)
        {
            target.Add(new EditProperty { Name = property.Name, Value = property.Value });
        }
    }

    private static List<SchemaProperty> WriteProperties(IEnumerable<EditProperty> source)
        => source.Select(property => new SchemaProperty { Name = property.Name, Value = property.Value }).ToList();

    private static void CopyProcessors(IEnumerable<SchemaProcessorRef> source, ICollection<EditProcessorRef> target)
    {
        foreach (var processor in source)
        {
            var edit = new EditProcessorRef { Processor = processor.Processor, PublishNamespace = processor.PublishNamespace };
            CopyProperties(processor.Properties, edit.Properties);
            target.Add(edit);
        }
    }

    private static void WriteProcessors(IEnumerable<EditProcessorRef> source, SchemaDocumentBuilder.ProcessorBuilder builder)
    {
        foreach (var processor in source)
        {
            builder.Add(processor.Processor.ToString(),
                processor.Properties.Select(property => new KeyValuePair<string, string?>(property.Name, property.Value)),
                processor.PublishNamespace);
        }
    }

    private static SchemaValueOrRef<T> Copy<T>(SchemaValueOrRef<T> value) => value switch
    {
        SchemaNodeRef reference => new SchemaNodeRef { Path = reference.Path, Indices = reference.Indices.ToArray() },
        SchemaPubRef reference => new SchemaPubRef { Namespace = reference.Namespace, Name = reference.Name },
        T constant => constant,
        _ => default
    };
}