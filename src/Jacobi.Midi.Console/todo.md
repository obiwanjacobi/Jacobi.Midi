# Console TODOs

What is planned for the console, and what was done. What the console does now is in the [readme](readme.md).

## Open

- [ ] Slider/Knob value change-drag jumps values when pressing/releasing shift (for fine/coarse changes). Seems like both modes work with their own values.

### Edit mode

- [ ] **A dashboard alias that is not in the rig.** Opening a dashboard whose alias is not in the rig asks the user to pick a rig device with the same map root (today it is reported).
- [ ] Selecting several controls.
- [ ] Copy and paste.
- [ ] **Patch names.** Show the number of an index variable as a device's own patch name (U01-1, A-11, ...): a display setting still to be designed.
- [ ] **Units from the schema.** Should units turn out to be the same for every dashboard of a device, a schema property can be added as the default; the dashboard setting stays the override.
- [ ] Save the chosen look (it is chosen in the toolbar and not saved).

### Synchronisation with the device

- [ ] **Synchronisation per device**, on the `DeviceSession` of each device (`RigConnections`). The console uses the [message exchange](../Jacobi.Midi.Device/Exchange/readme.md) of the Device project; it never builds SysEx messages itself.
  1. **Sync.** The ranges (`PlanRange`) that cover the bound parameters are received (`Receive`) into the device state, merged so that each atomic group is requested once. The exchange widens a range to the atomic groups it overlaps, so a sync usually delivers more values than are bound; the device state keeps them all.
  2. **Follow.** The exchange listens (`Listen`) on the same ranges, so changes made on the device show up on the dashboard.
  3. **Change.** A control writes the device state, which queues a `Send` of the parameter's range. While a knob is dragged, only the latest value per parameter is kept in the queue, so the device gets as many messages as it can take (paced by `midi.messageInterval`), not one per mouse move.
  4. **Select.** When the user changes an index variable, the bindings are resolved again; the controls that use it get the new instance, and its ranges are received (step 1) and followed (step 2) instead of those of the previous one.
  5. **Failure.** A block that times out or is rejected (`BlockStatus`) is shown on the controls of its parameters and in the status bar; nothing fails silently. Each block carries the plan nodes it covered (`AddressBlock.Nodes`), so a failure can be shown on the controls it affects.
  6. **Sync now.** A manual "sync now" (⟳) in the MIDI bar.

  Each device has its own exchange: a control whose slots are bound to different devices sends to each device separately. There is no transaction across devices. The sessions are made again when the schema library is rebuilt or a device changes (`RigConnections.Update` does this already). Tests use the fake MIDI backend of the console tests (`FakeMidi`).
- [ ] **Ask up front whether a device can be exchanged** (Midi.Device). Today you only find out at the first transfer, from three different exceptions: an unknown `midi.exchange` protocol; no request message for the range; a root that is not an address map. The console wants to mark such a device in the rig and use it offline, or write-only. That needs a query on the factory, something like "can this root, with these device values, be received / sent / listened to, and if not, why". Without it the console would have to guess from exception types.
- [ ] **Detect the receive group** in the device panel (see the [Device todo](../Jacobi.Midi.Device/todo.md): detect the receive group of a device).
- [ ] **The message interval as the pause on the wire** (see [Device todo](../Jacobi.Midi.Device/todo.md)). Dragging a knob streams messages; over a DIN connection the transmission time must be part of the pause.
- [ ] **Exchange patterns the console does not support.** Some devices need a message exchange pattern that the exchange does not have (see the [Device todo](../Jacobi.Midi.Device/todo.md): the Roland handshake, Yamaha Bulk Header/Footer, write/store requests, device modes, a manual operation on the device), and some need one that is specific to that device and of no use to any other. The console must not assume that every device in a rig can be synchronised:
  - **Detect it, show it.** When the exchange cannot handle a device (an unknown `midi.exchange`, a message it cannot use), the device is marked in the rig and on its controls with the reason. Its dashboards still open and work offline; nothing fails silently.
  - **Partial use.** A device that can be written but not read (no usable request) is used write-only: its parameters stay *unknown* until set, as for the parameters that cannot be read back (see *Parameter sources and targets* below).
  - **Generic first.** A pattern that more devices use becomes a built-in protocol (`ExchangeProtocols`) or, later, a configurable state machine (an `ITransferMachine`) described in the schema.
  - **Device specific as a plug-in.** A pattern that only one device uses comes with that device's assembly, the way processors do: a factory per assembly with its own namespace prefix, selected by name in the schema (e.g. `midi.exchange="acme.handshake"`). The console registers device assemblies already (for their processors and schemas), so it finds these protocols the same way, and the machines stay inside the existing `IExchangeProtocol` model.
  - **The user in the loop.** Patterns that need the user ("press the bulk dump key", "switch the device to edit mode") need a prompt from the console: the exchange asks, the console shows the request with a *Cancel*, the exchange waits. The console supplies this prompt for every device; the request text comes from the schema or the protocol.
