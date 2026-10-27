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
| **Rig** | The devices of the user: a list of device instances, and the assemblies and schema files they need. Saved in its own file, so the same rig serves all dashboards. |
| **Device instance** | One device of the rig: an **alias** (`synth`, `pedalboard`), a display name, the **map root** of its schema (e.g. `Gx700.AddressMap`), the `DeviceValues` the map root does not fix (e.g. `deviceId`), how to reach it (the endpoint by name and the UMP group), its colour, and its own assemblies and schema files. |
| **Schema library** | The schemas and processors the console works with, compiled into one `SchemaSet` and `Serializer`: the built-in assemblies plus the assemblies and schema files of the rig, its devices and the dashboard (see [Schemas](#schemas)). |
| **Index variable** | A repeat index the user changes in the UI, e.g. the patch number: declared on a layout control and used in binding paths as `Programs[$patch]`. Changed by a control bound to `$patch`. |
| **Parameter** | One field of a device's address map, identified by its schema path and `InstancePath` (e.g. the `Rate` of the phaser in the temporary patch). The display name, value range, steps and option names come from the schema's data type (`min`, `max`, `scale`, `shift`, `options`). |
| **Dashboard** | A tree of controls: layout controls that contain parameter controls and other layout controls. It refers to devices by alias only, so it is portable between rigs. |
| **Control** | An element of a dashboard. A **parameter control** (knob, envelope, ...) has named **slots**, each holding one parameter. A **layout control** (row, grid, section, ...) has no slots: it arranges and groups the controls inside it. |
| **Binding** | What fills a slot: device alias + schema path, with the repeat indices (constant or index variables) in the path. The slots of one control may be bound to parameters of different devices. |
| **Device state** | The console's value model of one device instance: the last known value of each parameter. All controls bound to the same parameter, on any dashboard, show the same value. |
| **Face** | The inner part of a parameter control: how its value is drawn (an arc, a ring of LEDs, a schematic symbol). Faces of one style for all control types form a **kit**. |
| **Chrome** | The outer part of a control or section: the plate or frame around it, with the label and the value text. |
| **Palette** | The colours a control is drawn with. Each device has its own palette, derived from the device's colour. |
| **Look** | A named combination of chrome, a kit of faces, a neutral palette, typography and a finish: the style of all controls. v1 has two ready-made looks to choose from (Foundry, Anodized); composing looks is v2. |
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
| Number box | value | any number, when typing or stepping the exact value matters; an index variable (`$patch`) |
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
- Double-click to type a value (Enter applies it, Escape cancels; an invalid value keeps the editor open). A number box steps with ▲/▼ instead, so a quick second click there is another step.
- Ctrl+click resets to the default value: the data type's `optionsDefault`, otherwise the origin of a number (zero within its range) and off for a switch; a text has none.
- The name and the value are always shown. The value is the logical value of the data type (after `scale` and `shift`); its unit and number of decimals are display settings of the control (see below).
- A parameter whose value is not known yet (not received from the device, not loaded) is drawn hollow, without a value.
- Keyboard: Tab moves between controls, the arrow keys change the value.

### Display settings belong to the dashboard

How a value is presented is a display concern, not a data concern: the schema says what the value *is* (range,
steps, option names), the dashboard says how it is *shown*. Per control:

- `unit`: the text after the value (`ms`, `Hz`, `dB`, `%`).
- `decimals`: the number of decimals shown.
- `label`: the name shown; default the parameter's field name.
- `color`, `plateColor`, `panelColor`: other colours for this control's accent, plate and (for a section) panel (see [Colour](#colour)).

Should units turn out to be the same for every dashboard of a device, a schema property can be added later as the
default; the dashboard setting stays the override.

## Layout controls

Layout controls have no MIDI function: they arrange, group and label. The root of a dashboard is a layout control, so
a dashboard is a tree.

**Everything is on a grid of cells** (`GridLayout`). Every control has a **size in cells** (`size`: columns,rows); the
cell is the unit of the whole dashboard and the dashboard's cell size scales it as a whole. The root (usually a grid) and
every grid and section place their children at a **cell position** (`at`: column,row, zero based from the top left of
the grid; for a section, of the grid inside its frame). A parameter control is fitted to its cells (`CellFit`): scaled
by the side that limits it, so making it bigger makes its knob bigger, and its chrome stretched along the other side,
so its plate fills its cells and lines up with the grid; a new control gets a default size per kind in which it fits unscaled (a knob is 5×7). A
section's frame and title take a fixed number of cells around its grid (`GridLayout.SectionInset`: one on the sides
and bottom, two on top), in every look. Positions and sizes are never empty: a file that leaves them out gets them when
it is opened (the controls side by side below those that have a position), and the console says so.

When a control is moved or sized onto other controls, these are **pushed down** below it (and the controls they then
overlap, and so on); a grid or section that no longer fits its children **grows**, and pushes in its own grid in turn.

| Control | Arranges its children | Used for |
|---------|-----------------------|----------|
| Grid | on cells, each child at its position (`at`) in its size | the root of a dashboard; exact placement, e.g. to copy a device's panel layout |
| Section | as a grid, inside a frame with a title | a group of parameters: "Filter", "Amp", a device |
| Row, Column | next to / below each other (flex): `gap`, `wrap`; a child's `grow` shares the remaining space; the children have no position | a strip of controls that adapts when controls are added or removed |
| Tabs | one page at a time (the headers take two rows) | many parameters on a small screen; the sections of a patch |
| Spacer, Separator | — | room or a line between controls |
| Label | — | a text on the dashboard |

A layout control can set what its children inherit:

- `device` and `base`: the device alias and the schema path that the bindings inside it are relative to. A section "Filter" with the base `AddressMap.Edit.Filter` holds knobs bound to `Cutoff` and `Resonance`. This is the same `base` a multi-slot control (the envelope) uses for its slots: one mechanism. A `base` is always a full path from the map root; bases do not stack.
- index variables: see below.
- the palette: by default the palette of its `device` (see below).

Moving a section moves its controls; copying a section and changing its `base` (e.g. from the first to the second
oscillator) copies all its bindings.

### Index variables

The parameters of a stored patch are in a repeat of the address map (128 memory patches of the GP-8). Which patch a
dashboard shows is not fixed: the user selects it. A layout control declares an **index variable** with its starting
value (e.g. `patch`, 5), and the bindings inside it use it as a repeat index: `AddressMap.Memory[$patch].Patch.Level`
(directly, or through a `base` such as `AddressMap.Memory[$patch].Patch`). A variable is inherited like `device` and
`base`; a declaration on a nearer layout control hides one with the same name further out. A variable on the
dashboard's root applies to the whole dashboard; two sections with their own variable show two patches side by side
(to compare them).

There is no special selector control: a variable is a binding target like a device parameter. A slot bound to
`$patch` (`<bind slot="value" parameter="$patch" />`) shows and changes the variable, so any number control selects
the patch: typically a number box, but a knob or a fader works too.

- A control sees the variable as the item number: the index + 1, from 1 to the number of items of the repeat it indexes, from the address map (`AddressMapAdapter.ItemCount`). A variable used on repeats with different item counts is reported; a variable that indexes no repeat in a binding path has no range and is reported too.
- Changing the variable resolves the bindings again: the controls that use it are pointed to the newly selected item (`ParameterValue` is re-targeted) and show its values (offline: unknown, or from a loaded dump; online: received from the device, see [Synchronisation](#synchronisation-with-the-device)).
- Showing the number as a device's own patch name (U01-1, A-11, ...) is a display setting still to be designed.
- The dashboard file holds the starting value; the selected value is state of the open dashboard.

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
it has. A face also tells its natural size in cells (a ring of LEDs needs more room than a flat arc); a control with
its chrome is fitted to the cells it is given (scaled, its plate filling the cells). Sizes in cells are at a reference cell of 24 pixels
(`Face.CellPixels`); the dashboard as a whole is scaled to its own cell size, in one place, so controls, fonts, gaps
and grids scale together.

In Avalonia a control is one class per control type, a face a small control that draws itself, chosen by key from
the look, a chrome a `ControlTheme` of the frame control (also used by sections), and the palette, typography and
finish are resources. A face draws on two layers: the static parts (a track, a body, a slot: `RenderStatic`) are only
drawn again when the brushes change or the value becomes known or unknown; the value (an arc, a pointer, lit LEDs:
`RenderValue`) is drawn again on every change of the value.

The resource keys every look provides (the face themes take them; the chrome templates use them too):

| Layer | Keys |
|-------|------|
| Palette | `ConsoleAccentBrush`, `ConsoleAccentTextBrush`, `ConsoleAccentDisplayBrush` (these three are derived from the accent in code, see [Colour](#colour)), `ConsoleTrackBrush`, `ConsoleBodyBrush`, `ConsoleSurfaceBrush`, `ConsolePointerBrush`, `ConsoleDisplayBrush`, `ConsoleDisplayTextBrush`, `ConsoleOutlineBrush`, `ConsoleTextBrush`, `ConsoleBackgroundBrush`, the materials `ConsolePlateBrush` (a control's plate, transparent when the controls are on the panel) and `ConsoleSectionBrush` (a section's panel) with `ConsolePlateTextBrush` (the label on the plate) (these three are tinted in code when a plate or panel has a colour) (and look specific ones for its own chrome, e.g. `ConsoleScrewBrush`) |
| Typography | `ConsoleLabelFont`, `ConsoleDisplayFont` |
| Finish | `ConsoleGlowOpacity` (0 draws no glow) |

**Kits.** A face is needed per control type: knob, slider, switch, selector, number box, text, XY pad and envelope.
Faces that belong together form a kit; a look picks a kit and may override the face of a single control type. Faces of
different kits share their drawing code where they can (one arc renderer, drawn with different strokes). The number
of faces is the real cost of this design, and it grows with the number of kits that are made, not with the number of
looks: a look is only a combination.

**Glow and other effects** belong to the finish and the palette (e.g. a glow radius), not to a face: effects are
expensive while values change, so they are applied in one place and can be reduced in one place.

### Colour

Colour carries meaning, not decoration. Three areas of controls and sections have a colour (`ColorArea`):

- the **accent**: what shows the value and the device (arcs, LEDs, a section's tag or strip); `color` in the files;
- the **plate**: the material a control is mounted on (Foundry's steel plate; Anodized has none until it is given a colour); `plateColor`;
- the **panel**: the material of a section (cast iron, aluminium); `panelColor`.

The colour of an area is found in this order; the first that is set wins:

1. the control's own colour;
2. the colour of the nearest layout control that sets one;
3. the colour of the device: the `device` of a layout control, the device the (first) slot of a parameter control is
   bound to;
4. the look's (unbound controls, devices without a colour).

**A palette derived from one colour.** The console derives the palette from a colour (`Palette` in Console.Core):
the accent itself, the text on the accent (dark or light, whichever has the most contrast) and the accent for text and
figures in the dark displays, lightened in steps until it has a contrast of 4.5 (WCAG 2) with them. The derived
colours are the resources `ConsoleAccentBrush`, `ConsoleAccentTextBrush` and `ConsoleAccentDisplayBrush`; the looks
draw everything in or on the accent with these keys, never with a colour of their own. A look sets them from its
accent for the whole application; a control whose colour differs from the colour around it sets them for itself and
the controls inside it.

**Materials keep their shading.** A plate or panel colour does not replace the look's material by a flat colour: the
stops of the material's gradient get the hue and saturation of the colour, and keep their differences in lightness
around it (`Palette.Tint`), so brushed steel becomes painted metal and keeps its light. A material the look does not
have (Anodized's transparent plate) becomes a plain one, lit from above. The label on a plate gets the text colour with
the most contrast; on a look whose controls are on the panel, the labels follow the panel's colour until the plate
has one.

**Colours per device.** A device instance has its colours in the rig. Because they are part of the rig, a device has
the same colours on every dashboard; a dashboard can override them, per area (e.g. two devices of the same colour on
one dashboard). On a dashboard that combines devices, the colour shows which device a control belongs to.

**A colour per control.** The user can give a single control or section another colour. Use it to make a control
stand out, not to show the device: that is what the device colour does.

**Picking a colour.** In *Edit* mode, a right-click on a control opens the colour chooser for
the area under the pointer, named in its title: on the face or on a section's tag or strip (the elements of a look's
chrome with the class `accentArea`) the accent, elsewhere on a parameter control its plate, elsewhere on a layout
control its panel. The property panel opens the same chooser for each area of the selected control. It offers:

- the colours of the look for the area: a few colours designed to suit it (in `ConsoleLooks`; Foundry has metals and patina, steel and enamel plates, iron and painted panels; Anodized LED colours and anodized aluminium);
- tints and shades of the current colour;
- any colour (a colour view in a flyout of its own, applied with *Apply*);
- *Inherited*: takes the colour away, so the control has the colour of the cascade again;
- *For all controls of device ...*: the pick becomes the device's colour in the rig instead of the control's.

A pick applies at once. The dashboard or the rig is then changed: *Save* writes the selected dashboard and the rig when
they changed, *Save as...* writes one of them to another file (XML or JSON by its extension). A file that was not
read from disk (the demo) is saved with a file picker. When the dashboard gives a device a colour of its own, a new rig
colour does not show on it; the console says so.

### Looks in v1

Two ready-made looks, kept simple as the basis for tweaking before more controls are added:

| Look | Faces (kit) | Chrome | Palette |
|------|-------------|--------|---------|
| **Foundry** | knob: engraved track, bakelite body, brass cap; fader: dark slot, brass cap; switch: pilot lamp and lever; text: amber readout | a control on a brushed steel plate with four screws, an engraved label and an amber readout; a section on a cast iron panel with a tag in the device colour | iron, steel, bakelite, brass, amber |
| **Anodized** | knob: ring of LEDs, machined cap; fader: slot with an LED meter; switch: LED bar and push button; text: OLED display | a control on the panel with a printed label and an OLED readout; a section on an aluminium panel with an LED strip and a title in the device colour | dark aluminium, LEDs and OLED in the accent |

Where things are, for tweaking (in `Jacobi.Midi.Console`):

- **Shapes**: the faces in `Faces/FoundryFaces.cs` and `Faces/AnodizedFaces.cs` (drawn in code; shared drawing code in `FaceDrawing`), collected per look in `Kits`.
- **Colours, materials, fonts and glow**: the palette, typography and finish in `Themes/Foundry.axaml` and `Themes/Anodized.axaml`: the same keys in both (see [Composable controls](#composable-controls)), solid colours or gradients.
- **Chrome**: the `ControlChrome` and `SectionFrame` control themes in the same two files.
- **Templates**: `Themes/Controls.axaml` puts a chrome around a face host for each control and gives every face its brushes from the palette keys; it is shared by the looks.
- **Looks**: `ConsoleLooks` (in `Looks/Look.cs`) lists them; applying one replaces the look's resources and kit and rebuilds the dashboard.

The accent is the device colour of the rig (or the dashboard); a look's own accent is used for controls without a
device. The look is chosen in the toolbar and not saved yet.

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

Several dashboards can be open on one rig; the toolbar switches between them (`Dashboards: [name ▾]`), opens another
(+) and closes one (✕). They share the rig's device states (`RigSession`), so a parameter has one value on every
dashboard; each dashboard (`DashboardSession`) has its own index variables. A dashboard that brings its own schemas
rebuilds the schema library, and with it the device states (their values are reset; this is reported).

What *Edit* mode does so far:

- the values are locked: the controls do not change them (`DashboardEditing.IsEditing`, inherited by all controls of the dashboard), the cursor is the arrow;
- a click selects the control under the pointer (outlined); a right-click also opens the colour chooser for the part under the pointer (see [Colour](#colour)); in *Play* mode a right-click does nothing;
- the right panel shows the selected control: its label, position in its grid (column, row), size (columns × rows) and display settings (unit, decimals) to change, its three colours (each opens the colour chooser, ✕ makes it inherited), and its device, base, variables and bindings as they are;
- dragging a control in a grid or section moves it, dragging the handle at the bottom right of the selection sizes it, by whole cells; the control follows the pointer and is placed when it is let go (controls in the way are pushed down, grids grow); the cells of the grid the selected control is in are drawn over it, with the column and row numbers every five cells;
- every change is an edit (`IEdit`) in the edit history (`EditHistory`): undo (↶, Ctrl+Z) and redo (↷, Ctrl+Y); the history is cleared when a dashboard is closed or another rig is opened. A dashboard or rig counts the edits since it was read or saved, so undoing all of them makes it unchanged again.

Still to come: the rig and parameter tree, the palettes, dragging controls into another grid or section, and adding
and deleting controls.

## Schemas

The console works with the serializer's own objects: a compiled `SchemaSet` and a `Serializer` with the processors,
the same objects the message exchange needs to read and write the MIDI messages. The `SchemaLibraryBuilder` gathers
them, in this order:

1. the built-in assemblies: the serializer's processors (`sys`) and the base `midi` schema with its processors (`SchemaLibraryBuilder.CoreAssemblies`), and the device assemblies the host preloads (Roland, Yamaha, ...);
2. the assemblies and schema files of the rig;
3. those of the rig's devices;
4. those of the dashboard.

An assembly brings its embedded schemas (`.xml`, `.json` resources) and its processor factories. A schema document of a
later source replaces an earlier one with the same name, so a device or a dashboard can customise a built-in schema;
the replacement is reported as information. A source that cannot be loaded (a missing file, an assembly that does not
load, an invalid schema) is reported and the others are still used; schemas that do not compile are reported and the
library then has no serializer.

With the library, `BindingResolver` checks a dashboard against the schemas: every device's map root must be an address
map, every binding a field of its device's map (the path starts at the map root, e.g. `AddressMap.…`), every index
variable must index a repeat of the map and stay below its item count. With the rig it also checks that the rig has the
dashboard's devices with the same map roots. All problems are reported together.

## Files

The rig and the dashboards are separate files, in XML or JSON (the format follows the file extension, as with the
schemas). The examples below are made up (the device `Acme` does not exist); they show the shape of the files, not
the final names.

### Rig

```json
{
  "format": "jacobi.midi.rig/1",
  "assemblies": [ "Acme.Midi.dll" ],
  "schemas": [ "C:/Schemas/Acme.xml" ],
  "devices": [
    {
      "alias": "synth",
      "name": "Acme S-1",
      "mapRoot": "Acme.AddressMap",
      "schemas": [ "AcmeS1Custom.xml" ],
      "deviceValues": { "deviceId": "0x10" },
      "endpoint": { "name": "USB MIDI Interface", "group": 0 },
      "color": "#3D8BFD"
    }
  ]
}
```

The same rig in XML:

```xml
<rig format="jacobi.midi.rig/1">
  <assembly path="Acme.Midi.dll" />
  <schema path="C:/Schemas/Acme.xml" />
  <device alias="synth" name="Acme S-1" mapRoot="Acme.AddressMap" color="#3D8BFD">
    <schema path="AcmeS1Custom.xml" />
    <deviceValue name="deviceId" value="0x10" />
    <endpoint name="USB MIDI Interface" group="0" />
  </device>
</rig>
```

- `assemblies`, `schemas`: on the rig for all its devices, on a device for that device only (see [Schemas](#schemas)); a relative path is relative to the rig file. A dashboard has them too.
- `deviceValues`: in the notation of the schemas (`0x10`, `10H` or decimal); written in hex.
- `endpoint`: the endpoint is matched by its **name** and UMP group. Endpoint ids are not portable (native Windows ids) and do not identify an instrument behind a DIN port. When no endpoint with that name is found, the device is shown as not connected and the user picks another endpoint.
- `color`, `plateColor`, `panelColor`: the colours of the device's controls (see [Colour](#colour)).

### Dashboard

```json
{
  "format": "jacobi.midi.dashboard/1",
  "name": "Lead sound",
  "cellSize": 24,
  "devices": [ { "alias": "synth", "mapRoot": "Acme.AddressMap" } ],
  "layout": {
    "type": "grid", "size": [37, 10],
    "children": [
      { "type": "section", "label": "Filter", "at": [0, 0], "size": [12, 10],
        "device": "synth", "base": "AddressMap.Edit.Filter",
        "children": [
          { "type": "knob", "label": "Cutoff", "at": [0, 0], "size": [5, 7], "bind": { "value": "Cutoff" } },
          { "type": "knob", "label": "Resonance", "at": [5, 0], "size": [5, 7], "bind": { "value": "Resonance" }, "color": "#E8590C" }
        ] },
      { "type": "envelope", "label": "Amp env", "at": [13, 0], "size": [10, 6], "unit": "ms",
        "device": "synth", "base": "AddressMap.Edit.AmpEnv",
        "segments": [
          { "time": "Attack" },
          { "time": "Decay", "level": "Sustain", "hold": true },
          { "time": "Release" }
        ] },
      { "type": "section", "label": "Stored patch", "at": [24, 0], "size": [13, 10],
        "device": "synth", "base": "AddressMap.Programs[$patch].Program",
        "variables": [ { "name": "patch", "value": 0 } ],
        "children": [
          { "type": "numberBox", "label": "Patch", "at": [0, 0], "size": [6, 4], "bind": { "value": "$patch" } },
          { "type": "knob", "label": "Level", "at": [6, 0], "size": [5, 7], "bind": { "value": "Level" } }
        ] }
    ]
  }
}
```

The same dashboard in XML:

```xml
<dashboard format="jacobi.midi.dashboard/1" name="Lead sound" cellSize="24">
  <device alias="synth" mapRoot="Acme.AddressMap" />
  <grid size="37,10">
    <section label="Filter" at="0,0" size="12,10" device="synth" base="AddressMap.Edit.Filter">
      <knob label="Cutoff" at="0,0" size="5,7">
        <bind slot="value" parameter="Cutoff" />
      </knob>
      <knob label="Resonance" at="5,0" size="5,7" color="#E8590C">
        <bind slot="value" parameter="Resonance" />
      </knob>
    </section>
    <envelope label="Amp env" at="13,0" size="10,6" unit="ms" device="synth" base="AddressMap.Edit.AmpEnv">
      <segment time="Attack" />
      <segment time="Decay" level="Sustain" hold="true" />
      <segment time="Release" />
    </envelope>
    <section label="Stored patch" at="24,0" size="13,10" device="synth" base="AddressMap.Programs[$patch].Program">
      <variable name="patch" value="0" />
      <numberBox label="Patch" at="0,0" size="6,4">
        <bind slot="value" parameter="$patch" />
      </numberBox>
      <knob label="Level" at="6,0" size="5,7">
        <bind slot="value" parameter="Level" />
      </knob>
    </section>
  </grid>
</dashboard>
```

- `devices` lists the aliases the dashboard needs with the map root they must have, and optionally colours (`color`, `plateColor`, `panelColor`) that override the rig's on this dashboard. Opening a dashboard whose alias is not in the rig asks the user to pick a rig device with the same map root.
- A binding is written as `[device:]path`. The path is a schema path in the notation of the serializer's node references: a repeat item is given by its index (`Programs[5].Program.Level`) or by an index variable (`Programs[$patch].Program.Level`), so the instance is part of the path. The device and the start of the path may come from the `device` and `base` of the control or of a layout control around it; a binding with its own device (`pedalboard:AddressMap.Temporary.Compressor.Attack`) has a full path.
- `variables` of a layout control: the index variables with their starting value (zero based); see [Index variables](#index-variables).
- `size` (columns,rows) and, inside a grid or section, `at` (column,row) are written for every control; a file without them gets them when it is opened (see [Layout controls](#layout-controls)). `grow` is only used inside a row or column.
- Every file names its kind and version in `format`; a file of another kind or version is not read.

### One model, two formats

The model is independent of the format (`RigFile`, `DashboardFile`). The mappers turn the model into a tree of
format-neutral elements (a name, attributes and child elements) and back; one XML and one JSON implementation turn
such a tree into text and back:

- In **XML** an element is an element: its attributes are attributes, its children child elements, without wrappers.
- In **JSON** an element is an object: its attributes are properties, its children are grouped in a property by their role (`children`, `segments`, `devices`, `variables` as arrays; `layout`, `endpoint` as an object; `bind`, `deviceValues` as an object of key and value; `assemblies`, `schemas` as an array of values). Elements of different kinds in one role (the controls) give their kind in `type`. These roles are listed in one place (`FileRoles`).

Reading is strict: an unknown attribute or element, a missing required value, an invalid value (a binding, a colour,
a size) or a duplicate (a device alias, a slot) is reported with its location in the file, e.g.
`dashboard/row[0]/knob[1]: unknown attribute 'colour'.` Nothing is ignored silently.

## Synchronisation with the device

The console uses the [message exchange](../Jacobi.Midi.Device/Exchange/readme.md) of the Device project; it never
builds SysEx messages itself.

1. **Connect.** Per device instance used by an open dashboard, a `MessageExchange` is created by a `MessageExchangeFactory` on the session, with the device instance's `DeviceValues` and endpoint.
2. **Sync.** The ranges (`PlanRange`) that cover the bound parameters are received (`Receive`) into the device state. The exchange widens a range to the atomic groups it overlaps, so a sync usually delivers more values than are bound; the device state keeps them all.
3. **Follow.** The exchange listens (`Listen`) on the same ranges, so changes made on the device show up on the dashboard.
4. **Change.** A control writes the device state, which queues a `Send` of the parameter's range. While a knob is dragged, only the latest value per parameter is kept in the queue, so the device gets as many messages as it can take (paced by `midi.messageInterval`), not one per mouse move.
5. **Select.** When the user changes an index variable, the bindings are resolved again; the controls that use it get the new instance, and its ranges are received (step 2) and followed (step 3) instead of those of the previous one.
6. **Failure.** A block that times out or is rejected (`AddressBlockStatus`) is shown on the controls of its parameters and in the status bar; nothing fails silently.

Each device has its own exchange: a control whose slots are bound to different devices sends to each device
separately. There is no transaction across devices.

**Offline**, the device state is filled from a `.syx` dump (the data messages are recognised with the address map,
as with `ReceivedData`) and can be written to a dump again. Synchronisation is the only difference between offline
and online.

### Device state

`DeviceState` keeps the values of one device instance in a `FieldValueStore` of the Device project, by the path and
instance of the device's map plan: the identity of a resolved binding (`ParameterReference`). A parameter without a
value is unknown.

- **Reading and writing.** Controls use `TryGetValue` and `SetValue`; the exchange receives into a sink (`CreateSink`) and sends from `Source`.
- **Changes.** `ValueChanged` reports every value that changed, with its origin: `User`, `Device` or `Dump`. Setting the known value again reports nothing. The event is raised on the thread that changed the value; the UI marshals it.
- **Dumps.** `LoadDump` splits a dump into its SysEx messages (`SysExSplitter` of the Messages project) and decodes the data messages of the device's map for this device (`AddressMapAdapter.ReadData`); other messages are skipped and reported. `SaveDump` writes a range of the map as the data messages the exchange would send (`AddressMapAdapter.EncodeData`): widened to atomic groups, all their values must be known.
- **Lossy values.** A value of a dump that the schema does not describe (a physical value that is not one of the options of its data type) is read as the data type's `optionsDefault` and saved as that: such bytes are not reproduced. The GP-8 bulk dump in the tests has four patches like that (`DynamicFilter.UpDown`, `Compressor.Turbo`).

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
| `Jacobi.Midi.Console.Core` | The support code without UI: rig, dashboard, controls and bindings (`BindingResolver`), their XML and JSON files (`RigFile`, `DashboardFile`, `Files`), the schema library (`Schemas`), the device state with dumps (`Devices`), an open dashboard and the values its controls follow (`Parameters`), the face state (`FaceState`, `ValueRange`); later the synchronisation with the message exchange. Tested in `Jacobi.Midi.Tests` (`Console` folder). |
| `Jacobi.Midi.Console` | The platform independent Avalonia UI (MVVM): parameter controls (`Controls`), faces and kits (`Faces`), chromes (`Chrome`), looks (`Looks`, `Themes`), the dashboard view with the layout controls (`Dashboards`), the GP-8 demo (`Assets/Demo`); later the rig and parameter tree and the edit panels. |
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
- [x] **A public value store.** The device state needs a value model by path and instance, as the internal `FieldValueStore` of the addressing code is. Make one public implementation and use it in both places.

## Milestones

- [x] Skeleton: the four projects in the solution; the model types; binding resolution with `device`/`base` inheritance; the face state; the layers proven with one knob (`KnobControl`, `ArcKnobFace` in the flat kit, `ControlChrome`, palette resources); tests.
- [x] Model: rig, dashboard, parameter and layout controls, bindings with `device`/`base` inheritance; XML and JSON reading and writing; tests.
- [x] Schemas and index variables: the schema library (built-in assemblies plus the assemblies and schema files of rig, device and dashboard, compiled with the serializer's objects); bindings checked against it; index variables on layout controls with the index selector, their item counts from the address maps; tests with the GP-8 schema.
- [x] Device state: values by parameter, offline loading from and saving to a dump; tests with the test device of the addressing tests.
- [ ] Synchronisation: exchanges per device instance, sync, follow and change with the latest-value queue; unsupported devices marked; tests with a fake port.
- [ ] Shared UI library extracted from the Schema Editor.
- [x] Composable controls: control, face, chrome and palette as separate layers with the face state; one kit and one look; all styling through named resources.
- [x] First controls and dashboard view (before the synchronisation, which is postponed): the layers (control, face, chrome, palette) with two looks, Foundry and Anodized, to tweak; knob, fader, switch and text; the layout controls; a dashboard is opened with its rig and schema library (`DashboardSession`), the controls follow the device state (`ParameterValue`), with ranges, steps and value types from the data types (`ParameterInfo`); values from dumps; the GP-8 demo; tests of the session.
- [x] Dashboard canvas with the layout controls and the parameter controls in *Play* mode.
- [x] Dashboard canvas, done as: index variables as binding targets (`$patch`, `VariableValue`) instead of an index selector control, with the number box (▲/▼) in both kits; controls that re-bind when a variable changes; typing a value (double-click) and reset (Ctrl+click) for all controls; several dashboards on one rig sharing the device states (`RigSession`, `DashboardSession`); the Edit/Play switch (Edit not available yet); the GP-8 compare demo; tests.
- [x] Device palettes and control colours: the palette derived from one colour with contrast (`RgbColor`, `Palette`); the colour cascade in the dashboard builder; colours per look, tints and shades and any colour in a colour chooser (right-click); control and device colours; Save and Save as for the dashboard and the rig; tests.
- [x] *Edit* mode foundation: values locked, selecting controls, colours by right-click, the property panel (label, size, display settings, colours, bindings shown), undo and redo of every edit; tests of the edit history.
- [ ] *Edit* mode: rig and parameter tree, drag & drop, control palettes, control panel.

## Not in v1

- Choosing and composing looks; more kits, chromes and finishes (see [Looks](#looks-v2)).
- Exchange patterns the message exchange does not have, generic or device specific (see [Exchange patterns the console does not support](#exchange-patterns-the-console-does-not-support-v2)).
- Parameters that are not in an address map: CC, NRPN, Program Change, MIDI 2.0 controllers and Property Exchange (see [Parameter sources and targets](#parameter-sources-and-targets)).
- The patch librarian (it will build on device state snapshots).
- MIDI learn, mapping a hardware controller onto dashboard controls.
- Undo.
- MIDI in the browser host (see [Browser support](#browser-support-ideas-not-in-v1)); the host itself exists and works offline.
- Devices that only do complete dumps without an address map.

## UI Design v2

https://claude.ai/artifact/M1HGkZK6yVEYUJKhEVzoNZ?sk=d8ie2DI0HEzm8BEo40qjnQ#play-15dd8cd198dd
