# MIDI Console

The MIDI Console lets a musician control the settings of their MIDI devices from **dashboards**: pages of standard
controls (knobs, sliders, switches, envelopes, ...) that are bound to the parameters of one or more devices. The
devices are described by the MIDI schemas made with the Schema Editor; the console needs no device specific code.

The users of the console are musicians, not developers: they never see schemas, processors or .NET. They see their
devices, the parameters of those devices (named as in the manual) and the dashboards they build from them.

This document is the design of the first version (v1), with the ideas for v2 where they influence v1. The application
is not built yet.

## Goals of v1

- Set up the devices you own (the **rig**) once: which device it is, how it is connected, its device id.
- Build any number of dashboards: one per device, or one for a purpose that combines parameters of several devices.
- Add standard controls and bind them to device parameters. A control manages one parameter (a knob) or several (an envelope).
- Arrange the controls with layout controls (rows, columns, grids, sections) that have no MIDI function.
- Play the dashboard: a control shows the current value of the device and sends a change to the device; changes made on the device itself show up on the dashboard.
- See at a glance which device a control belongs to: every device has its own colour palette; single controls can be given another colour.
- Save and load rigs and dashboards as XML or JSON.
- Work offline: edit a dashboard and its values without a device, load values from and save them to a SysEx dump.
- A stylized look: a control shows its function, not a photo of hardware.
- An Avalonia desktop application, like the Schema Editor.

