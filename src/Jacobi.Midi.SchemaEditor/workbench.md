# Reverse-Engineering Workbench

The workbench is the second mode of the Schema Editor (next to Schema mode). It helps you build a schema for a device
that has no (or poor) SysEx documentation: you load or receive dumps of the device, compare them, find out what the bytes mean,
and define the schema fields right on those bytes. While you define a field, its value in every dump is shown, read with the
schema as it would be with the finished schema.

The core technique is *differential capture*: dump the same patch repeatedly while changing one parameter on the device.
The bytes that differ between those dumps are the bytes of that parameter.

Switch between the modes with the `Schema | Workbench` switch in the toolbar. The workbench takes the whole window below it.

## Concepts

| Term | Meaning |
| --- | --- |
| **Session** | One reverse-engineering job, named by its title (e.g. `GX-700`). It holds the captures, their notes and the annotations, and is saved as a `.workbench.json` file. |
| **Capture** | One dump of the device: an ordered list of SysEx messages (F0 … F7). Loaded from a `.syx` file, pasted as hex or received from MIDI. |
| **Baseline** | The capture all others are compared with (marked ★). All highlighting is drawn on the baseline. |
| **Variant** | Any other capture. Its *change note* (free text) says what was changed on the device for this dump, e.g. `reverb level 40`. |
| **Message slot** | The position of a message in the dump (message 1, 2, …). The messages of all captures are compared slot by slot, by index. Messages are never joined. |
| **Target schema** | The schema document the workbench builds: the open document named after the session title. |
| **Unknown span** | Bytes of a message that no schema field describes yet; in the schema a `Wip_<offset>` placeholder field. |

## Layout