- [ ] **Devices that only do complete dumps without an address map.** A job for the message exchange of Midi.Device (see its todo: message exchange for schemas without an address map).

### Parameter sources and targets

- [ ] **Parameters that are not in an address map.** Only parameters in an address map are supported (SysEx data messages through the message exchange). Devices use other ways too, and the console is designed so that they can be added without changing the dashboards, the controls or the device state.

  The ways a device exposes a parameter:

  | Kind | Identification | Read back | Notes |
  |------|----------------|-----------|-------|
  | Address map (SysEx) | address, via the schema path | yes: request (RQ1, Dump Request) | value range in the schema; atomic groups; supported |
  | Control Change | channel + controller number | no; only what the device sends | 7-bit; 14-bit as an MSB/LSB pair (CC 0-31 + 32-63) |
  | RPN / NRPN | channel + 14-bit parameter number | no | a sequence of CCs (99/98 or 101/100, then 6/38); data entry increments |
  | Program Change / Bank Select | channel | no | a selector rather than a value |
  | MIDI 2.0 controllers | group + channel (+ note) + index | no | 32-bit values: registered/assignable controllers, per-note controllers |
  | MIDI 2.0 Property Exchange | resource name + JSON pointer | yes: Get, Subscribe | JSON resources over MIDI-CI; the device describes itself |

  **One abstraction: the parameter port.** Each device instance gets a parameter port per kind of parameter it uses. A port does one thing: it moves the values of parameters between the device state and the device. Its operations, and what a port tells about its capabilities:

  | Operation | Meaning | Not every port can |
  |-----------|---------|--------------------|
  | Read | ask the device for the current values of parameters | CC, NRPN and Program Change cannot be read back |
  | Write | send new values | `midi.access="read"` parameters cannot be written |
  | Observe | report values the device sends on its own | a device may not echo its front panel |
  | Group | the smallest set of parameters that is transferred together | atomic groups in an address map; an MSB/LSB pair |

  The device state and the controls only see logical values with a value domain (range, steps, options), as the schema's data types define them. Turning a logical value into bytes is the port's job: the serializer for the address map, the CC encoding for controllers. A control therefore does not know or care whether its knob ends up in a DT1, a CC or a Property Exchange Set.

  The capabilities matter for the UI: a parameter that cannot be read back stays *unknown* (hollow) until the user sets it or the device sends it, and a sync skips it. The same holds for a device whose exchange pattern is not supported: its port reports what it can do.

  **Keep the schema the source of truth.** Every parameter, of every kind, is identified by a schema path of the device's schema. For the address map this already holds. For CC, NRPN and MIDI 2.0 controllers the idea is to describe them in the device schema as well: fields with their data type (range, options, as for SysEx parameters) and `midi.*` tags that say how they are transferred (e.g. a controller number), in the spirit of the address map tags. Then:
  - a binding is always device alias + schema path + instance, whatever the kind;
  - the parameter tree in *Edit* mode shows all parameters of a device in one place;
  - the port for a parameter is chosen by the tags of its field, data-driven, without device specific code;
  - the same parameter may be reachable in two ways (a filter cutoff as CC 74 and as a SysEx address): one field, two transfers, and the port is chosen by capability (e.g. CC for fast changes while dragging, SysEx to read back).

  Property Exchange is different: the device describes its own resources at run time. A port for it would build its parameters from the device's resource list instead of a schema; the bindings would still be paths, but into the device's resources. This is the furthest away and needs its own design.

### Looks

