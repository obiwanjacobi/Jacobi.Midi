using Jacobi.Midi.VirtualDevice;
using Windows.Devices.Midi2;
using Windows.Devices.Midi2.CapabilityInquiry;
using Windows.Devices.Midi2.Enumeration;
using Windows.Devices.Midi2.Transports.Virtual;

// A virtual MIDI 2.0 device that answers MIDI-CI Property Exchange, to test the Device API against: the standard
// resources (DeviceInfo, ChannelList, ProgramList) and manufacturer specific resources that can be set whole or
// partially (see CustomResource). The platform's responder answers Discovery, Get and Subscribe; a Set is answered
// here (PropertySetResponder). Built on the capability-inquiry-virtual-device sample of Windows MIDI Services.

const string DeviceName = "Jacobi Test Device";
const string ProductInstanceId = "JACOBI0001";

if (!MidiApi.EnsureServiceAvailable())
{
    Console.WriteLine("Could not start the MIDI service (Windows MIDI Services).");
    return 1;
}

// creating a device that already exists waits forever: another run of this tool, or one the service still holds
var existing = MidiEndpointDeviceInformation.FindAll(MidiEndpointDeviceInformationSortOrder.Name,
        MidiEndpointDeviceInformationFilters.AllStandardEndpoints | MidiEndpointDeviceInformationFilters.VirtualDeviceResponder)
    .FirstOrDefault(endpoint => endpoint.Name == DeviceName || endpoint.GetDeclaredEndpointInfo().ProductInstanceId == ProductInstanceId);
if (existing is not null)
{
    Console.WriteLine($"The virtual device '{DeviceName}' already exists ({existing.EndpointDeviceId}).");
    Console.WriteLine("Stop the other run of this tool (Enter or Ctrl+C in its terminal). Without one, the MIDI service still holds it:");
    Console.WriteLine("restart the service as administrator (Stop-Process -Name MidiSrv -Force; Start-Service midisrv).");
    return 1;
}

var endpointInfo = new MidiDeclaredEndpointInfo
{
    Name = DeviceName,
    ProductInstanceId = ProductInstanceId,
    SpecificationVersionMajor = 1,
    SpecificationVersionMinor = 1,
    SupportsMidi10Protocol = true,
    SupportsMidi20Protocol = true,
    HasStaticFunctionBlocks = true,
};
var identity = new MidiDeclaredDeviceIdentity(
    0x7D, 0x00, 0x00,       // manufacturer: 7D, for non-commercial use
    0x01, 0x00,             // family
    0x01, 0x00,             // model
    0x01, 0x00, 0x00, 0x00); // software revision

var config = new MidiVirtualDeviceCreationConfig(DeviceName, "Answers MIDI-CI Property Exchange, including Set",
    "Jacobi.Midi", endpointInfo, identity, new MidiEndpointUserSuppliedInfo());

// creating the device can still hang when the service is in a bad state: say what it waits for
Console.WriteLine($"Creating the virtual device '{DeviceName}'...");
var session = MidiSession.Create("Jacobi.Midi Virtual Device");
var device = MidiVirtualDeviceManager.CreateVirtualDevice(config);
if (device is null)
{
    Console.WriteLine("Could not create the virtual device.");
    session.Dispose();
    return 1;
}
var connection = session.CreateEndpointConnection(device.DeviceEndpointDeviceId);
if (connection is null || connection.AddMessageProcessingPlugin(device) != MidiMessageProcessingPluginAddResult.Succeeded)
{
    Console.WriteLine("Could not connect to the virtual device.");
    session.Dispose();
    return 1;
}

// the device is removed however the run ends (Enter, Ctrl+C, closing the terminal), once; a hard kill (e.g. the
// debugger's Stop) cannot be handled and may leave it in the service
var stopping = 0;
void Stop()
{
    if (Interlocked.Exchange(ref stopping, 1) != 0)
        return;
    connection.RemoveMessageProcessingPlugin(device.PluginId);
    session.DisconnectEndpointConnection(connection.ConnectionId);
    session.Dispose();
    Console.WriteLine("Virtual device stopped.");
}
var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    stop.Set();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();

var responder = device.CapabilityInquiry;
responder.DeviceInfo = new MidiDeviceInfo(identity, "Jacobi", "Jacobi.Midi", DeviceName) { Version = "1.0" };
responder.ChannelList = Standard.ChannelList();
responder.SetProgramList("main", Standard.ProgramList());

var sets = new PropertySetResponder(responder, connection);
var resources = new MidiResourceList();
foreach (var entry in Standard.ResourceListEntries())
{
    resources.Entries.Add(entry);
}
foreach (var resource in CustomResource.All)
{
    sets.Publish(resource);
    resources.Entries.Add(new MidiResourceListEntry(resource.Name)
    {
        CanSet = resource.CanSet,
        RequireResourceId = resource.RequireResId,
        Schema = Windows.Data.Json.JsonObject.Parse(resource.Schema),
    });
}
responder.ResourceList = resources;
responder.SetResourceSubscribable("ChannelList", true);
responder.SetResourceSubscribable("X-ProgramEdit", true);

responder.MessageReceived += (_, args) =>
{
    if (!sets.Handle(args.Message, args.Group))
    {
        Console.WriteLine($"Not answered: capability inquiry message 0x{(int)args.Message.MessageType:X2}.");
    }
};
responder.IsEnabled = true;

if (!connection.Open())
{
    Console.WriteLine("Could not open the connection of the virtual device.");
    Stop();
    return 1;
}

Console.WriteLine($"Virtual device '{DeviceName}' is running (MUID {responder.GetMuid(0)}).");
Console.WriteLine("Resources: DeviceInfo, ChannelList, ProgramList (main), " + String.Join(", ", CustomResource.All.Select(resource => $"{resource.Name} (canSet {resource.CanSet})")));
Console.WriteLine("Press Enter or Ctrl+C to stop.");
// Enter, or the end of the input when it is redirected
_ = Task.Run(() =>
{
    Console.ReadLine();
    stop.Set();
});
stop.Wait();
Stop();
return 0;
