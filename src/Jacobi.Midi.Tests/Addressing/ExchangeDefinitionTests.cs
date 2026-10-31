using System.Reflection;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.Tests.Schema;
using Xunit;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// Reading exchange files (<see cref="ExchangeDefinitionReader"/>, XML and JSON) and finding them
/// (<see cref="ExchangeDefinitions"/>): the format, its defaults, the problems found without the schemas, and the
/// XML and JSON schemas of the format (exchange.xsd, exchange.schema.json).
/// </summary>
public class ExchangeDefinitionTests
{
    // the TestDevice exchange file (TestDevice.ExchangeXml) in JSON
    private const string ExchangeJson = """
        {
          "$schema": "exchange.schema.json",
          "for": "TestDevice.AddressMap",
          "responseTimeout": 1000,
          "messageInterval": 20,
          "exchanges": [
            {
              "name": "requestData",
              "states": [
                {
                  "name": "request",
                  "send": "TestEnvelope.Message.Request",
                  "receive": [ "TestEnvelope.Message.Data" ],
                  "on": [
                    { "event": "complete", "result": "completed" },
                    { "event": "timeout", "result": "timedOut" }
                  ]
                }
              ]
            },
            {
              "name": "sendData",
              "states": [
                {
                  "name": "write",
                  "send": "TestEnvelope.Message.Data",
                  "on": [ { "event": "sent", "result": "completed" } ]
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Read_TheTestDeviceFile()
    {
        var set = ExchangeDefinitionReader.ReadXml(TestDevice.ExchangeXml, "test");

        Assert.Equal(TestDevice.MapName, set.For);
        Assert.Equal([ExchangeNames.RequestData, ExchangeNames.SendData], set.Exchanges.Select(exchange => exchange.Name));
        var request = set.Find("REQUESTDATA")!;
        Assert.Equal(TimeSpan.FromSeconds(1), request.ResponseTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(20), request.MessageInterval);
        var state = Assert.Single(request.States);
        Assert.Equal(new SchemaName("TestEnvelope.Message.Request"), state.Send);
        Assert.Equal([new SchemaName("TestEnvelope.Message.Data")], state.Receive);
        Assert.Equal([ExchangeEvent.Complete, ExchangeEvent.Timeout], state.Transitions.Select(transition => transition.Event));
        Assert.Equal(BlockStatus.TimedOut, state.Transitions[1].Result);
        Assert.Equal(["TestEnvelope.Message.Request", "TestEnvelope.Message.Data"], request.Messages.Select(message => message.FullName));
    }

    [Fact]
    public void Read_Defaults_AndAnExchangeOverridesTheFile()
    {
        var set = ExchangeDefinitionReader.ReadXml("""
            <exchanges for="Device.Map">
              <exchange name="slow" responseTimeout="5000" messageInterval="40">
                <state name="request">
                  <send message="Envelope.Message.Request" />
                  <on message="Envelope.Message.Ack" goto="request" />
                </state>
              </exchange>
              <exchange name="default">
                <state name="request">
                  <send message="Envelope.Message.Request" />
                  <on event="sent" result="completed" />
                </state>
              </exchange>
            </exchanges>
            """, "test");

        Assert.Equal(TimeSpan.FromSeconds(5), set.Find("slow")!.ResponseTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(40), set.Find("slow")!.MessageInterval);
        Assert.Equal(TimeSpan.FromSeconds(1), set.Find("default")!.ResponseTimeout);
        Assert.Equal(TimeSpan.Zero, set.Find("default")!.MessageInterval);
        var transition = Assert.Single(set.Find("slow")!.States[0].Transitions);
        Assert.Equal(ExchangeEvent.Message, transition.Event);
        Assert.Equal(new SchemaName("Envelope.Message.Ack"), transition.Message);
        Assert.Equal("request", transition.Goto);
    }

    [Fact]
    public void Read_AnInvalidFile_ReportsAllProblems()
    {
        var exception = Assert.Throws<ExchangeDefinitionException>(() => ExchangeDefinitionReader.ReadXml("""
            <exchanges for="Device.Map" timeout="10">
              <exchange name="broken">
                <state name="request">
                  <on event="sent" result="completed" />
                  <on event="complete" goto="nowhere" />
                  <on event="later" message="Envelope.Message.Ack" result="done" />
                  <wait />
                </state>
                <state name="lonely">
                  <send message="Envelope.Message.Request" />
                  <on event="sent" result="completed" />
                </state>
              </exchange>
            </exchanges>
            """, "broken.xml"));

        Assert.Equal("broken.xml", exception.Location);
        string[] expected =
        [
            "has no attribute 'timeout'",
            "'wait' is not expected",
            "either an 'event' or a 'message'",
            "the event must be sent, complete or timeout",
            "the result must be completed, rejected, failed or timedOut",
        ];
        Assert.All(expected, text => Assert.Contains(exception.Problems, problem => problem.Contains(text, StringComparison.Ordinal)));
    }

    [Fact]
    public void Read_TheStatesAreChecked()
    {
        var exception = Assert.Throws<ExchangeDefinitionException>(() => ExchangeDefinitionReader.ReadXml("""
            <exchanges for="Device.Map">
              <exchange name="states">
                <state name="request">
                  <on event="sent" result="completed" />
                  <on event="complete" goto="nowhere" />
                </state>
                <state name="lonely">
                  <send message="Envelope.Message.Request" />
                  <on event="sent" result="completed" />
                </state>
                <state name="lonely">
                  <on event="timeout" result="failed" />
                </state>
              </exchange>
            </exchanges>
            """, "states.xml"));

        string[] expected =
        [
            "'sent' needs a 'send'",
            "'complete' needs a 'receive'",
            "there is no state 'nowhere'",
            "state 'lonely' is never reached",
            "the state 'lonely' is defined more than once",
        ];
        Assert.All(expected, text => Assert.Contains(exception.Problems, problem => problem.Contains(text, StringComparison.Ordinal)));
    }

    [Fact]
    public void ReadJson_TheSameAsTheXml()
    {
        var xml = ExchangeDefinitionReader.ReadXml(TestDevice.ExchangeXml, "test.exchange.xml");
        var json = ExchangeDefinitionReader.ReadJson(ExchangeJson, "test.exchange.json");

        Assert.Equal(xml.For, json.For);
        Assert.Equal(xml.Exchanges.Count, json.Exchanges.Count);
        foreach (var (fromXml, fromJson) in xml.Exchanges.Zip(json.Exchanges))
        {
            Assert.Equal(fromXml.Name, fromJson.Name);
            Assert.Equal(fromXml.ResponseTimeout, fromJson.ResponseTimeout);
            Assert.Equal(fromXml.MessageInterval, fromJson.MessageInterval);
            Assert.Equal(fromXml.States.Count, fromJson.States.Count);
            foreach (var (stateXml, stateJson) in fromXml.States.Zip(fromJson.States))
            {
                Assert.Equal(stateXml.Name, stateJson.Name);
                Assert.Equal(stateXml.Send, stateJson.Send);
                Assert.Equal(stateXml.Receive, stateJson.Receive);
                Assert.Equal(stateXml.Transitions, stateJson.Transitions);
            }
        }
    }

    [Fact]
    public void ReadJson_ProblemsAreLocatedByTheirPath()
    {
        var exception = Assert.Throws<ExchangeDefinitionException>(() => ExchangeDefinitionReader.ReadJson("""
            {
              "for": "Device.Map",
              "exchanges": [
                {
                  "name": "broken",
                  "timeout": 10,
                  "states": [
                    { "name": "request", "send": 5, "receive": "Envelope.Message.Data", "on": [ { "event": "later", "goto": "request" } ] }
                  ]
                }
              ]
            }
            """, "broken.exchange.json"));

        string[] expected =
        [
            "$.exchanges[0]: 'exchange' has no attribute 'timeout'",
            "$.exchanges[0].states[0].send: 'send' must be the name of a message (text), not number.",
            "$.exchanges[0].states[0].receive: must be an array, not string.",
            "$.exchanges[0].states[0].on[0]: the event must be sent, complete or timeout, not 'later'.",
        ];
        Assert.All(expected, text => Assert.Contains(exception.Problems, problem => problem.StartsWith(text, StringComparison.Ordinal)));
    }

    [Fact]
    public void Read_FromAStream_TheExtensionTellsTheFormat()
    {
        using var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ExchangeJson));
        using var xml = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(TestDevice.ExchangeXml));

        Assert.Equal(TestDevice.MapName, ExchangeDefinitionReader.Read(json, "Device.exchange.json").For);
        Assert.Equal(TestDevice.MapName, ExchangeDefinitionReader.Read(xml, "Device.exchange.xml").For);
    }

    [Fact]
    public void Xsd_TheExchangeFilesAreValid()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, TestHelpers.PathOf("exchange.xsd"));
        var roland = Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland"));
        var files = roland.GetManifestResourceNames().Where(ExchangeDefinitions.IsDefinitionResource)
            .Select(resource => new StreamReader(roland.GetManifestResourceStream(resource)!).ReadToEnd())
            .Append(TestDevice.ExchangeXml)
            .ToList();

        Assert.True(files.Count >= 3);
        foreach (var file in files)
        {
            var errors = new List<string>();
            XDocument.Parse(file).Validate(schemas, (_, args) => errors.Add(args.Message));
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Xsd_AnInvalidFileIsReported()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, TestHelpers.PathOf("exchange.xsd"));
        var errors = new List<string>();

        XDocument.Parse("""
            <exchanges for="Device">
              <exchange name="broken">
                <state name="request">
                  <send message="Message" />
                  <on event="later" result="done" />
                </state>
              </exchange>
            </exchanges>
            """).Validate(schemas, (_, args) => errors.Add(args.Message));

        // 'for' and 'message' are no schema names, the event and result are unknown
        Assert.Equal(4, errors.Count);
    }

    [Fact]
    public void JsonSchema_DescribesTheFormat()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(TestHelpers.PathOf("exchange.schema.json")));
        var root = schema.RootElement;
        var definitions = root.GetProperty("$defs");

        // the properties the JSON reader knows, and no others
        Assert.Equal(["$schema", "for", "responseTimeout", "messageInterval", "exchanges"], Names(root.GetProperty("properties")));
        Assert.Equal(["name", "responseTimeout", "messageInterval", "states"], Names(definitions.GetProperty("exchange").GetProperty("properties")));
        Assert.Equal(["name", "send", "receive", "on"], Names(definitions.GetProperty("state").GetProperty("properties")));
        Assert.Equal(["event", "message", "goto", "result"], Names(definitions.GetProperty("transition").GetProperty("properties")));

        static string[] Names(JsonElement properties) => [.. properties.EnumerateObject().Select(property => property.Name)];
    }

    [Theory]
    [InlineData("Jacobi.Midi.Roland.Exchange.Gx700.exchange.xml", true)]
    [InlineData("Jacobi.Midi.Roland.Exchange.Gx700.exchange.json", true)]
    [InlineData("Jacobi.Midi.Roland.Schemas.Gx700.schema.xml", false)]
    [InlineData("Jacobi.Midi.Roland.Exchange.Gx700.xml", false)]
    public void IsDefinitionResource(string resource, bool expected)
        => Assert.Equal(expected, ExchangeDefinitions.IsDefinitionResource(resource));

    [Fact]
    public void AddAssembly_TheRolandFiles()
    {
        var exchanges = new ExchangeDefinitions();

        Assert.Empty(exchanges.AddAssembly(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland"))));

        Assert.Equal(TimeSpan.FromMilliseconds(20), exchanges.Find(new SchemaName("Gp8.AddressMap"), ExchangeNames.SendData)!.MessageInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(60), exchanges.Find(new SchemaName("Gx700.AddressMap"), ExchangeNames.RequestData)!.MessageInterval);
        Assert.Null(exchanges.Find(new SchemaName("Gx700.AddressMap"), "storeData"));
    }

    [Fact]
    public void Add_ALaterFileForTheSameRootReplacesTheEarlier()
    {
        var exchanges = new ExchangeDefinitions();
        var first = ExchangeDefinitionReader.ReadXml(TestDevice.ExchangeXml, "first");
        var second = ExchangeDefinitionReader.ReadXml(TestDevice.ExchangeXml, "second");

        Assert.Null(exchanges.Add(first));
        Assert.Same(first, exchanges.Add(second));
        Assert.Equal("second", exchanges.Find(TestDevice.MapName)!.Source);
    }
}