- [ ] **Choosing and composing looks; more kits, chromes and finishes.** The user chooses a look, or composes one from the building blocks: a chrome for the controls and one for the sections, a kit of faces (with an override per control type), a neutral palette, typography and a finish, each with its parameters (colours, corner radius, screws or not). The building blocks themselves are made in code; the user combines them, the way the schemas combine processors. A few ready-made looks are the starting point; free composition is for users who want it, and not every combination will look good.

  A look is a setting of the user, not of the dashboard: a shared dashboard looks the way its user likes it. The device palettes and control colours stay; the look only decides how they are used. A look is saved as a file, in XML or JSON like the rig and the dashboards. A made-up example (the names are not final):

  ```json
  {
    "format": "jacobi.midi.look/1",
    "name": "Workshop",
    "chrome": "plate.screwed",
    "sectionChrome": "panel.riveted",
    "kit": "machined",
    "faces": { "switch": "toggle" },
    "palette": "steel",
    "typography": "condensed",
    "finish": "grain"
  }
  ```

  The [composable controls](readme.md#composable-controls) and the named resources are already there for this: no control has a colour, size, font or shape of its own in code. A look then only selects other faces, chromes and resources.

### Browser support

- [ ] **MIDI in the browser host.** The host exists and works offline. A browser host is attractive (nothing to install, the same dashboards on any computer), but it has pitfalls:
  - **MIDI in the browser is Web MIDI.** A browser `IMidiApi` (the Core contract) would be implemented with JavaScript interop on the Web MIDI API. Web MIDI is MIDI 1.0 bytes, not UMP: the adapter converts SysEx to and from SysEx7 packets, and the UMP group is always 0.
  - **SysEx needs permission.** SysEx access is requested explicitly (`requestMIDIAccess({ sysex: true })`), only on a secure (HTTPS) page, and the user must allow it. The console is useless without SysEx, so the UI must explain the permission prompt and handle a refusal.
  - **Not all browsers have Web MIDI.** At the time of writing Chromium based browsers support it, Firefox only with an extra site permission step, and Safari not at all. The host needs a clear message when MIDI is not available, and offline use must still work.
  - **No file system.** Rigs, dashboards and schemas are opened with file pickers and saved as downloads, or kept in browser storage (which the user or the browser can clear). Loading device assemblies (DLLs) the way the desktop does is not an option; only the schemas, processors and exchange protocols built into the application and uploaded schema files would be available.
  - **One thread.** WebAssembly .NET runs on the browser's thread: blocking waits deadlock, and the exchange's timeouts and message intervals depend on a busy UI thread. The exchange and the Core contracts must stay fully asynchronous for this to work.
  - **Trimming.** A published WebAssembly app is trimmed; the discovery of processor factories and anything else found by reflection must be preserved explicitly, or it disappears at run time.
  - **Timing.** Web MIDI timestamps are in milliseconds (`performance.now()`), not the native MIDI clock; `send` takes such a timestamp for scheduling.

  A browser host could also serve the [remote MIDI access](../readme.md#remote-midi-access) of the vision: the device stays at home, the dashboard runs anywhere.

### Later

- [ ] **Shared UI library extracted from the Schema Editor**: the MIDI bar and its connection service, the themes and palette, and the registration of device assemblies and their processors; the central package versions (`Directory.Packages.props`) move with them to a level above both applications. *Not now (2026-10-29):* the Schema Editor's MIDI bar holds one connection, the console a session per device of the rig, so the console got its own MIDI bar.
- [ ] The patch librarian (it will build on device state snapshots).
- [ ] MIDI learn, mapping a hardware controller onto dashboard controls.
- [ ] **UI design v2**: <https://claude.ai/artifact/M1HGkZK6yVEYUJKhEVzoNZ?sk=d8ie2DI0HEzm8BEo40qjnQ#play-15dd8cd198dd>

  The first mock-up of Edit mode, for reference:

  ```text
  ┌───────────────────────────────────────────────────────────────────────────────────────┐
  │ ☰  Dashboards: [Lead sound ▾] [+]     [Edit | Play]                    MIDI ● Sync ⟳ │
  ├──────────────────────┬───────────────────────────────────────────┬────────────────────┤
  │ RIG                  │  ┌ Filter (synth) ────────────────────┐   │ CONTROL            │
  │ ▾ synth  (Acme)  ●   │  │ ◜◝      ◜◝   ╱╲___               │   │ Knob · 2×2         │
  │   ▸ Edit             │  │ Cutoff  Reso  ╱    ╲  Amp env      │   │ Label  Cutoff      │
  │     ▸ Filter         │  │  64      12   A D  S R             │   │ Unit   -  Dec. 0   │
  │ ▸ pedalboard (GX) ○  │  └────────────────────────────────────┘   │ Color  ■ device    │
  │ CONTROLS             │  ┌ pedalboard ──────────────┐             │ BINDING            │
  │ Knob Slider Switch … │  │ [On] Drive ◜◝ Level ◜◝ │             │ synth ·            │
  │ LAYOUT               │  └──────────────────────────┘             │  Edit.Filter.Cutoff│
  │ Row Column Grid …    │                                           │                    │
  └──────────────────────┴───────────────────────────────────────────┴────────────────────┘
  ```

## Done

### Milestones

- [x] Skeleton: the four projects in the solution; the model types; binding resolution with `device`/`base` inheritance; the face state; the layers proven with one knob (`KnobControl`, `ArcKnobFace` in the flat kit, `ControlChrome`, palette resources); tests.
- [x] Model: rig, dashboard, parameter and layout controls, bindings with `device`/`base` inheritance; XML and JSON reading and writing; tests.
- [x] Schemas and index variables: the schema library (built-in assemblies plus the assemblies and schema files of rig, device and dashboard, compiled with the serializer's objects); bindings checked against it; index variables on layout controls with the index selector, their item counts from the address maps; tests with the GP-8 schema.
- [x] Device state: values by parameter, offline loading from and saving to a dump; tests with the test device of the addressing tests.
- [x] Composable controls: control, face, chrome and palette as separate layers with the face state; one kit and one look; all styling through named resources.
- [x] First controls and dashboard view (before the synchronisation, which is postponed): the layers (control, face, chrome, palette) with two looks, Foundry and Anodized, to tweak; knob, fader, switch and text; the layout controls; a dashboard is opened with its rig and schema library (`DashboardSession`), the controls follow the device state (`ParameterValue`), with ranges, steps and value types from the data types (`ParameterInfo`); values from dumps; the GP-8 demo; tests of the session.
- [x] Dashboard canvas with the layout controls and the parameter controls in *Play* mode.
- [x] Dashboard canvas, done as: index variables as binding targets (`$patch`, `VariableValue`) instead of an index selector control, with the number box (▲/▼) in both kits; controls that re-bind when a variable changes; typing a value (double-click) and reset (Ctrl+click) for all controls; several dashboards on one rig sharing the device states (`RigSession`, `DashboardSession`); the Edit/Play switch (Edit not available yet); the GP-8 compare demo; tests.
- [x] Device palettes and control colours: the palette derived from one colour with contrast (`RgbColor`, `Palette`); the colour cascade in the dashboard builder; colours per look, tints and shades and any colour in a colour chooser (right-click); control and device colours; Save and Save as for the dashboard and the rig; tests.
- [x] *Edit* mode foundation: values locked, selecting controls, colours by right-click, the property panel (label, size, display settings, colours, bindings shown), undo and redo of every edit; tests of the edit history.
- [x] *Edit* mode: rig and parameter tree, drag & drop, control palettes, control panel: the parameter tree of a device (`ParameterNode`); the inheritance of device, base and variables in one place (`BindingScope`); dropping fields, groups and palette items with the index variables they need (`ControlDrops`, `ControlCatalog`); moving controls into another grid or section with their bindings (`ControlMoves`); deleting controls and changing their kind; editing bindings, device, base, variables and arrangement; editing the devices of the rig; the sessions following the edits; a new dashboard; tests.
- [x] MIDI connections: `RigConnections` (Console.Core, `Connections/`) holds the MIDI session, the endpoints and a `DeviceSession` per device of the rig; Edit mode is always connected; Play mode has an explicit choice in the MIDI bar, Offline (dump files) or MIDI, and the bar shows the connection of each device. In Play mode with MIDI the controls of a device that is not online are disabled; tests with a fake MIDI backend.
- [x] The device's endpoint: the device panel picks the endpoint from the endpoints MIDI reaches and the UMP group (with the names the endpoint gives them), shows the device's connection and tests it without changing any value (Identity Request, `DeviceIdentity` in Midi.Device; or a request of the first parameter of its map that is discarded, `DeviceSession.Probe`); the rig tree shows the connection per device.
- [x] Undo and redo of every edit (`EditHistory`).

### Prerequisites in the existing code

- [x] **A public value store.** The device state needs a value model by path and instance, as the internal `FieldValueStore` of the addressing code is. Make one public implementation and use it in both places.
- [x] **Sending one parameter.** A data message only carries one whole group occurrence (see [Device todo](../Jacobi.Midi.Device/todo.md)): one field of a non-atomic group, such as a parameter in the GX-700's individual temporary area, cannot be sent yet (`NotSupportedException`). The console sends one parameter per change, so this has to be solved first: an open choice that targets a range in Jacobi.BinarySerializer, and `CreateData`/`Decode` that accept any aligned block. *Done:* a data message carries any block of whole fields (one parameter, part of a group, neighbouring groups), sent and received; the open choice of Jacobi.BinarySerializer targets a range (`FromTarget` to `ToTarget`).
- [x] **A device session** (Midi.Device): a `DeviceSession` (one per connection) keeps the state of the device; Send completes atomic groups from it and only reads what it does not hold (`SessionData.Device` always reads), instead of reading the whole atomic group before every send.
- [x] **The connection state on the exchange** (Midi.Device): the session reports a lost connection (`IsConnected`, `Disconnected`; the exchange has `ConnectionState`/`ConnectionStateChanged`), so the console can show a device as disconnected.
