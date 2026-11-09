using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>
/// Reads an exchange file, XML or JSON, into an <see cref="ExchangeDefinitionSet"/> and checks what can be checked
/// without the schemas: the elements and attributes, the names, and the turns (an unknown or unreachable turn, a
/// transition on an event the turn cannot have, a retry on an event that cannot be retried). Whether the messages
/// exist and fit the map is checked when an exchange is used. Both formats are turned into the same tree of nodes
/// first, so the checks are the same; the formats are described by <c>exchange.xsd</c> and <c>exchange.schema.json</c>
/// next to this reader.
/// </summary>
public static class ExchangeDefinitionReader
{
    private const int DefaultResponseTimeout = 1000;

    /// <summary>Reads an exchange file from its XML text.</summary>
    /// <param name="source">Where the text comes from (a resource or a path), to report problems.</param>
    /// <exception cref="ExchangeDefinitionException">The file is not valid; all problems found are listed.</exception>
    public static ExchangeDefinitionSet ReadXml(string xml, string source)
    {
        ArgumentNullException.ThrowIfNull(xml);
        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            throw new ExchangeDefinitionException(source, [exception.Message]);
        }
        return Read(XmlNodes.From(document.Root!), source, []);
    }

    /// <summary>Reads an exchange file from its JSON text.</summary>
    /// <inheritdoc cref="ReadXml(string, string)"/>
    public static ExchangeDefinitionSet ReadJson(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException exception)
        {
            throw new ExchangeDefinitionException(source, [exception.Message]);
        }
        using (document)
        {
            var problems = new List<string>();
            return Read(JsonNodes.From(document.RootElement, problems), source, problems);
        }
    }

    /// <summary>Reads an exchange file from a stream (e.g. an embedded resource); the extension of <paramref name="source"/> tells the format (.xml or .json).</summary>
    /// <inheritdoc cref="ReadXml(string, string)"/>
    public static ExchangeDefinitionSet Read(Stream stream, string source)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(source);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        return IsJson(source) ? ReadJson(text, source) : ReadXml(text, source);
    }

    /// <summary>True when the name (a path or resource) is a JSON file; otherwise it is read as XML.</summary>
    internal static bool IsJson(string name) => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static ExchangeDefinitionSet Read(Node root, string source, List<string> problems)
    {
        if (root.Name != "exchanges")
        {
            throw new ExchangeDefinitionException(source, [.. problems, $"{root.Location}the root must be 'exchanges', not '{root.Name}'."]);
        }

        Attributes(root, problems, "for", "responseTimeout", "messageInterval");
        var forRoot = Required(root, "for", problems);
        var responseTimeout = Milliseconds(root, "responseTimeout", problems) ?? DefaultResponseTimeout;
        var messageInterval = Milliseconds(root, "messageInterval", problems) ?? 0;

        var exchanges = new List<ExchangeDefinition>();
        foreach (var node in Children(root, problems, "exchange"))
        {
            if (ReadExchange(node, responseTimeout, messageInterval, problems) is { } exchange)
            {
                if (exchanges.Any(existing => String.Equals(existing.Name, exchange.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add($"{node.Location}the exchange '{exchange.Name}' is defined more than once.");
                }
                exchanges.Add(exchange);
            }
        }
        if (exchanges.Count == 0 && problems.Count == 0)
        {
            problems.Add($"{root.Location}the file has no exchange.");
        }

        if (problems.Count > 0)
        {
            throw new ExchangeDefinitionException(source, problems);
        }
        return new ExchangeDefinitionSet { For = new SchemaName(forRoot!), Exchanges = exchanges, Source = source };
    }

    private static ExchangeDefinition? ReadExchange(Node node, int responseTimeout, int messageInterval, List<string> problems)
    {
        var count = problems.Count;
        Attributes(node, problems, "name", "responseTimeout", "messageInterval");
        var name = Required(node, "name", problems);
        var timeout = Milliseconds(node, "responseTimeout", problems) ?? responseTimeout;
        var interval = Milliseconds(node, "messageInterval", problems) ?? messageInterval;

        var children = Children(node, problems, "turn", "before", "after").ToList();
        var turns = ReadTurns(children.Where(child => child.Name == "turn"), problems);
        var before = ReadPart(node, children, "before", problems);
        var after = ReadPart(node, children, "after", problems);
        if (turns.Count == 0)
        {
            problems.Add($"{node.Location}the exchange '{name}' has no turn.");
        }
        if (name is null || turns.Count == 0)
        {
            return null;
        }

        // the turns are checked also when others have problems: all problems are reported at once
        var exchange = new ExchangeDefinition
        {
            Name = name,
            ResponseTimeout = TimeSpan.FromMilliseconds(timeout),
            MessageInterval = TimeSpan.FromMilliseconds(interval),
            Turns = turns,
            Before = before,
            After = after,
        };
        CheckTurns(node, exchange.Name, null, exchange.Turns, problems);
        CheckTurns(node, exchange.Name, "before", exchange.Before, problems);
        CheckTurns(node, exchange.Name, "after", exchange.After, problems);
        return problems.Count > count ? null : exchange;
    }

    // the turns once per call (a 'before' or 'after'), at most one of each; they receive no data
    private static IReadOnlyList<ExchangeTurn> ReadPart(Node exchange, List<Node> children, string part, List<string> problems)
    {
        var parts = children.Where(child => child.Name == part).ToList();
        if (parts.Count == 0)
        {
            return [];
        }
        if (parts.Count > 1)
        {
            problems.Add($"{parts[1].Location}an exchange has one '{part}': there is more than one.");
        }
        var node = parts[0];
        Attributes(node, problems);
        var turns = ReadTurns(Children(node, problems, "turn"), problems);
        if (turns.Count == 0)
        {
            problems.Add($"{node.Location}'{part}' has no turn.");
        }
        foreach (var turn in turns.Where(turn => turn.Receive.Count > 0))
        {
            problems.Add($"{node.Location}'{part}', turn '{turn.Name}': a 'receive' is not possible: the turns {part} the blocks receive no data.");
        }
        return turns;
    }

    // the turns of one part, each name once
    private static List<ExchangeTurn> ReadTurns(IEnumerable<Node> nodes, List<string> problems)
    {
        var turns = new List<ExchangeTurn>();
        foreach (var turnNode in nodes)
        {
            var turn = ReadTurn(turnNode, problems);
            if (turn is not null)
            {
                if (turns.Any(existing => String.Equals(existing.Name, turn.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add($"{turnNode.Location}the turn '{turn.Name}' is defined more than once.");
                }
                turns.Add(turn);
            }
        }
        return turns;
    }

    private static ExchangeTurn? ReadTurn(Node node, List<string> problems)
    {
        var count = problems.Count;
        Attributes(node, problems, "name");
        var name = Required(node, "name", problems);

        SchemaName? send = null;
        var receive = new List<SchemaName>();
        var transitions = new List<ExchangeTransition>();
        foreach (var child in Children(node, problems, "send", "receive", "on"))
        {
            switch (child.Name)
            {
                case "send":
                    Attributes(child, problems, "message");
                    if (send is not null)
                    {
                        problems.Add($"{child.Location}a turn sends one message: there is more than one 'send'.");
                    }
                    send = Required(child, "message", problems) is { } sent ? new SchemaName(sent) : send;
                    break;
                case "receive":
                    Attributes(child, problems, "message");
                    if (Required(child, "message", problems) is { } received)
                    {
                        receive.Add(new SchemaName(received));
                    }
                    break;
                default:
                    if (ReadTransition(child, problems) is { } transition)
                    {
                        transitions.Add(transition);
                    }
                    break;
            }
        }

        if (problems.Count > count || name is null)
        {
            return null;
        }
        return new ExchangeTurn { Name = name, Send = send, Receive = receive, Transitions = transitions };
    }

    private static ExchangeTransition? ReadTransition(Node node, List<string> problems)
    {
        var count = problems.Count;
        Attributes(node, problems, "event", "message", "retry", "goto", "result");
        var eventName = node.Attribute("event");
        var message = node.Attribute("message");
        var target = node.Attribute("goto");
        var resultName = node.Attribute("result");

        if ((eventName is null) == (message is null))
        {
            problems.Add($"{node.Location}an 'on' has either an 'event' or a 'message'.");
        }
        if ((target is null) == (resultName is null))
        {
            problems.Add($"{node.Location}an 'on' has either a 'goto' or a 'result'.");
        }

        var @event = ExchangeEvent.Message;
        if (eventName is not null && !TryParseEvent(eventName, out @event))
        {
            problems.Add($"{node.Location}the event must be sent, received or timeout, not '{eventName}'.");
        }
        BlockStatus? result = null;
        if (resultName is not null)
        {
            result = ParseResult(resultName);
            if (result is null)
            {
                problems.Add($"{node.Location}the result must be completed, rejected, failed or timedOut, not '{resultName}'.");
            }
        }
        var retry = Retry(node, problems);
        if (retry > 0 && (@event is ExchangeEvent.Sent or ExchangeEvent.Received))
        {
            // on sent the turn would send again without waiting; on received there is nothing left to retry
            problems.Add($"{node.Location}a 'retry' is for 'timeout' or a message, not for '{eventName}'.");
        }

        return problems.Count > count
            ? null
            : new ExchangeTransition { Event = @event, Message = message is null ? (SchemaName?)null : new SchemaName(message), Retry = retry, Goto = target, Result = result };
    }

    // per part (the turns per block, 'before' or 'after'): the targets of the transitions are turns of the same part,
    // every turn can be reached from its first one, and an event fits its turn
    private static void CheckTurns(Node node, string exchange, string? part, IReadOnlyList<ExchangeTurn> turns, List<string> problems)
    {
        if (turns.Count == 0)
        {
            return;
        }
        var of = part is null ? $"'{exchange}'" : $"'{exchange}' ({part})";
        foreach (var turn in turns)
        {
            foreach (var transition in turn.Transitions)
            {
                if (transition.Goto is { } target && ExchangeDefinition.Find(turns, target) is null)
                {
                    problems.Add($"{node.Location}{of}, turn '{turn.Name}': there is no turn '{target}' to go to"
                        + (part is null ? "." : $" in '{part}' (a 'goto' stays in its part)."));
                }
                if (transition.Event == ExchangeEvent.Sent && turn.Send is null)
                {
                    problems.Add($"{node.Location}{of}, turn '{turn.Name}': 'sent' needs a 'send' in the turn.");
                }
                if (transition.Event == ExchangeEvent.Received && turn.Receive.Count == 0)
                {
                    problems.Add($"{node.Location}{of}, turn '{turn.Name}': 'received' needs a 'receive' in the turn.");
                }
            }
        }

        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var next = new Stack<ExchangeTurn>([turns[0]]);
        while (next.TryPop(out var turn))
        {
            if (!reached.Add(turn.Name))
            {
                continue;
            }
            foreach (var target in turn.Transitions.Select(transition => transition.Goto).OfType<string>())
            {
                if (ExchangeDefinition.Find(turns, target) is { } found)
                {
                    next.Push(found);
                }
            }
        }
        foreach (var turn in turns.Where(turn => !reached.Contains(turn.Name)))
        {
            problems.Add($"{node.Location}{of}, turn '{turn.Name}' is never reached.");
        }
    }

    private static bool TryParseEvent(string name, out ExchangeEvent @event)
    {
        @event = name.ToLowerInvariant() switch
        {
            "sent" => ExchangeEvent.Sent,
            "received" => ExchangeEvent.Received,
            "timeout" => ExchangeEvent.Timeout,
            _ => ExchangeEvent.Message,
        };
        return @event != ExchangeEvent.Message;
    }

    private static BlockStatus? ParseResult(string name)
        => name.ToLowerInvariant() switch
        {
            "completed" => BlockStatus.Completed,
            "rejected" => BlockStatus.Rejected,
            "failed" => BlockStatus.Failed,
            "timedout" => BlockStatus.TimedOut,
            _ => null,
        };

    // the children; an unknown one is a problem (a typo must not be ignored)
    private static IEnumerable<Node> Children(Node node, List<string> problems, params string[] names)
    {
        foreach (var child in node.Children)
        {
            if (names.Contains(child.Name))
            {
                yield return child;
            }
            else
            {
                problems.Add($"{child.Location}'{child.Name}' is not expected in '{node.Name}' (expected: {String.Join(", ", names.Select(name => $"'{name}'"))}).");
            }
        }
    }

    // an unknown attribute is a problem (a typo must not be ignored)
    private static void Attributes(Node node, List<string> problems, params string[] names)
    {
        foreach (var name in node.Attributes.Keys.Where(name => !names.Contains(name)))
        {
            problems.Add($"{node.Location}'{node.Name}' has no attribute '{name}' (expected: {String.Join(", ", names)}).");
        }
    }

    private static string? Required(Node node, string name, List<string> problems)
    {
        var value = node.Attribute(name)?.Trim();
        if (String.IsNullOrEmpty(value))
        {
            problems.Add($"{node.Location}'{node.Name}' needs the attribute '{name}'.");
            return null;
        }
        return value;
    }

    // the retries of an 'on': a number, 1 or more; 0 without the attribute
    private static int Retry(Node node, List<string> problems)
    {
        if (node.Attribute("retry") is not { } text)
        {
            return 0;
        }
        if (Int32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }
        problems.Add($"{node.Location}'retry' must be a number of 1 or more, not '{text}'.");
        return 0;
    }

    private static int? Milliseconds(Node node, string name, List<string> problems)
    {
        if (node.Attribute(name) is not { } text)
        {
            return null;
        }
        if (Int32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        problems.Add($"{node.Location}'{name}' must be a number of milliseconds, not '{text}'.");
        return null;
    }

    /// <summary>
    /// An element of an exchange file, independent of its format: its name, its attributes (as text) and its children,
    /// and where it is in the file (a line in XML, a path in JSON) to report problems.
    /// </summary>
    private sealed record Node(string Name, IReadOnlyDictionary<string, string> Attributes, IReadOnlyList<Node> Children, string Location)
    {
        public string? Attribute(string name) => Attributes.GetValueOrDefault(name);
    }

    /// <summary>
    /// Turns XML elements into <see cref="Node"/>s: the attributes are the attributes (those in the XML Schema instance
    /// namespace, such as <c>xsi:noNamespaceSchemaLocation</c>, are left out), the children are the child elements.
    /// </summary>
    private static class XmlNodes
    {
        private static readonly XNamespace Instance = "http://www.w3.org/2001/XMLSchema-instance";

        public static Node From(XElement element)
            => new(element.Name.LocalName,
                element.Attributes()
                    .Where(attribute => !attribute.IsNamespaceDeclaration && attribute.Name.Namespace != Instance)
                    .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value),
                [.. element.Elements().Select(From)],
                element is IXmlLineInfo info && info.HasLineInfo() ? $"line {info.LineNumber}: " : String.Empty);
    }

    /// <summary>
    /// Turns JSON into <see cref="Node"/>s, as the XML would be: the root object is 'exchanges'; its 'exchanges' array
    /// holds the 'exchange' children, an exchange's 'turns' array the 'turn' children, and its 'before' and 'after'
    /// arrays (of turns) a 'before' and 'after' child with those turns. In a turn, 'send' is a message name, 'receive' an
    /// array of message names and 'on' an array of transitions. Other values are attributes (as text); other arrays and
    /// objects are children with their own name, which the checks report as not expected.
    /// The root's '$schema' is left out.
    /// </summary>
    private static class JsonNodes
    {
        public static Node From(JsonElement root, List<string> problems)
            => Object("exchanges", root, "$", problems, new Dictionary<string, string>
            {
                ["exchanges"] = "exchange",
            });

        // the arrays of an object and the name of their items, per element
        private static readonly Dictionary<string, Dictionary<string, string>> Arrays = new()
        {
            ["exchange"] = new() { ["turns"] = "turn" },
            ["turn"] = new() { ["receive"] = "receive", ["on"] = "on" },
        };

        private static Node Object(string name, JsonElement element, string path, List<string> problems, Dictionary<string, string>? arrays = null)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{path}: '{name}' must be an object, not {Kind(element)}.");
                return new Node(name, new Dictionary<string, string>(), [], $"{path}: ");
            }

            arrays ??= Arrays.GetValueOrDefault(name) ?? [];
            var attributes = new Dictionary<string, string>();
            var children = new List<Node>();
            foreach (var property in element.EnumerateObject())
            {
                var at = $"{path}.{property.Name}";
                if (path == "$" && property.Name == "$schema")
                {
                    continue;
                }
                if (arrays.TryGetValue(property.Name, out var itemName))
                {
                    children.AddRange(Items(itemName, property.Value, at, problems));
                }
                else if (name == "turn" && property.Name == "send")
                {
                    children.Add(MessageNode("send", property.Value, at, problems));
                }
                else if (name == "exchange" && property.Name is "before" or "after")
                {
                    // the turns once per call: a part with its turns, as the XML element holds them
                    children.Add(new Node(property.Name, new Dictionary<string, string>(), [.. Items("turn", property.Value, at, problems)], $"{at}: "));
                }
                else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    children.Add(new Node(property.Name, new Dictionary<string, string>(), [], $"{at}: "));
                }
                else
                {
                    attributes[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
                }
            }
            return new Node(name, attributes, children, $"{path}: ");
        }

        private static IEnumerable<Node> Items(string name, JsonElement array, string path, List<string> problems)
        {
            if (array.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"{path}: must be an array, not {Kind(array)}.");
                yield break;
            }
            var index = 0;
            foreach (var item in array.EnumerateArray())
            {
                var at = $"{path}[{index++}]";
                // a received message is a name; the other items are objects
                yield return name == "receive" ? MessageNode(name, item, at, problems) : Object(name, item, at, problems);
            }
        }

        // a message given by its name: a node with the attribute 'message', as in the XML
        private static Node MessageNode(string name, JsonElement element, string path, List<string> problems)
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                problems.Add($"{path}: '{name}' must be the name of a message (text), not {Kind(element)}.");
                return new Node(name, new Dictionary<string, string>(), [], $"{path}: ");
            }
            return new Node(name, new Dictionary<string, string> { ["message"] = element.GetString()! }, [], $"{path}: ");
        }

        private static string Kind(JsonElement element) => element.ValueKind.ToString().ToLowerInvariant();
    }
}

/// <summary>
/// An exchange file that is not valid, with all problems found.
/// </summary>
public sealed class ExchangeDefinitionException(string source, IReadOnlyList<string> problems)
    : Exception($"The exchange file '{source}' is not valid:{Environment.NewLine}{String.Join(Environment.NewLine, problems)}")
{
    /// <summary>Where the file was read from (a resource or a path).</summary>
    public string Location { get; } = source;

    public IReadOnlyList<string> Problems { get; } = problems;
}
