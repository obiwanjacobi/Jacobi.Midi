# MIDI

## Schema Editor

An application that lets you author Midi Schemas.

- Easily create new schema definitions with an interactive GUI.
- Load/Save schema files in Json, Xml and Yaml.
- Work on multiple (related) files at once (SchemaSet).
- Import/Export Midi SysEx messages for testing schema definitions.
- Live MIDI connection with device for testing and probing.
- Reverse Engineer existing Midi SysEx messages incrementally.
- Add descriptions and sample values per field for documentation and testing.
- Register processor assmeblies and base schema assemblies.

### New Schema from scratch

A rich UI that guides the user through the schema making process.
The editor presents the options applicable at that node, which prevents invalid constructions.
The nested/hierarchical structure of a schema is presented in such a way that it does not become cluttered or confusing for the user.

### Reverse Engineering

During reverse engineering of an existing Midi SysEx message, the bytes are presented as like in a binary editor.
The F0/F7 are recognized automatically and presented with the correct structure/processor.
The user can highlight one or more bytes and isolate them by giving it a name and potentially a datatype.
Multiple Midi messages may be collected and compared in order to detect differences between them and learning what bytes represent a certain device characteristic/setting.

#### Schema editor MVP

The Avalonia desktop and WebAssembly applications share the same schema editor:

- Create schema documents, add nested groups and fields, and edit the hierarchy.
- Open and save JSON, XML, YAML, and YML schema files.
- Edit data types, descriptions, and sample values for individual nodes.
- Import SysEx bytes as hexadecimal, recognize F0/F7 boundaries, compare messages, and add selected bytes to a schema field.

Each workspace document owns a mutable `EditDocument` from the serializer's schema editing model.
Commands edit this draft directly; tree items reference its nodes rather than reparsing source text.
`SchemaFileService` only creates drafts and handles format detection, loading and serialization.
Saving builds an independent `SchemaDocument` snapshot and uses the library's shared JSON, XML or
YAML writer. Definitions, references and other features not exposed by the current UI are preserved.
For loaded XML, the editor retains source presentation metadata and merges snapshot differences into
it: comments, the declaration, section ordering, compact properties/processors and line endings survive
ordinary edits. No-op saves return the original text; edited saves may normalize lexical details such
as attribute quotes or spacing before a closing bracket. Comments are not yet retained for JSON/YAML.
Loading does not compile schemas or require their included documents to be open.
XML `nodeDefs` now contains typed `field`, `group`, `packed`, `repeat` or `choice` elements,
not the retired `nodeDef kind="..."` form. The Roland/GP-8 schemas use the new contract.

Run the desktop editor with `dotnet run --project src/Jacobi.Midi.SchemaEditor/Jacobi.Midi.SchemaEditor.Desktop/Jacobi.Midi.SchemaEditor.Desktop.csproj`.
Run the browser editor locally with `dotnet run --project src/Jacobi.Midi.SchemaEditor/Jacobi.Midi.SchemaEditor.Browser/Jacobi.Midi.SchemaEditor.Browser.csproj`.
Both targets currently use the repository's .NET 11 target framework. Live MIDI connections and device/processor assembly registration are not part of this MVP.

## Midi Device Console

An application that provides central control over multiple Midi Devices.
Communication is either based on Midi v1 (with schemas) or Midi v2 capabilities inquiry.

### Patch Editor / Librariran

- Allow users to change patch settings remotely on their Midi devices.
- Provide categorization, tagging and naming individual patches in an off-device database.
- Provide a way to share patch libraries per Midi device.

## Remote Midi Access

A (server+client) tool to let an authenticated user (or application) interact with a Midi device over a remote internet connection for the purpose of creating/reverse engineering the Midi device schema.

- The server tool runs locally on the computer that has the Midi device in question connected.
- The user of the computer determines who gets access.
- The server tool transports the midi messages (two-way) over the internet to/from the client-tool.
- The server tool must be reachable from the internet (firewall/modem settings).
- The client tool starts the (outgoing) connection to the remote server tool.
- The server and client tools are cross-platform (win/mac/lin)
- The server tool should be a single package that simply works and contains code for Midi, Network and Security (certificate?)

## MIDI v2 Support

We will need a cross-platform (win,mac,lin) way to open/close MIDI ports and receive/send MIDI messages.
Now MIDI v2 support is being rolled out, it stands to reason to go with that, also because MIDI v2 has the features we are building for MIDI v1, such as Capability Inquiry (CI).

`Jacobi.Midi.Core` defines platform-neutral sessions, endpoint discovery, and packet I/O.
`Jacobi.Midi.Windows` implements these contracts using Windows MIDI Services SDK preview 11.
The Windows project targets .NET 11 and Windows 10.0.26100.0 and uses the SDK sample's
WinRT projection configuration, without a fixed runtime architecture or manual native-file copying.
The MIDI SDK package is private to the implementation so consuming applications use its compiled
projection rather than receiving a direct WinMD reference. Windows MIDI Services and the runtime
needed to activate the SDK must be available on the target machine. Consumer activation was verified
against the local installation; standalone deployment to a clean machine has not been verified.
Legacy/hybrid SDK fallback modes are deliberately rejected because they do not provide
this implementation's full discovery and scheduling contract.