Not in v1: see [Not in v1](#not-in-v1).

## Concepts

| Term | Meaning |
|------|---------|
| **Rig** | The devices of the user: a list of device instances. Saved in its own file, so the same rig serves all dashboards. |
| **Device instance** | One device of the rig: an **alias** (`synth`, `pedalboard`), a display name, the **map root** of its schema (e.g. `Gx700.AddressMap`), the `DeviceValues` the map root does not fix (e.g. `deviceId`), how to reach it (the endpoint by name and the UMP group) and its colour. |
| **Parameter** | One field of a device's address map, identified by its schema path and `InstancePath` (e.g. the `Rate` of the phaser in the temporary patch). The display name, value range, steps and option names come from the schema's data type (`min`, `max`, `scale`, `shift`, `options`). |
| **Dashboard** | A tree of controls: layout controls that contain parameter controls and other layout controls. It refers to devices by alias only, so it is portable between rigs. |
| **Control** | An element of a dashboard. A **parameter control** (knob, envelope, ...) has named **slots**, each holding one parameter. A **layout control** (row, grid, section, ...) has no slots: it arranges and groups the controls inside it. |
| **Binding** | What fills a slot: device alias + schema path + instance path. The slots of one control may be bound to parameters of different devices. |
| **Device state** | The console's value model of one device instance: the last known value of each parameter. All controls bound to the same parameter, on any dashboard, show the same value. |
| **Face** | The inner part of a parameter control: how its value is drawn (an arc, a ring of LEDs, a schematic symbol). Faces of one style for all control types form a **kit**. |
| **Chrome** | The outer part of a control or section: the plate or frame around it, with the label and the value text. |
| **Palette** | The colours a control is drawn with. Each device has its own palette, derived from the device's colour. |
| **Look** | A named combination of chrome, a kit of faces, a neutral palette, typography and a finish: the style of all controls. v1 has one look (light and dark); choosing and composing looks is v2. |
| **Edit / Play** | The two modes of a dashboard: in *Edit* the layout and bindings are changed, in *Play* the controls are used. |

### Single place of truth

Controls never send MIDI. A parameter control reads and writes the **device state**; the device state is synchronised
with the device. This keeps the UI independent of how a parameter reaches its device (see
[Parameter sources and targets](#parameter-sources-and-targets)), lets several controls share a parameter, and makes
offline use the same as online use minus the synchronisation.

## Parameter controls

The v1 catalog. The *suggested for* column is how the console picks a control when a parameter is dropped on a
dashboard; the user can change the control type afterwards.

| Control | Slots | Suggested for |
|---------|-------|---------------|
| Knob | value | a number with a range (`min`/`max`) |
| Slider (horizontal, vertical) | value | one number, or two numbers (min/max), both with a range |
| Switch | value | a `sys.bool` (e.g. `roland.switch`) |
| Selector | value | a data type with `options`: a row of segments, or a drop-down list when there are many options |
| Number box | value | any number, when typing the exact value matters |
| Text | value | a string (a patch name) |
| XY pad | x, y | two numbers |
| Envelope | per segment: time, level | a set of envelope parameters |

### The envelope is a list of segments

Envelopes differ per device: ADSR, ADR, AR, Roland's T1-T4/L1-L3, a compressor's attack and release. The envelope
control is therefore not an ADSR control: it is a list of **segments**, each with an optional *time* slot and an
optional *level* slot, and a flag for the segment that *holds* (sustains) until note off. ADSR, AR and the multi-stage
envelopes are presets of that one control. Dragging a breakpoint changes the time and the level of its segment
together; a segment without a bound level uses a fixed level (0 or the maximum) in the drawing.

### Behaviour of all parameter controls

- Drag vertically or turn the mouse wheel to change the value; with Shift in fine steps.
- Double-click to type a value.
- Ctrl+click resets to the default value (the data type's `optionsDefault`, otherwise not available).
- The name and the value are always shown. The value is the logical value of the data type (after `scale` and `shift`); its unit and number of decimals are display settings of the control (see below).
- A parameter whose value is not known yet (not received from the device, not loaded) is drawn hollow, without a value.
- Keyboard: Tab moves between controls, the arrow keys change the value.

### Display settings belong to the dashboard

How a value is presented is a display concern, not a data concern: the schema says what the value *is* (range,
steps, option names), the dashboard says how it is *shown*. Per control:

- `unit`: the text after the value (`ms`, `Hz`, `dB`, `%`).
- `decimals`: the number of decimals shown.
- `label`: the name shown; default the parameter's field name.
- `color`: another colour for this control (see [Colour](#colour)).

Should units turn out to be the same for every dashboard of a device, a schema property can be added later as the
default; the dashboard setting stays the override.

## Layout controls

Layout controls have no MIDI function: they arrange, group and label. The root of a dashboard is a layout control, so
a dashboard is a tree. Every control has a **size in cells**; the cell is the unit of the whole dashboard (a knob is 2×2,
an envelope 6×3) and the cell size scales the whole dashboard.

| Control | Arranges its children | Used for |
|---------|-----------------------|----------|
| Row, Column | next to / below each other (flex): `gap`, alignment, `wrap`; a child's `grow` shares the remaining space | most layouts; they adapt when controls are added or removed |
| Grid | on cells, each child at a cell position (`at`) | exact placement, e.g. to copy a device's panel layout |
| Section | as a row or column, inside a frame with a title | a group of parameters: "Filter", "Amp", a device |
| Tabs | one page at a time | many parameters on a small screen; the sections of a patch |
| Spacer, Separator | — | room or a line between controls |
| Label | — | a text on the dashboard |

A layout control can set what its children inherit:

- `device` and `base`: the device alias and the schema path that the bindings inside it are relative to. A section "Filter" with the base `AddressMap.Edit.Filter` holds knobs bound to `Cutoff` and `Resonance`. This is the same `base` a multi-slot control (the envelope) uses for its slots: one mechanism. A `base` is always a full path from the map root; bases do not stack.
- the palette: by default the palette of its `device` (see below).

Moving a section moves its controls; copying a section and changing its `base` (e.g. from the first to the second
oscillator) copies all its bindings.

## Visual style

Flat and functional. Every parameter control has one recognisable shape and the value is part of that shape:

- a knob is an arc from the origin to the value; the origin is zero within the range: the minimum for 0..100, the centre for a bipolar range such as -50..+50 (a `shift` that makes the minimum negative), the maximum for -50..0;
- a slider is a filled bar, a switch a lit tile, a selector a row of segments with the selected one lit;
- an envelope is its curve with breakpoints to drag.

These rules hold for every look; a look only decides how they are drawn.

### Composable controls

A control is not drawn as one piece: it is composed of layers, each with one responsibility and a narrow contract.
This keeps the control logic independent of the look and lets the user compose a look from ready-made building
blocks (v2, see [Looks](#looks-v2)).

| Layer | Decides | Knows about |
|-------|---------|-------------|
| **Control** | the value, the slots, the interaction (drag, wheel, keyboard, typing a value, reset) | the device state; nothing of the look |
| **Face** | how the value is drawn: an arc, a ring of LEDs, a schematic switch, a row of segments | the face state (below) and the palette |
| **Chrome** | the plate or frame around a control, and the frame of a section | the label, the value text and the face as its content |
| **Palette** | the colours, as named brushes (accent, track, fill, text, plate, ...) | nothing; the device palette and a control's own colour replace entries |
| **Typography** | the fonts of labels and values | nothing |
| **Finish** | effects over the whole dashboard: grain, scanlines, the strength of a glow | nothing |

**The face state** is the only thing a control gives its face: a small, immutable description of what to draw:

- the value as a fraction of its range (0-1) and the origin of the range (the minimum, or the centre for a bipolar value);
- whether the value is unknown;
- for a selector the option names and the selected option, for a switch on or off;
- for an envelope its breakpoints (time and level per segment, the hold segment).

A face draws the face state with the palette's brushes, nothing else: it never sees the device state, the schema or
MIDI, and it has no behaviour. The interaction stays in the control, so a knob is dragged the same way whatever face
it has. A face also tells its preferred size in cells (a ring of LEDs needs more room than a flat arc); the chrome
and the layout controls adapt to it.

In Avalonia a control is one class per control type, a face a small control that draws itself (`Render`), chosen by
key from the look, a chrome a `ControlTheme` of the frame control (also used by sections), and the palette, typography
and finish are resources. Static parts (plate, track) and the value (arc, pointer) are drawn separately, so a change
of the value only redraws the value.

**Kits.** A face is needed per control type: knob, slider, switch, selector, number box, text, XY pad and envelope.
Faces that belong together form a kit; a look picks a kit and may override the face of a single control type. Faces of
different kits share their drawing code where they can (one arc renderer, drawn with different strokes). The number
of faces is the real cost of this design, and it grows with the number of kits that are made, not with the number of
looks: a look is only a combination.

**Glow and other effects** belong to the finish and the palette (e.g. a glow radius), not to a face: effects are
expensive while values change, so they are applied in one place and can be reduced in one place.

### Colour

Colour carries meaning, not decoration. The colour of a control is found in this order; the first that is set wins:

1. the control's own `color`;
2. the palette of the nearest layout control that sets one;
3. the palette of the device the control's (first) slot is bound to;
4. the look's neutral palette (unbound controls, layout controls without a device).

**A palette per device.** A device instance has one colour in the rig; the console derives the device's palette from it
(accent, track, fill, text on the accent) for the light and the dark variant of the look, with enough contrast for
the text. Because the colour is part of the rig, a device has the same colours on every dashboard; a dashboard can
override it (e.g. two devices of the same colour on one dashboard). On a dashboard that combines devices, the colour
shows which device a control belongs to.

**A colour per control.** The user can give a single control another colour: a swatch of its device's palette, or any
colour (from which the same palette is derived). Use it to make a control stand out, not to show the device: that is
what the device palette does.

### Looks (v2)

In v2 the user chooses a look, or composes one from the building blocks: a chrome for the controls and one for the
sections, a kit of faces (with an override per control type), a neutral palette, typography and a finish, each with
its parameters (colours, corner radius, screws or not). The building blocks themselves are made in code; the user
combines them, the way the schemas combine processors. A few ready-made looks are the starting point; free
composition is for users who want it, and not every combination will look good.

A look is a setting of the user, not of the dashboard: a shared dashboard looks the way its user likes it. The device
palettes and control colours stay; the look only decides how they are used. A look is saved as a file, in XML or JSON
like the rig and the dashboards. A made-up example (the names are not final):

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

To make that possible later, v1 already builds the [composable controls](#composable-controls) and draws everything
through named resources: no control has a colour, size, font or shape of its own in code. A v2 look then only
selects other faces, chromes and resources.

### Edit and Play

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

In *Edit* mode:

- the left panel shows the rig: the devices and their parameters as a tree (the groups and fields of the map root, with the names of the schema), and the palettes of parameter and layout controls;
- dragging a parameter onto the dashboard creates the suggested control, already bound; dragging a group creates a section with its `device` and `base` set and a control per field;
- dragging a control from the palette creates an unbound control; its slots are bound in the right panel, or by dropping a parameter on the slot;
- the right panel edits the selected control: type, size, display settings, colour and bindings; for a layout control its arrangement (`gap`, alignment, ...) and what its children inherit.

In *Play* mode the layout is locked and only the values change.

## Files

The rig and the dashboards are separate files, in XML or JSON (the format follows the file extension, as with the
schemas). The examples below are made up (the device `Acme` does not exist); they show the shape of the files, not
the final names.

### Rig

```json
{
  "format": "jacobi.midi.rig/1",
  "schemas": [ "C:/Schemas/Acme.xml" ],
  "devices": [
    {
      "alias": "synth",
      "name": "Acme S-1",
      "mapRoot": "Acme.AddressMap",
      "deviceValues": { "deviceId": "0x10" },
      "endpoint": { "name": "USB MIDI Interface", "group": 0 },
      "color": "#3D8BFD"
    }
  ]
}
```

- `schemas`: schema files beyond the schemas embedded in the device assemblies (Device, Roland, Yamaha, Korg).
- `endpoint`: the endpoint is matched by its **name** and UMP group. Endpoint ids are not portable (native Windows ids) and do not identify an instrument behind a DIN port. When no endpoint with that name is found, the device is shown as not connected and the user picks another endpoint.
- `color`: the colour the device's palette is derived from.

### Dashboard

```json
{
  "format": "jacobi.midi.dashboard/1",
  "name": "Lead sound",
  "cellSize": 48,
  "devices": [ { "alias": "synth", "mapRoot": "Acme.AddressMap" } ],
  "layout": {
    "type": "row", "gap": 1,
    "children": [
      { "type": "section", "label": "Filter", "direction": "row",
        "device": "synth", "base": "AddressMap.Edit.Filter",
        "children": [
          { "type": "knob", "label": "Cutoff", "bind": { "value": "Cutoff" } },
          { "type": "knob", "label": "Resonance", "bind": { "value": "Resonance" }, "color": "#E8590C" }
        ] },
      { "type": "envelope", "label": "Amp env", "size": [6, 3], "unit": "ms",
        "device": "synth", "base": "AddressMap.Edit.AmpEnv",
        "segments": [
          { "time": "Attack" },
          { "time": "Decay", "level": "Sustain", "hold": true },
          { "time": "Release" }
        ] }
    ]
  }
}
```

The same dashboard in XML:

```xml
<dashboard format="jacobi.midi.dashboard/1" name="Lead sound" cellSize="48">
  <devices>
    <device alias="synth" mapRoot="Acme.AddressMap" />
  </devices>
  <row gap="1">
    <section label="Filter" direction="row" device="synth" base="AddressMap.Edit.Filter">
      <knob label="Cutoff">
        <bind slot="value" path="Cutoff" />
      </knob>
      <knob label="Resonance" color="#E8590C">
        <bind slot="value" path="Resonance" />
      </knob>
    </section>
    <envelope label="Amp env" size="6,3" unit="ms" device="synth" base="AddressMap.Edit.AmpEnv">
      <segment time="Attack" />
      <segment time="Decay" level="Sustain" hold="true" />
      <segment time="Release" />
    </envelope>
  </row>
</dashboard>
```

- `devices` lists the aliases the dashboard needs with the map root they must have, and optionally a `color` that overrides the rig's colour on this dashboard. Opening a dashboard whose alias is not in the rig asks the user to pick a rig device with the same map root.
- A binding has a device alias, a schema path and, for a parameter inside a repeat, an `instance` (e.g. `[5]`). The device and the start of the path may come from the `device` and `base` of the control or of a layout control around it. In v1 the instance is fixed: a dashboard binds to one place in the device's address space, normally the edit buffer (temporary patch).
- A control without a `size` gets the default size of its type; `at` is only used inside a grid, `grow` only inside a row, column or section.
- The model is independent of the format: one model, an XML and a JSON reader/writer, as for the schemas.

## Synchronisation with the device

The console uses the [message exchange](../Jacobi.Midi.Device/Exchange/readme.md) of the Device project; it never
builds SysEx messages itself.

1. **Connect.** Per device instance used by an open dashboard, a `MessageExchange` is created by a `MessageExchangeFactory` on the session, with the device instance's `DeviceValues` and endpoint.
2. **Sync.** The ranges (`PlanRange`) that cover the bound parameters are received (`Receive`) into the device state. The exchange widens a range to the atomic groups it overlaps, so a sync usually delivers more values than are bound; the device state keeps them all.
3. **Follow.** The exchange listens (`Listen`) on the same ranges, so changes made on the device show up on the dashboard.
4. **Change.** A control writes the device state, which queues a `Send` of the parameter's range. While a knob is dragged, only the latest value per parameter is kept in the queue, so the device gets as many messages as it can take (paced by `midi.messageInterval`), not one per mouse move.
5. **Failure.** A block that times out or is rejected (`AddressBlockStatus`) is shown on the controls of its parameters and in the status bar; nothing fails silently.

Each device has its own exchange: a control whose slots are bound to different devices sends to each device
separately. There is no transaction across devices.

**Offline**, the device state is filled from a `.syx` dump (the data messages are recognised with the address map,
as with `ReceivedData`) and can be written to a dump again. Synchronisation is the only difference between offline
and online.

### Exchange patterns the console does not support (v2)

Some devices need a message exchange pattern that the exchange does not have (see the
[Device todo](../Jacobi.Midi.Device/todo.md): the Roland handshake, Yamaha Bulk Header/Footer, write/store requests,
device modes, a manual operation on the device), and some need one that is specific to that device and of no use to
any other. The console must not assume that every device in a rig can be synchronised:

- **Detect it, show it.** When the exchange cannot handle a device (an unknown `midi.exchange`, a message it cannot use), the device is marked in the rig and on its controls with the reason. Its dashboards still open and work offline; nothing fails silently.
- **Partial use.** A device that can be written but not read (no usable request) is used write-only: its parameters stay *unknown* until set, as for the parameters that cannot be read back (see [Parameter sources and targets](#parameter-sources-and-targets)).
- **Generic first.** A pattern that more devices use becomes a built-in protocol (`ExchangeProtocols`) or, later, a configurable state machine (an `ITransferMachine`) described in the schema.
- **Device specific as a plug-in.** A pattern that only one device uses comes with that device's assembly, the way processors do: a factory per assembly with its own namespace prefix, selected by name in the schema (e.g. `midi.exchange="acme.handshake"`). The console registers device assemblies already (for their processors and schemas), so it finds these protocols the same way, and the machines stay inside the existing `IExchangeProtocol` model.
- **The user in the loop.** Patterns that need the user ("press the bulk dump key", "switch the device to edit mode") need a prompt from the console: the exchange asks, the console shows the request with a *Cancel*, the exchange waits. The console supplies this prompt for every device; the request text comes from the schema or the protocol.

## Parameter sources and targets

v1 only supports parameters in an address map (SysEx data messages through the message exchange). Devices use other
ways too, and the console is designed so that they can be added without changing the dashboards, the controls or the
device state.

### The ways a device exposes a parameter

| Kind | Identification | Read back | Notes |
|------|----------------|-----------|-------|
| Address map (SysEx) | address, via the schema path | yes: request (RQ1, Dump Request) | value range in the schema; atomic groups; v1 |
| Control Change | channel + controller number | no; only what the device sends | 7-bit; 14-bit as an MSB/LSB pair (CC 0-31 + 32-63) |
| RPN / NRPN | channel + 14-bit parameter number | no | a sequence of CCs (99/98 or 101/100, then 6/38); data entry increments |
| Program Change / Bank Select | channel | no | a selector rather than a value |
| MIDI 2.0 controllers | group + channel (+ note) + index | no | 32-bit values: registered/assignable controllers, per-note controllers |
| MIDI 2.0 Property Exchange | resource name + JSON pointer | yes: Get, Subscribe | JSON resources over MIDI-CI; the device describes itself |

### One abstraction: the parameter port

Each device instance gets a **parameter port** per kind of parameter it uses. A port does one thing: it moves the
values of parameters between the device state and the device. Its operations, and what a port tells about its
capabilities:

| Operation | Meaning | Not every port can |
|-----------|---------|--------------------|
| Read | ask the device for the current values of parameters | CC, NRPN and Program Change cannot be read back |
| Write | send new values | `midi.access="read"` parameters cannot be written |
| Observe | report values the device sends on its own | a device may not echo its front panel |
| Group | the smallest set of parameters that is transferred together | atomic groups in an address map; an MSB/LSB pair |

The device state and the controls only see **logical values with a value domain** (range, steps, options), as the
schema's data types define them. Turning a logical value into bytes is the port's job: the serializer for the address
map, the CC encoding for controllers. A control therefore does not know or care whether its knob ends up in a DT1, a
CC or a Property Exchange Set.

The capabilities matter for the UI: a parameter that cannot be read back stays *unknown* (hollow) until the user sets
it or the device sends it, and a sync skips it. The same holds for a device whose exchange pattern is not supported
(see [above](#exchange-patterns-the-console-does-not-support-v2)): its port reports what it can do.

### Keep the schema the source of truth

Every parameter, of every kind, is identified by a **schema path** of the device's schema. For the address map this
already holds. For CC, NRPN and MIDI 2.0 controllers the idea is to describe them in the device schema as well: fields
with their data type (range, options, as for SysEx parameters) and `midi.*` tags that say how they are transferred
(e.g. a controller number), in the spirit of the address map tags. Then:

- a binding is always device alias + schema path + instance, whatever the kind;
- the parameter tree in *Edit* mode shows all parameters of a device in one place;
- the port for a parameter is chosen by the tags of its field, data-driven, without device specific code;
- the same parameter may be reachable in two ways (a filter cutoff as CC 74 and as a SysEx address): one field, two transfers, and the port is chosen by capability (e.g. CC for fast changes while dragging, SysEx to read back).

Property Exchange is different: the device describes its own resources at run time. A port for it would build its
parameters from the device's resource list instead of a schema; the bindings would still be paths, but into the
device's resources. This is the furthest away and needs its own design.

## Browser support (ideas, not in v1)

The Schema Editor already has a browser (WebAssembly) host without live MIDI. For the console a browser host would be
attractive (nothing to install, the same dashboards on any computer), but it has pitfalls:

- **MIDI in the browser is Web MIDI.** A browser `IMidiApi` (the Core contract) would be implemented with JavaScript interop on the Web MIDI API. Web MIDI is MIDI 1.0 bytes, not UMP: the adapter converts SysEx to and from SysEx7 packets, and the UMP group is always 0.
- **SysEx needs permission.** SysEx access is requested explicitly (`requestMIDIAccess({ sysex: true })`), only on a secure (HTTPS) page, and the user must allow it. The console is useless without SysEx, so the UI must explain the permission prompt and handle a refusal.
- **Not all browsers have Web MIDI.** At the time of writing Chromium based browsers support it, Firefox only with an extra site permission step, and Safari not at all. The host needs a clear message when MIDI is not available, and offline use must still work.
- **No file system.** Rigs, dashboards and schemas are opened with file pickers and saved as downloads, or kept in browser storage (which the user or the browser can clear). Loading device assemblies (DLLs) the way the desktop does is not an option; only the schemas, processors and exchange protocols built into the application and uploaded schema files would be available.
- **One thread.** WebAssembly .NET runs on the browser's thread: blocking waits deadlock, and the exchange's timeouts and message intervals depend on a busy UI thread. The exchange and the Core contracts must stay fully asynchronous for this to work.
- **Trimming.** A published WebAssembly app is trimmed; the discovery of processor factories and anything else found by reflection must be preserved explicitly, or it disappears at run time.
- **Timing.** Web MIDI timestamps are in milliseconds (`performance.now()`), not the native MIDI clock; `send` takes such a timestamp for scheduling.

A browser host could also serve the [remote MIDI access](../readme.md#remote-midi-access) of the vision: the device
stays at home, the dashboard runs anywhere.

## Projects

The projects are in this folder, laid out like the Schema Editor's. The folder has its own `Directory.Packages.props`;
keep its Avalonia versions in sync with the Schema Editor's.

| Project | Contents |
|---------|----------|
| `Jacobi.Midi.Console.Core` | The support code without UI: rig, dashboard, controls and bindings (`BindingResolver`), the face state (`FaceState`, `ValueRange`); later the XML and JSON readers and writers, the device state and its synchronisation with the message exchange. Tested in `Jacobi.Midi.Tests` (`Console` folder). |
| `Jacobi.Midi.Console` | The platform independent Avalonia UI (MVVM): parameter controls (`Controls`), faces and kits (`Faces`), chromes (`Chrome`), palette and control themes (`Themes`); later the dashboard canvas, layout controls, looks, rig and parameter tree and edit panels. |
| `Jacobi.Midi.Console.Desktop` | The desktop host; the Windows build references the Windows MIDI backend, as the Schema Editor's desktop host does. |
| `Jacobi.Midi.Console.Browser` | The browser (WebAssembly) host; without MIDI, so the console works offline there (see [Browser support](#browser-support-ideas-not-in-v1)). |

The namespace `Jacobi.Midi.Console` hides `System.Console` in code in a `Jacobi.Midi.*` namespace that references
these projects (e.g. the tests): write `System.Console` there.

UI parts that the Schema Editor already has and the console needs too are moved to a shared library, so that both
applications use the same code: the MIDI bar and its connection service, the themes and palette, and the registration
of device assemblies and their processors. The central package versions (`Directory.Packages.props`) move with them
to a level above both applications.

## Prerequisites in the existing code

- [ ] **Sending one parameter.** A data message only carries one whole group occurrence (see [Device todo](../Jacobi.Midi.Device/todo.md)): one field of a non-atomic group, such as a parameter in the GX-700's individual temporary area, cannot be sent yet (`NotSupportedException`). The console sends one parameter per change, so this has to be solved first: an open choice that targets a range in Jacobi.BinarySerializer, and `CreateData`/`Decode` that accept any aligned block.
- [ ] **The message interval as the pause on the wire** (see [Device todo](../Jacobi.Midi.Device/todo.md)). Dragging a knob streams messages; over a DIN connection the transmission time must be part of the pause.
- [ ] **A public value store.** The device state needs a value model by path and instance, as the internal `FieldValueStore` of the addressing code is. Make one public implementation and use it in both places.

## Milestones

- [x] Skeleton: the four projects in the solution; the model types; binding resolution with `device`/`base` inheritance; the face state; the layers proven with one knob (`KnobControl`, `ArcKnobFace` in the flat kit, `ControlChrome`, palette resources); tests.
- [ ] Model: rig, dashboard, parameter and layout controls, bindings with `device`/`base` inheritance; XML and JSON reading and writing; tests.
- [ ] Device state: values by parameter, offline loading from and saving to a dump; tests with the test device of the addressing tests.
- [ ] Synchronisation: exchanges per device instance, sync, follow and change with the latest-value queue; unsupported devices marked; tests with a fake port.
- [ ] Shared UI library extracted from the Schema Editor.
- [ ] Composable controls: control, face, chrome and palette as separate layers with the face state; one kit and one look; all styling through named resources.
- [ ] Dashboard canvas with the layout controls and the parameter controls in *Play* mode.
- [ ] Device palettes and control colours.
- [ ] *Edit* mode: rig and parameter tree, drag & drop, control palettes, control panel.

## Not in v1

- Choosing and composing looks; more kits, chromes and finishes (see [Looks](#looks-v2)).
- Exchange patterns the message exchange does not have, generic or device specific (see [Exchange patterns the console does not support](#exchange-patterns-the-console-does-not-support-v2)).
- A "current patch" that changes the instance of many bindings at once; v1 bindings have a fixed instance.
- Parameters that are not in an address map: CC, NRPN, Program Change, MIDI 2.0 controllers and Property Exchange (see [Parameter sources and targets](#parameter-sources-and-targets)).
- The patch librarian (it will build on device state snapshots).
- MIDI learn, mapping a hardware controller onto dashboard controls.
- Undo.
- MIDI in the browser host (see [Browser support](#browser-support-ideas-not-in-v1)); the host itself exists and works offline.
- Devices that only do complete dumps without an address map.

## UI Design v2

https://claude.ai/artifact/M1HGkZK6yVEYUJKhEVzoNZ?sk=d8ie2DI0HEzm8BEo40qjnQ#play-15dd8cd198dd
