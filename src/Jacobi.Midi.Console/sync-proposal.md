# Proposal: message exchange in the console

Status: **proposal**, worked out step by step. Decisions go to [docs/decisions.md](../../docs/decisions.md)
(*Message exchange in the console*), the work items to the [todo list](todo.md), what is built to the
[readme](readme.md). This file is removed once it is split over those.

## Terms

| Term | Meaning |
| ---- | ------- |
| **Dump file** | A `.syx` file: SysEx messages one after the other. What the console's Offline mode uses (*Open › Dump…*, `DeviceState.LoadDump`/`SaveDump`, `ValueOrigin.Dump`). |
| **Dump message** | A SysEx message with a fixed content, without addresses (Midi.Device: `DumpMap`). |
| **Address-map data message** | A SysEx message that carries the values at an address (Midi.Device: `AddressMap`, e.g. Roland DT1). |

A dump file can hold either kind of message.

## 1. Where we are

- The console has never exchanged data with a device. What works: the device state (`DeviceState`), offline values
  from dump files (*Open › Dump…*, `LoadDump`/`SaveDump`), and the MIDI connections (`RigConnections`: a
  `DeviceSession` per online device, used only for `Probe`).
- Dump files go through `AddressMapAdapter` (`ReadData`, `EncodeData`): only **address-map data messages** in a dump
  file are recognised. A dump file with **dump messages** (e.g. a Korg function-code dump) gives no values.
- Midi.Device handles both kinds of root through its message maps (`AddressMap`, `DumpMap`), but that generic part
  is internal: the base layer is "meant to work without an exchange file too (...); not everything for that is built
  yet" ([Messages readme](../Jacobi.Midi.Device/Messages/readme.md)).

## 2. One picture: a dump file and a device carry the same messages

A dump file and a device are two carriers of the **same messages** of a root (address-map data messages or dump
messages, as the root's message map describes them):

| | Dump file | Device |
| - | --------- | ------ |
| Messages | at rest, in a file | on the wire |
| Message map (Device.Messages) | recognises and decodes / writes them | the same |
| Pattern (exchange file) | none | which message is sent, what comes back, timing |
| Into `DeviceState` | sink, origin `Dump` | sink, origin `Device` |
| From `DeviceState` | source | source |

So the message map is the single place that knows the messages, for files and devices alike. The exchange is only
the layer on top for the device.

## 3. Parked: dump files with dump messages

*Parked (decision 8): the console keeps `AddressMapAdapter` for its dump files; this is the
[Device todo](../Jacobi.Midi.Device/todo.md) item `IMessageMap.Load` and `Save`. Kept here for the picture.*

What is missing is the public counterpart of the exchange for messages at rest, on top of the internal message maps,
for any root:

- **read**: messages → recognised data messages of the root (for these device values) → decoded into a sink; other
  messages reported;
- **write**: a range and a source → the data messages that carry it (as the device would get them).

No new layer: `IMessageMap` gets a **load** and a **save**, implemented once in the interface (a default
implementation) from what every map already has: `DataMessages`, `TryRead` (+ `ReceivedData.Decode`), `Split` and
`Write`. So `AddressMap` and `DumpMap` get them without code of their own. Sketch (names not final):

- `Load(messages, device, sink, warnings)` → the number of messages decoded: per message, the map's data messages are
  tried (`TryRead`); a recognised one is decoded into the sink, any other is reported.
- `Save(range, source, device)` → the SysEx messages: the range is split for a data message (`Split`) and a message
  is written per block (`Write`). *Later: on the [Device todo](../Jacobi.Midi.Device/todo.md).*

Consequence: `IMessageMap` and `MessageMaps` (to get the map of a root) become public. Only the facts (`Root`,
`Device`, `DataMessages`) and `Load`/`Save` are public members; what the exchange uses (`Split`, `Write`, `TryRead`,
`IsComplete`, `CreateState`, ...) stays internal (`IDeviceState` is internal).

`DeviceState` then uses it for its dump files instead of `AddressMapAdapter`, and so handles dump messages too.
`AddressMapAdapter.ReadData` goes (its only user is `DeviceState`; `EncodeData` goes with `Save`); `PlanData`/`WriteData` of the
Schema Editor stay address-specific for now.

## 4. Step 1: see the device's values (Play mode with MIDI) — built

What the user sees: in Play mode with MIDI, the controls of an online device show the values on the device.

What is there already:

- `RigConnections` holds the `DeviceSession` per online device and the `RigSession` with the `DeviceState`s;
  `ProbeData` already shows how a parameter becomes a range (`Plan.CreateRange(path, instance, path, instance)`).
- `DeviceState.CreateSink(ValueOrigin.Device)` is a sink for the exchange; controls follow `ValueChanged` and marshal
  to the UI thread themselves (`ParameterControlBase`).
- The bound parameters of a dashboard are in `DashboardSession.Resolution` (`ParameterReference`: device, path,
  instance).
- The tests have `FakeMidiApi` with a `Responder` that plays the devices.

Sketch (minimal, names not final):

- `RigConnections.Receive(alias, exchange, parameters, ct)`: per parameter a range, `DeviceSession.RequestData` into the
  device's `DeviceState` with origin `Device`, one after the other; returns the `TransferResult`s. The session answers
  the other parameters of an atomic group it already received from its state, so nothing is requested twice.
- The view model calls it when a device comes online and when the dashboard is opened, both in Play mode with MIDI
  only, for the parameters of the dashboard's device. One dashboard is assumed (decision 10).
- The dashboard names the exchange to request with (decision 9). The exchange file comes from an assembly (embedded,
  loaded with it as now) or is a file listed in the sources next to the schema files (decision 11; the Console todo
  item *Exchange files next to schema files*). `SchemaSources` is shared by the rig, its devices and the dashboard,
  so all three can list exchange files. A made-up example (names not final):

  ```xml
  <exchange path="Acme.exchange.xml" />
  <device alias="synth" mapRoot="Acme.AddressMap" receive="requestData" />
  ```

  `RigConnections.Receive` then calls `DeviceSession.Execute(name, range, ...)` (or `RequestData` when the name
  is the standard one). A device without a `receive` is not received from (offline, as now) and the reason is shown.
- Blocks that do not complete are reported in the notifications (on the controls is a later step).

## 5. Later steps (to be worked out)

- **Operations in the dashboard, bound to exchange patterns:** the dashboard says which operations it offers on a
  device (e.g. receive, send, follow), on which range and with which exchange (by name: `requestData`, `sendData`, or
  one of the device's exchange file). A dump file is the offline counterpart of the same operation.
- **What runs the operations** (the glue between `DeviceState`, the dashboard and the `DeviceSession`): its shape
  follows from the operations above, so it is designed after them.
- **Switching from Offline to MIDI** with values from a dump: prompt the user (send them to the device, or take the
  device's values).
- Failures shown on the controls; devices that cannot be exchanged (no exchange file, read-only, write-only).
