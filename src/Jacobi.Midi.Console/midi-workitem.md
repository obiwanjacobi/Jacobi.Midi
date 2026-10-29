# Connect Console to MIDI

I've read Midi.Device's exchange (its readme, todo list and MessageExchange itself), the Core MIDI API, and the console readme's synchronisation design. Short answer: the exchange covers the basic flow the console needs (receive a range, listen for changes, send a range, a result per block). Four gaps in Midi.Device would hurt a dashboard in practice; the rest is console work. I've changed no code.

**What Midi.Device already provides**

- Opening an exchange per device: MessageExchangeFactory.Create(endpointId, deviceValues, options) opens its own connection to the endpoint. The UMP group comes from MessageExchangeOptions.Group.
- Receive, Send and Listen, each on a PlanRange. Listen hands data the device sends on its own to a value sink, and DeviceState.CreateSink(ValueOrigin.Device) fits that directly.
- A result per block: completed, timed out, rejected or failed, with optional progress reporting. Each block carries the plan nodes it covered (AddressBlock.Nodes), so a failure can be shown on the controls it affects.
- One transfer at a time per device, and data from a running transfer doesn't also go to a listener.

**Missing in Midi.Device**

- **Done:** a data message now carries any block of whole fields (one parameter, part of a group, neighbouring groups), sent and received; the open choice of Jacobi.BinarySerializer targets a range (`FromTarget` to `ToTarget`). What follows is the original analysis.
  Sending one parameter of a non-atomic group throws NotSupportedException (first item of the Device todo). A data message can only carry one whole group occurrence. The GP-8 isn't affected, because its patch is atomic. The GX-700's individual temporary area is, and with it any device that addresses parameters one by one. This is the main blocker, and the fix starts in Jacobi.BinarySerializer (an open choice that targets a range).

- **Done:** a `DeviceSession` (one per connection) keeps the state of the device; Send completes atomic groups from it and only reads what it does not hold (`SessionData.Device` always reads). The session also reports a lost connection (`IsConnected`, `Disconnected`; the exchange has `ConnectionState`/`ConnectionStateChanged`), which covers the connection state item below. What follows is the original analysis.
  Every send to an atomic group first reads that whole group from the device. When a range only partly covers an atomic group, Send receives the group, copies your values over it, and sends it back. For one knob step on the GP-8 that's a request, a reply, then the data, every time. The console already has those values (DeviceState.Source). What's missing is a way for Send to take the rest of the group from the source and only read what the source doesn't know.

- The pause between messages ignores the transmission time on a DIN cable (second Device todo item). Dragging a knob streams messages, so on a slow (DIN) port the device may not get the pause it needs.

- No connection state on the exchange. MessageExchange hides its IMidiConnection, so nobody hears when the cable is pulled or the connection faults. The console can't show a device as disconnected or offer a reconnect. It needs a state or event on the exchange (the connection already has ConnectionStateChanged).

- No way to ask up front whether a device can be exchanged at all. Today you only find out at the first transfer, from three different exceptions:
  - an unknown midi.exchange protocol;
  - no request message for the range;
  - a root that isn't an address map.

  The console readme wants to mark such a device in the rig and use it offline, or write-only. That needs a query on the factory, something like "can this root, with these device values, be received / sent / listened to, and if not, why". Without it the console would have to guess from exception types.

- More exchange patterns and a "user in the loop" hook (the rest of the Device todo). These are not needed for the GP-8 or GX-700 (Roland one-way works), so they don't block a first connection.

Core needs nothing new. IMidiApi.WatchMidiEndpoints lists endpoints by name, and the rig stores the endpoint's name, so finding the endpoint id is console work.

## What the console still has to build (not Midi.Device's job)

- A MIDI session and the endpoint list. The desktop host already hands the console an IMidiApi, but nothing uses it. The Schema Editor's connection service holds a single connection, while exchanges open their own, so it doesn't fit as is. The "shared UI library" milestone could still share the MIDI bar.
- Picking an endpoint for a device: a list of available endpoints and a group in the device panel (today it's a free-text name), and a connected/disconnected mark in the rig tree.
- The synchronisation per device:
  - Create the exchange.
  - Work out the ranges that cover the bound parameters, merged so that each atomic group is requested once.
  - Receive them once, then listen.
  - Queue changes the user makes, keeping only the latest value per parameter, and send them.
  - When a variable like $patch changes, receive and listen to the new item.
  - Report failures in the notifications and on the controls.
  - Offer a manual "sync now" (the ⟳ in the readme's mock-up).
- Lifetime: rebuild the exchanges when the schema library is rebuilt or a device changes.
- Tests: the exchange's own test port is internal, but IMidiSession and IMidiConnection are public. A fake session that plays the device works for console tests.

**Suggested order**

- Midi.Device first:
  - items 2 and 4: send without the read, and the connection state;
  - item 5, the capability query, so the console doesn't depend on exception types.
These are small.
- Console: session and endpoints, the device's endpoint picker, then synchronisation against the GP-8 (atomic patches, so it works without item 1).
- Then item 1 (sending part of a group). It starts in Jacobi.BinarySerializer and is what devices like the GX-700 need.