```text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ ☰  [Schema | Workbench]                                                             MIDI bar ● │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ REVERSE-ENGINEERING SESSION  GX-700 •                          New…  Open…  Save  Save as…     │
├──────────────────────┬────────────────────────────────────────────────┬────────────────────────┤
│ CAPTURES             │ [Find value: 70 ] Find ◀ ▶  2 matches… Hex|Dec │ SELECTION              │
│ Load .syx  Paste hex │ legend                                         │ slot 1 · 11 (1 byte)   │
│ Receive              │ ┌ Slot 1 · 15 bytes · Message1: 1 of 13 … ───┐ │ ┌ Field Reverb ──────┐ │
│ ★ patch1             │ 0000 F0 41 00 79 12 00 00 00  00 01 10 46 …    │ │ Name / Data type   │ │
│ ● reverb 40   2 B Δ  │                                    ‾‾          │ │ Processor / Fixed  │ │
│ ─────────────────────│ ┌ Slot 2 · 19 bytes ─────────────────────────┐ │ │ VALUES             │ │
│ SLOTS                │ …                                              │ │ patch1         32  │ │
│ Slot 1 · 15 bytes  Δ │────────────────────────────────────────────────│ │ reverb 40      40  │ │
│ Slot 2 · 19 bytes    │ INTERPRET  slot 1 · 11 (1 byte)                │ │ Apply  Remove      │ │
│  probably the comp…  │            patch1     reverb 40                │ │ OTHER SLOTS        │ │
│                      │ uint8      32         40                       │ └────────────────────┘ │
│                      │ int8 …                                         │ SLOT 1 NOTE / NOTE …   │
├──────────────────────┴────────────────────────────────────────────────┴────────────────────────┤
│ status                                                                                         │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

The three columns, the captures and slots lists, and the byte view and interpret strip are separated by splitters;
each panel keeps a minimum size.

## Getting started

1. Switch to **Workbench** and choose **New session…**. Type the title, e.g. the device name `GX-700`.
   The title is also the name of the schema the workbench builds.
2. Add the first dump with **Load .syx…**, **Paste hex…** or **Receive**. The first capture becomes the baseline.
3. Change one parameter on the device, dump again, and add that capture. Type what you changed in its change note.
4. The bytes that differ light up in the byte view. Select them, look at the interpret strip, and define a field.
5. Repeat for the next parameter. **Save** the session to continue later.

## Captures

The **Captures** list (top left) shows one row per capture: its label, the number of messages and bytes, and a badge with
the number of bytes that differ from the baseline (`2 B Δ`).

- **Load .syx…** reads a SysEx file; it is split into messages at F0 … F7.
- **Paste hex…** takes hex text: bytes separated by spaces, commas or nothing (`F0 41 … F7`, `F04110F7`), with or without `0x`.
- **Receive** collects the SysEx7 messages that arrive on the connection and group of the MIDI bar (start the connection first).
  The dump ends when no message arrives for half a second, so a dump of several messages becomes one capture.
  Pressing the button again ends the dump at once.
- **Set as baseline** makes another capture the one everything is compared with.
- **Hide** leaves a capture out of the comparison without removing it (e.g. a dump taken by mistake).
- **Remove** deletes the capture from the session (after confirmation).
- Type the **change note** of a variant in its row. Variants are named by their change note in the interpret strip and the field values.

Selecting a variant **focuses** it: the byte view highlights only its differences, and the value finder searches it.
**Highlight all variants** (right panel) clears the focus.

## Slots

The **Slots** list (bottom left) shows the messages of the baseline. `Δ` marks a slot in which a visible variant differs:
it tells you which message changed in a dump of many messages. A different message length or a missing message is shown
in the slot's text (`length 77 → 79`, `missing in 1`).

Selecting a slot scrolls the byte view to it and selects its first data byte.

A **slot note** describes what a message is, e.g. `probably the compressor`. Type it in the right panel (**Slot n note**)
for the slot of the selection. It is shown under the slot in the list and in the slot header of the byte view.

## Byte view

The byte view shows the messages of the baseline, one block per slot, 16 bytes per row, with an offset column and an ASCII
column. F0 and F7 are dimmed: they are the SysEx framing. Offsets count from the start of the message (F0 is offset 0).

What is drawn on the bytes:

| Mark | Meaning |
| --- | --- |
| Orange background | The byte differs in a variant. The stronger the colour, the more variants differ (or: the focused variant differs). |
| Hatched | The byte probably holds no parameter but is derived from the others, e.g. a checksum: it differs in every variant that changed the message, while those variants changed different bytes otherwise. |
| Dots under the value | The bits that changed (most significant left). Useful for bytes that hold several switches. |
| Bar under the bytes | The bytes are described by a schema field (neighbouring fields alternate in colour). |
| Corner mark | The byte has a note. |
| Dashed yellow outline | A match of the value finder. |
| Outlined | The selection (also in the ASCII column). |

The slot header shows the slot number, the message length, the root of the target schema with the number of bytes it
describes, and the slot note.

The **tooltip** of a byte shows its offset, its value (hex and decimal) in the baseline and every visible variant, the field
or unknown span it belongs to and its notes.

The **Hex | Decimal** switch shows the bytes and offsets in decimal: most devices show parameter values in decimal on their display.

### Selecting

- Click a byte (in the hex grid or the ASCII column), drag or Shift+click to select a range.
- Double-click selects the whole field or unknown span under the pointer.
- Arrow keys move the selection, Shift+arrows extend it, Home/End go to the start/end of the row.
- Tab / Shift+Tab jump to the next/previous byte that differs (skipping derived bytes such as checksums), across the slots.

The selection is always within one slot. It drives the interpret strip and the field panel.

### Notes on bytes

A note on the selected bytes (right panel, **Note on the bytes**) records what you think they are before you define a
field, e.g. `tone A/B?`. Clear the text to remove the note. Notes are kept in the session, not in the schema.

## Finding a value

You just set sustain to 70 on the device: where is 70 in the dump? Type the value in the find box above the byte view and
press Enter (Escape clears it).

- `70` and `-12` are decimal, `0x46`, `$46` and `46h` are hex, and `46 00` is a sequence of hex bytes.
- The value is found as one byte (also as a 7-bit signed byte for a negative value), as two 7-bit bytes (MSB or LSB first)
  for values above 127, and as a pair of nibbles for values from 16 to 255.
- The search looks in the focused variant (where the value you just set is), or in the baseline when no variant is focused.
- Matches in bytes that differ from the baseline come first; the first match is selected. ◀ and ▶ step through the matches.
  The status tells how many matches there are and how the selected one holds the value.

Short values occur often; the matches in changed bytes are the ones to look at first.

## Interpret strip

The interpret strip (below the byte view) shows the selected bytes read in the common ways a device stores a value,
with a column per capture (the baseline first):

| Selection | Rows |
| --- | --- |
| 1 byte | `uint8`, `int8`, `int7 (7-bit signed)`, `bits` |
| 2 or 4 bytes | `uint16`/`uint32` big and little endian |
| 2 to 4 bytes | `7-bit, MSB first`, `7-bit, LSB first` (packed 7-bit data bytes) |
| 2 to 8 bytes | `nibbles, high first`, `nibbles, low first` |
| any | `ascii` (first when all bytes are printable), `hex` (last) |

Comparing the columns with the change notes tells you which interpretation is right. Clicking a row sets the data type
(and processor) of the field in the field panel, for `uint8`, `int8`, `7-bit, MSB first`, `ascii` and `hex`.

## Building the schema

### The target schema

The workbench builds the open schema document named after the session title. When there is none, the first field you define
creates it (an XML document, like the device schemas in this repository). To continue an existing schema, open it in Schema
mode: the workbench finds it by name, also without the saved session. The line under the field panel tells whether the
schema is open and how many slots it describes.

Everything the workbench changes is a normal edit of the document: Schema mode shows it, and **Save** (☰ menu) writes it.
Edits made in Schema mode are picked up when you switch back to the workbench.

### How a message is described

Each slot gets a root group `Message<n>` (`Message1` for slot 1) with the `midi.sysex` processor for the F0/F7 framing.
The schema must describe a message from start to end, or it cannot be read. So the bytes that are not known yet are
described by placeholder fields: `sys.bytes` fields with a fixed byte length, named `Wip_<offset>`.

Defining a field splits a placeholder; removing it merges the bytes back:

```text
Wip_1 (13 bytes)  →  define Reverb at 11  →  Wip_1 (10), Reverb (1), Wip_12 (2)
```

Fields made by the workbench have a fixed byte length, and the workbench computes the byte positions of the fields from
those lengths. A field of unknown size (e.g. without a byte length, added in Schema mode) stops the layout: the bytes after
it are not shown as described, and the field panel says why.

### The field panel

The field panel (right) shows what can be done with the selection:

- **Define a field** — the selection is inside one unknown span (or in a slot that has no root yet). Enter the name; a
  data type is suggested (`sys.uint8` for 1 byte, `sys.uint16` with `ref:Midi.sysexbyte` for 2, `sys.string` with
  `ref:Midi.string` for printable text, `sys.bytes` otherwise). **Define field** adds it.
- **Field &lt;name&gt;** — the selection starts in a field. **Apply** changes its name, data type, processor and fixed value;
  **Remove field** turns its bytes back into an unknown span.
- Nothing can be done for F0/F7, for a selection over several spans, or for bytes after a field of unknown size; the panel says so.

The fields:

- **Data type** — any registered data type or a data type def of the schema.
- **Processor** (optional) — e.g. `ref:Midi.sysexbyte` (a value in 7-bit data bytes) or `ref:Midi.string` (ASCII text).
  A `ref:Midi.*` processor adds the include of the `Midi` schema to the document.
- **Fixed value** — the field always holds this value, e.g. the manufacturer id. Checking it fills in the value of the
  baseline (e.g. `65` for 41H). A capture with another value there gets a read problem.

**Values** shows the field's value in the baseline and every visible variant, while you edit: the field is tried on a copy
of the schema, and every capture is read with it, the same way *Match* does. A capture that cannot be read (completely)
shows the problem under its value. Nothing is changed in the document until you press **Define field** or **Apply**.

### The same field in other slots

Many fields are at the same position in every message, such as the manufacturer id. **Other slots** (in the field panel)
defines the field at the same position in the other slots, where those bytes are still unknown:

- while defining: **Define here and in n other slots** defines it for the selection and in the other slots at once;
- with a field selected: **Define it in n other slots**.

With **Only where the bytes are the same** (the default) only the slots with the same bytes there get the field, e.g. `41H`
for `RolandId`. Turn it off for a field whose value differs per message, such as an address. A slot without a root gets one.

## Sessions

**New…**, **Open…**, **Save** and **Save as…** are in the session header. A session file (`<title>.workbench.json`) is
self-contained: the captures are stored in it as hex, with their labels, change notes and hidden flags, the baseline,
the notes on bytes and the slot notes. The schema is saved separately, as a normal schema document.

Starting a new session or opening another one asks before unsaved changes are discarded. A `•` after the title marks
unsaved changes.

## How it works (for developers)

The code is in `Jacobi.Midi.SchemaEditor/Workbench/`; the tests are in `Jacobi.Midi.Tests/SchemaEditor/Workbench*Tests.cs`.

| Type | Responsibility |
| --- | --- |
| `WorkbenchViewModel` | The session: captures, baseline, focus, comparison, selection, notes, find, receive, save/open. |
| `WorkbenchFieldViewModel` | The field panel: define, apply, remove, copy to other slots, and the preview of the values. |
| `WorkbenchView`, `HexView` | The view; `HexView` draws the byte view itself (only the visible rows) and handles selection. |
| `Capture`, `ByteRange`, `Annotation` | A dump, a byte range in a slot, a note on bytes. |
| `CaptureComparison`, `SlotComparison` | The comparison of the visible variants with the baseline, per slot and byte. |
| `ByteInterpretations`, `ValueFinder` | The interpret strip rows, and finding a value. |
| `WorkbenchSchema`, `MessageLayout`, `FieldSpan` | Building the target schema: roots, placeholders, define/remove fields, the layout of the fields on the bytes. |
| `WorkbenchReader` | Reads the captures of a slot with the (draft) schema, through `SchemaInstantiator` like *Match*. |
| `WorkbenchSessionFile` | The session file (source generated JSON). |
| `SysExSplitter`, `HexText` | Splitting a byte stream into messages; hex text. |

The main window provides file access (`WorkbenchStorage`) and access to the schema documents (`WorkbenchSchemaHost`:
find, create and compile documents, and the processors of the opened assemblies).

The serializer does not report which input bytes a field was read from, so the workbench computes the byte positions from
the byte lengths of the fields (see the to-do list for a read trace in Jacobi.BinarySerializer).

## Not yet available

Ideas from the design that are not built yet (see also `todo.md`):

- **Frame proposal**: propose the message frame from the captures — the common header as fixed fields, the checksum
  (trying `roland.checksum`, `yamaha.checksum`, `sys.crc` over the plausible ranges) and an address field; or start from an
  included vendor schema (e.g. `Roland`, message `DT1`).
- **Slots with the same layout** as a repeat or a shared node def, and a choice selected by the address.
- **Grouping fields**, e.g. a checksum scope or a processor on a group.
- **Checking the interpretations against the noted values** (✓ in the interpret strip), and **sweeps**: a series of
  variants of one parameter shown as a table and a plot of raw against noted value, proposing the data type and
  `Scale`/`Shift`. Change notes are free text, so this needs a way to enter the noted value.
- **Request** the dump with the schema's request message before receiving, and **auto-capture** every received dump.
- Comparing messages of **different lengths** by aligning them, instead of byte by byte up to the shorter one.
- **Toggling the layers** of the byte view, **bit fields** picked in a bit grid, and keyboard shortcuts such as
  `Ctrl+Tab` (next unknown span) and `Ctrl+1…9` (focus a variant).
