# MIDI Console

The MIDI Console lets a musician control the settings of their MIDI devices from **dashboards**: pages of standard
controls (knobs, sliders, switches, envelopes, ...) that are bound to the parameters of one or more devices. The
devices are described by the MIDI schemas made with the Schema Editor; the console needs no device specific code.

The users of the console are musicians, not developers: they never see schemas, processors or .NET. They see their
devices, the parameters of those devices (named as in the manual) and the dashboards they build from them.

This document describes what the console does now. What is planned (among which the synchronisation of the values with
the devices) is in the [todo list](todo.md).

## Concepts

| Term | Meaning |
|------|---------|
| **Rig** | The devices of the user: a list of device instances, and the assemblies and schema files they need. Saved in its own file, so the same rig serves all dashboards. |
| **Device instance** | One device of the rig: an **alias** (`synth`, `pedalboard`), a display name, the **map root**: the root group of its schema (any root group; usually an address map, e.g. `Gx700.AddressMap`), the `DeviceValues` the map root does not fix (e.g. `deviceId`), how to reach it (the endpoint by name and the UMP groups to send and receive on; usually the same group), its colour, and its own assemblies and schema files. |
| **Schema library** | The schemas and processors the console works with, compiled into one `SchemaSet` and `Serializer`: the built-in assemblies plus the assemblies and schema files of the rig, its devices and the dashboard (see [Schemas](#schemas)). |
| **Index variable** | A repeat index the user changes in the UI, e.g. the patch number: declared on a layout control and used in binding paths as `Programs[$patch]`. Changed by a control bound to `$patch`. |
| **Parameter** | One field of the root group of a device (usually its address map), identified by its schema path and `InstancePath` (e.g. the `Rate` of the phaser in the temporary patch). The display name, value range, steps and option names come from the schema's data type (`min`, `max`, `scale`, `shift`, `options`). |
| **Dashboard** | A tree of controls: layout controls that contain parameter controls and other layout controls. It refers to devices by alias only, so it is portable between rigs. |
| **Control** | An element of a dashboard. A **parameter control** (knob, envelope, ...) has named **slots**, each holding one parameter. A **layout control** (row, grid, section, ...) has no slots: it arranges and groups the controls inside it. |
| **Binding** | What fills a slot: device alias + schema path, with the repeat indices (constant or index variables) in the path. The slots of one control may be bound to parameters of different devices. |
| **Device state** | The console's value model of one device instance: the last known value of each parameter. All controls bound to the same parameter, on any dashboard, show the same value. |
| **Face** | The inner part of a parameter control: how its value is drawn (an arc, a ring of LEDs, a schematic symbol). Faces of one style for all control types form a **kit**. |
| **Chrome** | The outer part of a control or section: the plate or frame around it, with the label and the value text. |
| **Palette** | The colours a control is drawn with. Each device has its own palette, derived from the device's colour. |
| **Look** | A named combination of chrome, a kit of faces, a neutral palette, typography and a finish: the style of all controls. There are two ready-made looks to choose from (Foundry, Anodized). |
| **Edit / Play** | The two modes of a dashboard: in *Edit* the layout and bindings are changed, in *Play* the controls are used. |

### Single place of truth

Controls never send MIDI. A parameter control reads and writes the **device state**. This keeps the UI independent of
how a parameter reaches its device, lets several controls share a parameter, and makes offline use the same as online
use minus the synchronisation.

## Parameter controls

The *suggested for* column is how the console picks a control when a parameter is dropped on a dashboard; the user can
change the control type afterwards.

| Control | Slots | Suggested for |
|---------|-------|---------------|
| Knob | value | a number with a range (`min`/`max`) |
| Slider (horizontal, vertical) | value | one number, or two numbers (min/max), both with a range; horizontal when its cells are wider than tall (`GridLayout.IsWide`), else vertical |
| Switch | value | a `sys.bool` (e.g. `roland.switch`); only a bool: a switch bound to anything else is reported and not bound, a parameter that is not a bool cannot be dropped on it, and a control bound to one cannot become a switch (`ControlCatalog.Accepts`) |
| Selector | value | a data type with `options`: a row of segments, or with more than six options (`SelectorFaceState.MaxSegments`) a display with ▲/▼ whose option opens the list on a click |
| Number box | value | any number, when typing or stepping the exact value matters; an index variable (`$patch`) |
| Text | value | a string (a patch name) |
| XY pad | x, y | two numbers: dragging the point sets both |
| Envelope | per segment: time, level | a set of envelope parameters; a new one is an ADSR (`ControlCatalog.Adsr`), segments are added, removed and set to hold in the control panel |

### The envelope is a list of segments

Envelopes differ per device: ADSR, ADR, AR, Roland's T1-T4/L1-L3, a compressor's attack and release. The envelope
control is therefore not an ADSR control: it is a list of **segments**, each with an optional *time* slot and an
optional *level* slot, and a flag for the segment that *holds* (sustains) until note off. ADSR, AR and the multi-stage
envelopes are presets of that one control. Dragging a breakpoint changes the time and the level of its segment
together; a segment without a bound level uses a fixed level (the maximum, 0 for the last segment) and one without a
bound time half its share of the width (`EnvelopeShape`: each segment has an equal share, the segment that holds one
more for the hold).

### Behaviour of all parameter controls

- Drag vertically (a horizontal slider: horizontally) or turn the mouse wheel to change the value; with Shift in fine steps.
- Double-click to type a value (Enter applies it, Escape cancels; an invalid value keeps the editor open). A number box steps with ▲/▼ instead, so a quick second click there is another step.
- Ctrl+click resets to the default value: the data type's `optionsDefault`, otherwise the origin of a number (zero within its range) and off for a switch; a text has none.
- The name and the value are always shown. The value is the logical value of the data type (after `scale` and `shift`); its unit and number of decimals are display settings of the control (see below).
- A parameter whose value is not known yet (not received from the device, not loaded) is drawn hollow, without a value.
- An on/off parameter (a bool) on a knob, slider or number box is a number from 0 (off) to 1 (on), and a choice the number of its option (shown by its name), in steps of 1 (`ParameterInfo`); their controls are suggested to be a switch and a selector.
- A control only takes a parameter it can show (`ControlCatalog.Accepts`): a switch a bool, a selector a choice, a number box a number, the knob, slider, XY pad and envelope a value in a range (a number, a bool or a choice), a text anything. Another parameter is reported and not bound, cannot be dropped on it, and a control bound to one cannot become that kind.
- A parameter dropped on a control with more slots (an XY pad, an envelope) binds the first slot it shows that is not bound yet (`SlotNames.Shown`).
- Keyboard: Tab moves between controls, the arrow keys change the value.

### Display settings belong to the dashboard

How a value is presented is a display concern, not a data concern: the schema says what the value *is* (range,
steps, option names), the dashboard says how it is *shown*. Per control:

- `unit`: the text after the value (`ms`, `Hz`, `dB`, `%`).
- `decimals`: the number of decimals shown.
- `label`: the name shown; default the parameter's field name.
- `color`, `plateColor`, `panelColor`: other colours for this control's accent, plate and (for a section) panel (see [Colour](#colour)).

## Layout controls

Layout controls have no MIDI function: they arrange, group and label. The root of a dashboard is a layout control, so
a dashboard is a tree.

**Everything is on a grid of cells** (`GridLayout`). Every control has a **size in cells** (`size`: columns,rows); the
cell is the unit of the whole dashboard and the dashboard's cell size scales it as a whole. The root (usually a grid)
and every grid and section place their children at a **cell position** (`at`: column,row, zero based from the top left
of the grid; for a section, of the grid inside its frame). A parameter control is fitted to its cells (`CellFit`):
scaled by the side that limits it, so making it bigger makes its knob bigger, and its chrome stretched along the other
side, so its plate fills its cells and lines up with the grid (the scale follows from its size when it is first laid
out, so a value text that grows does not rescale it); a new control gets a default size per kind in which it fits
unscaled (a knob is 5×7). A section's frame and title take a fixed number of cells around its grid
(`GridLayout.SectionInset`: one on the sides and bottom, two on top), in every look. Positions and sizes are never
empty: a file that leaves them out gets them when it is opened (the controls side by side below those that have a
position), and the console says so.

When a control is moved or sized onto other controls, these are **pushed down** below it (and the controls they then
overlap, and so on); a grid or section that no longer fits its children **grows**, and pushes in its own grid in turn.

| Control | Arranges its children | Used for |
|---------|-----------------------|----------|
| Grid | on cells, each child at its position (`at`) in its size | the root of a dashboard; exact placement, e.g. to copy a device's panel layout |
| Section | as a grid, inside a frame with a title | a group of parameters: "Filter", "Amp", a device |
| Row, Column | next to / below each other (flex): `gap`, `wrap`; a child's `grow` shares the remaining space; the children have no position | a strip of controls that adapts when controls are added or removed |
| Tabs | one page at a time (the headers take two rows); each child is a page, named by its label: a grid or section; any other control put on tabs gets a grid page of its own (`ControlCatalog.PageFor`) | many parameters on a small screen; the sections of a patch |
| Spacer, Separator | — | room or a line between controls; a separator is a line along the long side of its cells (`GridLayout.IsWide`) |
| Label | — | a text on the dashboard |

A layout control can set what its children inherit:

- `device` and `base`: the device alias and the schema path that the bindings inside it are relative to. A section "Filter" with the base `AddressMap.Edit.Filter` holds knobs bound to `Cutoff` and `Resonance`. This is the same `base` a multi-slot control (the envelope) uses for its slots: one mechanism. A `base` is always a full path from the map root; bases do not stack.
- index variables: see below.
- the palette: by default the palette of its `device` (see [Colour](#colour)).

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

- A control sees the variable as the item number: the index + 1, from 1 to the number of items of the repeat it indexes, from the address map, or the repeat's own count in a schema without one (`SchemaLibrary.ItemCount`). A variable used on repeats with different item counts is reported; a variable that indexes no repeat in a binding path has no range and is reported too.
- Changing the variable resolves the bindings again: the controls that use it are pointed to the newly selected item (`ParameterValue` is re-targeted) and show its values (unknown, or from a loaded dump).
- The dashboard file holds the starting value; the selected value is state of the open dashboard.

## Visual style

Flat and functional. Every parameter control has one recognisable shape and the value is part of that shape:

- a knob is an arc from the origin to the value; the origin is zero within the range: the minimum for 0..100, the centre for a bipolar range such as -50..+50 (a `shift` that makes the minimum negative), the maximum for -50..0;
- a slider is a filled bar, a switch a lit tile, a selector a row of segments with the selected one lit;
- an envelope is its curve with breakpoints to drag.

These rules hold for every look; a look only decides how they are drawn.

### Composable controls

A control is not drawn as one piece: it is composed of layers, each with one responsibility and a narrow contract.
This keeps the control logic independent of the look.

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
- for a slider whether it runs horizontally; for a selector the option names and the selected option, for a switch on or off;
- for an envelope its breakpoints (time and level per segment, the hold segment).

A face draws the face state with the palette's brushes, nothing else: it never sees the device state, the schema or
MIDI, and it has no behaviour. The interaction stays in the control, so a knob is dragged the same way whatever face
it has. A face also tells its natural size in cells (a ring of LEDs needs more room than a flat arc); a control with
its chrome is fitted to the cells it is given (scaled, its plate filling the cells). A face with a direction (a fader)
is drawn upright and turned a quarter when its control is horizontal (`ControlOrientation`, set by the dashboard from
the ratio of the cells, given to the face in its face state), so a kit has one drawing for both. A fader also stretches
along its direction into all the room its cells give it (the chrome gives its face what the label and the value
leave), and dragging along the whole slot covers the whole range: a long fader is finer. Other faces keep their own
size, centred. Sizes in cells are at a reference cell of 24 pixels (`Face.CellPixels`); the dashboard as a whole is
scaled to its own cell size, in one place, so controls, fonts, gaps and grids scale together.

In Avalonia a control is one class per control type, a face a small control that draws itself, chosen by key from
the look, a chrome a `ControlTheme` of the frame control (also used by sections), and the palette, typography and
finish are resources. A face draws on two layers: the static parts (a track, a body, a slot: `RenderStatic`) are only
drawn again when the brushes change or the value becomes known or unknown; the value (an arc, a pointer, lit LEDs:
`RenderValue`) is drawn again on every change of the value. No control has a colour, size, font or shape of its own in
code: everything is drawn through named resources.

The resource keys every look provides (the face themes take them; the chrome templates use them too):

| Layer | Keys |
|-------|------|
| Palette | `ConsoleAccentBrush`, `ConsoleAccentTextBrush`, `ConsoleAccentDisplayBrush` (these three are derived from the accent in code, see [Colour](#colour)), `ConsoleTrackBrush`, `ConsoleBodyBrush`, `ConsoleSurfaceBrush`, `ConsolePointerBrush`, `ConsoleDisplayBrush`, `ConsoleDisplayTextBrush`, `ConsoleOutlineBrush`, `ConsoleTextBrush`, `ConsoleBackgroundBrush`, the materials `ConsolePlateBrush` (a control's plate, transparent when the controls are on the panel) and `ConsoleSectionBrush` (a section's panel) with `ConsolePlateTextBrush` (the label on the plate) (these three are tinted in code when a plate or panel has a colour) (and look specific ones for its own chrome, e.g. `ConsoleScrewBrush`) |
| Typography | `ConsoleLabelFont`, `ConsoleDisplayFont` |
| Finish | `ConsoleGlowOpacity` (0 draws no glow) |

**Kits.** A face is needed per control type: knob, slider, switch, selector, number box, text, XY pad and envelope.
Faces that belong together form a kit; a look picks a kit and may override the face of a single control type. Faces of
different kits share their drawing code where they can (one arc renderer, drawn with different strokes). The number
of faces is the real cost of this design, and it grows with the number of kits, not with the number of looks: a look
is only a combination.

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

**Picking a colour.** In *Edit* mode, a right-click on a control opens the colour chooser for the area under the
pointer, named in its title: on the face or on a section's tag or strip (the elements of a look's chrome with the
class `accentArea`) the accent, elsewhere on a parameter control its plate, elsewhere on a layout control its panel.
The property panel opens the same chooser for each area of the selected control. It offers:

- the colours of the look for the area: a few colours designed to suit it (in the module of the look; Foundry has metals and patina, steel and enamel plates, iron and painted panels; Anodized LED colours and anodized aluminium);
- tints and shades of the current colour;
- any colour (a colour view in a flyout of its own, applied with *Apply*);
- *Inherited*: takes the colour away, so the control has the colour of the cascade again;
- *For all controls of device ...*: the pick becomes the device's colour in the rig instead of the control's.

A pick applies at once. The dashboard or the rig is then changed. The file menu (☰ at the left of the toolbar) has
*Open* (*Rig…*, *Dashboard…*, *Dump…*), *Save*, *Save as* and *Close dashboard*: *Save › Dashboard* or *Rig* writes
that one, *Save › All* (Ctrl+S) writes the selected dashboard and the rig when they changed, *Save as* writes one of them to
another file (XML or JSON by its extension). A file that was not
read from disk (a new dashboard) is saved with a file picker. A changed marker (●) shows unsaved changes: of each open
dashboard next to its name in the list of dashboards, of the rig next to the RIG heading (Edit mode), of the rig or the
selected dashboard on the file menu, and on the items of *Save*. Unsaved changes are not discarded silently: *Open › Rig…* (in place of a changed rig) and *Close
dashboard* (of a changed dashboard) ask the user to confirm first. When the dashboard gives a device a colour of its own, a new rig
colour does not show on it; the console says so.

### Looks

Two ready-made looks, kept simple as the basis for tweaking:

| Look | Faces (kit) | Chrome | Palette |
|------|-------------|--------|---------|
| **Foundry** | knob: engraved track, bakelite body, brass cap; fader: dark slot, brass cap; switch: pilot lamp and lever; text: amber readout; selector: bakelite keys with brass names, lit in the accent; XY pad and envelope: amber screen, the point or curve in the accent, brass beads | a control on a brushed steel plate with four screws, an engraved label and an amber readout; a section on a cast iron panel with a tag in the device colour | iron, steel, bakelite, brass, amber |
| **Anodized** | knob: ring of LEDs, machined cap; fader: slot with an LED meter; switch: LED bar and push button; text: OLED display; selector: keys with an LED above each name; XY pad and envelope: OLED screen, the point or curve lit in the accent | a control on the panel with a printed label and an OLED readout; a section on an aluminium panel with an LED strip and a title in the device colour | dark aluminium, LEDs and OLED in the accent |

Where things are, for tweaking (in `Jacobi.Midi.Console`):

- **Modules**: each kit and the looks built on it are a **module**, a folder of its own under `Looks` (`Looks/Foundry`, `Looks/Anodized`), changed in isolation. A module has everything it offers: its faces (a file per face), its kit and its looks with their accent and swatches (`FoundryLooks`, `AnodizedLooks`), and the resources of its looks (`Foundry.axaml`). Modules need not offer the same: one may have more looks (other palettes on the same kit) or resources of its own (e.g. Foundry's `ConsoleScrewBrush`). A module finds its resources through its own assembly (`Look.ThemeOf`), so it can move to an assembly of its own later.
- **Shapes**: the faces of a module (drawn in code; shared drawing code in `Faces/FaceDrawing`), collected in the kit of the module.
- **Colours, materials, fonts and glow**: the palette, typography and finish in the resources of a look: the same keys in every look (see [Composable controls](#composable-controls)), solid colours or gradients.
- **Chrome**: the `ControlChrome` and `SectionFrame` control themes in the same resources.
- **Templates**: `Themes/Controls.axaml` puts a chrome around a face host for each control and gives every face its brushes from the palette keys (one theme for all faces, `Face.StyleKeyOverride`); it is shared by the looks.
- **Looks**: `ConsoleLooks` (in `Looks/Look.cs`) lists the looks of the modules; applying one replaces the look's resources and kit and rebuilds the dashboard. A new module is added to that list.

The accent is the device colour of the rig (or the dashboard); a look's own accent is used for controls without a
device. A dashboard has its look (`look`, by name): the console shows the selected dashboard with that look. It is
changed in the dashboard's properties in Edit mode, or in the toolbar in Play mode: both are the same edit of the
dashboard, saved with it. A dashboard without a look is shown with the first look, and gets it in Edit mode (it then changes,
which is told); a look the console does not know is told when the dashboard is opened.

## Edit and Play

Several dashboards can be open on one rig; the toolbar switches between them, opens another (+ in Edit mode: a new dashboard; or
one from a file, in the file menu) and closes one (file menu; a dashboard with unsaved changes only after the user confirms discarding them). They share the rig's device states (`RigSession`), so a parameter has one value on
every dashboard; each dashboard (`DashboardSession`) has its own index variables. A dashboard that brings its own
schemas rebuilds the schema library, and with it the device states (their values are reset; this is reported). The
console starts empty: open a rig and its dashboards (*Open*), or make a new dashboard. A GP-8 example (a rig and two
dashboards) is in `docs/examples/console` of the repository; its values come from the bulk dump of the tests
(`src/Jacobi.Midi.Tests/Schema/gp8-sysex.syx`, *Open › Dump…*).

In *Play* mode the layout is locked and only the values change. Where the values come from is chosen in the MIDI bar
(see [MIDI](#midi)).

In *Edit* mode:

- the values are locked: the controls do not change them (`DashboardEditing.IsEditing`, inherited by all controls of the dashboard), the cursor is the arrow;
- a click selects the control under the pointer (outlined); a right-click also opens the colour chooser for the part under the pointer (see [Colour](#colour)); in *Play* mode a right-click does nothing;
- Esc, or a click beside the dashboard, selects nothing: the panel shows the dashboard (its name and zoom);
- dragging a control in a grid or section moves it, dragging the handle at the bottom right of the selection sizes it, by whole cells; the control follows the pointer and is placed when it is let go (controls in the way are pushed down, grids grow); the cells of the grid the selected control is in are drawn over it, with the column and row numbers every five cells;
- dragging a control over another grid, section, row, column or tabs moves it into it; its bindings stay bound to the same parameters (`ControlMoves`: a moved layout control keeps the device and base it inherited, a binding that would point elsewhere is written again; a variable that is not around its new place is reported). A palette item dropped in a grid or section goes to the cell under the pointer, elsewhere after the other controls;
- Delete (or *Delete* in the panel) deletes the selected control and the controls inside it; the root stays;
- tabs: a click on a tab header shows its page and selects it (drop on the page to add to it, on the headers to add a page); the page shown stays shown when the dashboard is built again, and selecting a control shows the pages it is on;
- a label or separator has only an accent: it is drawn in the look's text or outline colour until it is given a colour (right-click, or the panel);
- the left and right panels are sized with the splitters between them and the dashboard (at least 180 pixels; kept while Play mode closes them).

**The rig and the palettes** (left panel). Per device of the rig its parameter tree (`ParameterNode`, from the plan of
its map root; fields whose value the schema fixes are left out), with the number of items of a repeat and the range or
options of a field, and the device's MIDI connection (a dot, see [MIDI](#midi)); below it the palettes of parameter
controls and layout controls (`ControlCatalog`). The dot before a device is its accent colour (empty without one). A
usage mark (◆) shows what the controls of the selected dashboard are bound to (`DashboardSession.UsedPaths`): a field
any of its repeat items is bound to, and fainter, a device, group or repeat with such fields inside it.

- Dragging a field onto the dashboard creates the suggested control (`ControlCatalog.Suggest`: options a selector, a `sys.bool` a switch, a string a text, a number with a range a knob, else a number box), bound to it.
- Dragging a group, a repeat or a device creates a section with its `device` and `base`, a control per field and a nested section per group, laid out by `GridLayout.Complete`.
- Dropping a field on a parameter control binds its slot (`ControlDrops`). A binding is relative to the base around it when it can be (`BindingScope.BindingOf`).
- A repeat in the path gets the index the base around it gives, else an index variable in scope that indexes the same repeat (`BindingResolution.Repeats`), else a new variable named after the repeat: on the nearest grid or section around a dropped field, on the new section of a dropped group with a number box bound to it.
- A device the dashboard does not list yet is added.
- Dragging a control from a palette creates an unbound control.

**The control panel** (right panel) edits the selected control:

- its label, position in its grid (column, row), size (columns × rows) and display settings (unit, decimals);
- its three colours (each opens the colour chooser, ✕ makes it inherited);
- its kind, with a button per kind (one single-value control into another, a grid into a section, a row into a column: `ControlCatalog.WithKind`);
- the bindings of the slots as text (`[device:]path` or `$name`), the device and base (with what is inherited as the placeholder), the index variables of a layout control, the gap and wrap of a row or column, and the segments of an envelope.

With nothing selected it shows the dashboard's name and zoom (its `cellSize` as a percentage of the default 24 pixels,
25-400%: the whole dashboard scales with it and it is saved with the dashboard), its look, and the device aliases the dashboard
uses (`DeviceAliases.Of`: its `devices`, the `device` of its controls and of their bindings), each with whether the rig
has it. Renaming an alias there renames it everywhere in the dashboard, as one edit (`DeviceAliases.Rename`): after a
device got another alias in the rig, its controls work again. Dashboards are not changed automatically when the rig
renames a device: dashboard files that are not open would need the same change anyway. The panel is made anew for every
selection, and a press on the dashboard first takes the text being typed in it, so that nothing of one control is
taken for the next.

**The device panel** (right panel, when a device is selected in the rig): alias, name, map root (one of the root groups
of the library, `SchemaLibrary.Roots`, address maps first), device values, the MIDI endpoint and UMP groups with the
device's connection and its tests (see [MIDI](#midi)), colours and its own schemas (schema files and assemblies, added
and removed there); *Remove* takes it out. The *+* of the rig adds a device for an address map of the library (the
other root groups in a submenu; named after it: `Gp8.AddressMap` is `gp8`), or from a schema file or an assembly the
user picks: it becomes a schema source of the new device, which gets a root group of the documents the file brings
(`SchemaLibrary.RootsFromFile`, an address map first), also when such a document replaces a built-in one of the same
name; that replacement holds for all devices of the rig, and is shown as information. A change of the schemas builds
the library again, which resets the values of the devices (reported); schema files and assemblies are loaded by their
path, so not in the browser. Open dashboards that use a renamed or removed alias are reported (a renamed one is
renamed in the dashboard's properties).

**Undo.** Every change is an edit (`IEdit`) in the edit history (`EditHistory`): undo (↶, Ctrl+Z) and redo (↷,
Ctrl+Y); the history is cleared when a dashboard is closed or another rig is opened. A dashboard or rig counts the
edits since it was read or saved, so undoing all of them makes it unchanged again. After an edit the dashboard is
resolved again (`DashboardSession.Refresh`: the variables keep their current item); after an edit of the rig the rig is
opened again on the same library, and the devices that stay the same (the same device, also by another alias; map root, device values) keep their state
and values (`RigSession.Open` with the previous session).

**Notifications** (below the status) list what the console tells: problems (files, schemas, devices, bindings, dumps,
MIDI) in their own colour, and information (a schema that replaces a built-in one, sizes given to a file); the header
counts both.

## MIDI

The MIDI bar (in the toolbar) shows whether the devices of the rig are connected: a dot (green: all devices online,
amber: some, red: none) with a summary; a click opens the list of the devices with the status of each.

- **Edit mode is always connected**, to choose the endpoints and test the devices.
- **Play mode** has an explicit choice of where the values come from: **Offline** (dump files: *Open › Dump…*) or **MIDI** (the devices). With MIDI, the controls of a device that is not online are disabled; *Open › Dump…* is only available offline.
- Without a MIDI backend (the browser host) the console stays offline; the bar says so.

The values are not exchanged with the devices yet: a connected device can be tested, but its values still come from
dumps (the synchronisation is on the [todo list](todo.md)).

**Connections** (`RigConnections` in Console.Core). One MIDI session and the endpoints it reaches (watched, so the list
follows the endpoints that come and go), and a `DeviceSession` of the [message exchange](../Jacobi.Midi.Device/Exchange/readme.md)
per device of the rig with an endpoint (`MessageExchangeFactory.CreateSession`, with the device's `DeviceValues` and
UMP groups). The endpoint is found by its name (see [Rig](#rig)). The status of a device (`DeviceConnection`):

| Status | Meaning |
|--------|---------|
| No endpoint | the device has no endpoint in the rig |
| Endpoint not found | no endpoint has its name; the device connects when it appears |
| Connecting | the connection is being opened |
| Online | connected |
| Lost | the connection was lost (e.g. the cable was pulled); the device connects again when its endpoint comes back |
| Failed | the connection could not be opened, or the schemas do not compile; tried again when the endpoint is added or updated |

The connections follow the rig: a device whose endpoint, groups, device values or schemas changed connects again, a
device that left the rig is disconnected. Problems are shown with the device and in the notifications; nothing is
thrown at the user.

**The device panel** picks the endpoint from the endpoints MIDI reaches (the device's own endpoint is listed too when it
is not there; ✕ disconnects the device from MIDI) and the UMP groups to send and receive on (1-16, with the names the endpoint gives its groups in that direction; the
receive group follows the send group as long as they are the same). They differ when the device is cabled to ports of
different groups, e.g. a DIN in/out pair of a MIDI 1.0 interface, or a MIDI 2.0 device with one-way function blocks. It shows the device's connection and offers two tests, which check whether a connection can be made and the
right device answers. **A test changes no value anywhere**:

- **Identity request**: a universal Identity Request to all devices on the endpoint and groups, over a connection of its own (`DeviceIdentity` in Midi.Device); the replies within a second are shown (manufacturer, family, member, revision, device id). Independent of the schemas; older devices (the GP-8) do not answer it.
- **Request data**: a request of the first parameter of the device's map, with its schemas and device values (the device sends the atomic group it is in). The data is discarded (`DeviceSession.Probe`): not the device state, not the session's state, and no listener gets it. Only when the device is online.

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
the replacement is shown as information in the notifications. A source that cannot be loaded (a missing file, an
assembly that does not load, an invalid schema) is reported and the others are still used; schemas that do not compile
are reported and the library then has no serializer.

With the library, `BindingResolver` checks a dashboard against the schemas: every device's map root must be a root
group of the schemas (an address map or not: a plain schema of groups and fields works the same; whether its values can
be sent and received is up to the Device project, not the console), every binding a field of its device's root (the
path starts at the root, e.g. `AddressMap.…`), every index variable must index a repeat of it and stay below its item
count. With the rig it also checks that the rig has the dashboard's devices with the same map roots. All problems are
reported together.

## Device state

`DeviceState` keeps the values of one device instance in a `FieldValueStore` of the Device project, by the path and
instance of the device's map plan: the identity of a resolved binding (`ParameterReference`). A parameter without a
value is unknown.

- **Reading and writing.** Controls use `TryGetValue` and `SetValue`; a value sink (`CreateSink`) and a value source (`Source`) give the values to and take them from the serializer and the message exchange.
- **Changes.** `ValueChanged` reports every value that changed, with its origin: `User`, `Device` or `Dump`. Setting the known value again reports nothing. The event is raised on the thread that changed the value; the UI marshals it.
- **Dumps.** `LoadDump` splits a dump into its SysEx messages (`SysExSplitter` of the Messages project) and decodes the data messages of the device's map for this device (`AddressMapAdapter.ReadData`); other messages are skipped and reported. *Open › Dump…* loads a `.syx` file into every device of the rig: each takes the messages that are its data. `SaveDump` writes a range of the map as the data messages the exchange would send (`AddressMapAdapter.EncodeData`): widened to atomic groups, all their values must be known.
- **Lossy values.** A value of a dump that the schema does not describe (a physical value that is not one of the options of its data type) is read as the data type's `optionsDefault` and saved as that: such bytes are not reproduced. The GP-8 bulk dump in the tests has four patches like that (`DynamicFilter.UpDown`, `Compressor.Turbo`).

## Files

The rig and the dashboards are separate files, in XML or JSON (the format follows the file extension, as with the
schemas). The examples below are made up (the device `Acme` does not exist); the GP-8 example in `docs/examples/console` is a real
one.

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
      "endpoint": { "name": "USB MIDI Interface", "sendGroup": 0, "receiveGroup": 0 },
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
- `endpoint`: the endpoint is matched by its **name** and UMP group (0-15 in the file, shown as 1-16). Endpoint ids are not portable (native Windows ids) and do not identify an instrument behind a DIN port. When no endpoint with that name is found, the device is shown as not connected and the user can pick another endpoint.
- `color`, `plateColor`, `panelColor`: the colours of the device's controls (see [Colour](#colour)).

### Dashboard

```json
{
  "format": "jacobi.midi.dashboard/1",
  "name": "Lead sound",
  "cellSize": 24,
  "look": "Foundry",
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
<dashboard format="jacobi.midi.dashboard/1" name="Lead sound" cellSize="24" look="Foundry">
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

- `devices` lists the aliases the dashboard needs with the map root they must have, and optionally colours (`color`, `plateColor`, `panelColor`) that override the rig's on this dashboard. An alias that is not in the rig, or has another map root there, is reported.
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

## Projects

The projects are in this folder, laid out like the Schema Editor's. The folder has its own `Directory.Packages.props`;
keep its Avalonia versions in sync with the Schema Editor's.

| Project | Contents |
|---------|----------|
| `Jacobi.Midi.Console.Core` | The support code without UI: rig, dashboard, controls and bindings (`BindingResolver`, `BindingScope`), the edits of Edit mode (`Editing`: history, catalog, drops, moves), their XML and JSON files (`RigFile`, `DashboardFile`, `Files`), the schema library (`Schemas`), the device state with dumps (`Devices`), an open dashboard and the values its controls follow (`Parameters`), the MIDI connections of the rig (`Connections`), the face state (`FaceState`, `ValueRange`). Tested in `Jacobi.Midi.Tests` (`Console` folder; `FakeMidi` is a MIDI backend for the tests). |
| `Jacobi.Midi.Console` | The platform independent Avalonia UI (MVVM): parameter controls (`Controls`), faces and kits (`Faces`), chromes (`Chrome`), looks (`Looks`, `Themes`), the dashboard view with the layout controls (`Dashboards`), the rig tree, the palettes, the panels of Edit mode and the MIDI bar (`ViewModels`). |
| `Jacobi.Midi.Console.Desktop` | The desktop host; the Windows build references the Windows MIDI backend, as the Schema Editor's desktop host does. |
| `Jacobi.Midi.Console.Browser` | The browser (WebAssembly) host; without MIDI, so the console works offline there. |

The namespace `Jacobi.Midi.Console` hides `System.Console` in code in a `Jacobi.Midi.*` namespace that references
these projects (e.g. the tests): write `System.Console` there.

## UI Design v2

https://claude.ai/artifact/M1HGkZK6yVEYUJKhEVzoNZ?sk=d8ie2DI0HEzm8BEo40qjnQ#play-15dd8cd198dd