```csharp
IMidiApi api = new Jacobi.Midi.Windows.WindowsMidiApi();
if (!await api.EnsureIsAvailable())
    throw new MidiException("Windows MIDI Services is unavailable.");

await using var observer = await api.WatchMidiEndpoints(
    change => Console.WriteLine($"{change.ChangeType}: {change.Endpoint.Name}"), ct);
await using var session = await api.CreateSession(
    new MidiSessionOptions { Name = "My MIDI application" }, ct);

// Select an endpoint from observer.Endpoints, then:
await using var connection = await session.Connect(endpointId,
    new MidiConnectionOptions { Directions = MidiDirections.Duplex },
    packets => { /* Process or copy the received StampedPacket values here. */ }, ct);
```

### Windows backend contracts

- Discovery includes standard UMP and legacy MIDI 1 endpoints. Diagnostic endpoints are not
  listed, but their IDs can be opened explicitly for loopback testing. IDs are native Windows
  endpoint interface IDs, compared case-insensitively by this backend. They do not identify
  an instrument attached behind DIN ports.
- Watching returns after initial OS enumeration; initial endpoints also generate `Added`
  notifications. Notifications are serialized on a background worker and can begin before
  watching returns. Each inventory access returns a stable snapshot. The native watcher's
  inventory is authoritative; Refresh rebuilds the Core snapshot from that inventory without
  a separate OS enumeration or MIDI inquiries. Native callbacks only signal reconciliation;
  rapid changes may coalesce, so notifications describe snapshot differences rather than an
  exhaustive history of every transient endpoint/property change. Watcher creation and mapping
  run on background workers. OS enumeration completion does not imply that endpoint protocol
  discovery has finished.
- Await observer `WaitForCompletion(ct)` for shutdown or asynchronous watcher/callback failures.
  Cancellation cancels only the wait, not observation.
  Disposal stops observation and waits for in-progress application callbacks to finish.
- Capabilities are immutable snapshots. Unknown protocol support/directions remain unknown,
  rather than being reported as unsupported. Function-block directions are converted to the
  application's perspective; group terminal blocks provide a direction fallback.
  Connection `EndpointInfo` is the snapshot captured when opening; discovery reports later updates.
- Connections default to duplex; receive-only and send-only are supported. A receive handler
  is required when receiving. Known unsupported directions are rejected. Unknown direction
  metadata cannot verify a return path or DIN cabling. Windows native connections remain
  duplex internally; the wrapper enforces the requested directions. Normally open only one
  connection per endpoint in a session: each native connection owns buffers and a processing
  thread. Connections are not automatically shared because handlers and ownership differ.
- Receive callbacks begin only after native opening succeeds, though they may precede the
  return from Connect. Packets arriving during opening are buffered; failed opening discards them.
  Receive callbacks are serialized per connection on a background worker, with batches up to
  64 packets. Copy values that must outlive a callback. Do not use `async void` callbacks or
  synchronously wait for connection/session/observer disposal inside their own callbacks.
- Endpoint refresh signals coalesce into one pending reconciliation. Receive packets have a
  bounded buffer (4096 entries); overflow faults the connection explicitly. Receive-handler exceptions and device
  removal fault the connection, populate `Error`, and raise `ConnectionStateChanged`.
  Faulted connections remain session-owned until disposed; there is no automatic reconnection.
  State callbacks are serialized in transition order on a separate background worker, without
  holding connection locks. Disposal waits for these callbacks too: do not synchronously wait
  for connection/session disposal from a state callback. State may advance before notification.
  Callback failures are also written to `System.Diagnostics.Trace`.
- Timestamps use the native monotonic Windows MIDI clock. Use `TimestampFrequency` for
  conversion to seconds; zero means send immediately. `SendScheduled` uses the native service
  scheduler and requires nondecreasing timestamps within a batch. Host scheduling does not
  guarantee the instrument's acoustic playback time.
- Sending validates and copies the whole batch before submission. Do not mutate packet storage
  during submission. Adjacent packets with the same timestamp are batched within the native
  connection's queried word limit, splitting only between complete UMPs. Submission stops at the
  first failed transmission. Send completion means native submission acceptance; service receipt
  confirmation is disabled, so it does not confirm service receipt or device acknowledgement.
  Cancellation/failure can leave a partially accepted batch and does not retract queued packets.
  Disposal does not promise to retract already accepted scheduled messages.
- Disposal is idempotent. Disposing a connection removes it from the session; session disposal
  closes every owned connection, including faulted connections. Opening cancellation cleans up
  partially opened resources and does not control the returned connection's lifetime.

## Resources

Windows:
https://microsoft.github.io/MIDI/


Cross platform UI for tools:
- avalonia >= 11.3: works on Win/Mac/Lin but not native rendered.
- Uno Platform 6: All the same Xaml but not native.
- MAUI 11: native but Linux support is unofficial/community solutions (GTK4)

X-plat MIDI v1:
https://github.com/atsushieno/managed-midi / https://github.com/managed-midi/managed-midi
MIDI v2: https://github.com/atsushieno/cmidi2
https://github.com/celtera/libremidi

Windows Specific MIDI v2: https://microsoft.github.io/MIDI/
