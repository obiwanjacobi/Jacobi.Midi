# Schema Editor - To Do

Known gaps and loose ends, collected while building the editor.

## Node editor

- [x] Editing `description` or `sample` on the Properties tab does not refresh the staged values on the General tab; pressing *Apply* there writes the old values back. Fixed: the description and sample value fields are removed from the General tab; both are edited as properties only.
- [ ] The node def, count, selected index, member node def, byte size/length/offset and value fields are plain text boxes; offer the document's node defs (and the reachable `ref:` paths) as suggestions.
- [x] `SchemaWorkspaceDocument.ApplyNodeEdits` takes the type-specific properties as an optional trailing `NodeFacets` parameter; fold name, data type, description and sample into the staged model so there is one apply path. Fixed: there is no apply step any more; `NodeFacets` edits the name, data type and type-specific values of the node live.
- [ ] Remove contiguous match selection. send separate messages for gaps.
- [ ] Value processors (repeat/choice) are not shown or editable; to be redesigned.
- [ ] The data type options and the field Value validation (`DataTypeValueValidator`) only know the registered types and the data type defs of the edited document; data type defs of included schemas are not listed and their types count as custom (not validated). Resolve the includes' documents to fix this.
- [x] The built-in data type descriptors have no `Min`/`Max`, so the Value range warning only applies to data type defs that define them. Fixed: the validation falls back to the range of the descriptor's `ClrType`.
- [x] The Value validation compares the typed value to the `Options` keys (physical values) and to `Min`/`Max` (logical values) without applying `Scale` and `Shift`; decide whether options should be matched on the physical value after scaling.
- [x] *Delete* has no confirmation. Fixed: *Delete* asks for confirmation (`ConfirmViewModel`).
- [ ] There is no undo.
- [ ] The Add menu has no keyboard shortcuts (e.g. Ctrl+S for Save in the ☰ menu).
- [ ] Moving a node (Arrange: up/down, into/out of a group) or changing its type does not update the `ref:` paths that point at it or at its members (count, byte length/size, selected index, field values); they may no longer resolve.
- [ ] A node that is moved to another parent or whose type is changed is written as a new element in an XML source (`XmlSchemaSource` matches nodes by identity within their parent): the comments inside and before it are lost. Reordering within the same parent keeps them.
- [ ] Change type only covers group, repeat and choice; a field cannot be changed into a group-like node (or back).
- [ ] A node cannot be moved between the document members and the node defs (e.g. turn a member into a node def), nor to another document.

## Effective processors and properties

- [ ] Resolution only uses definitions in the edited document; definitions in included schemas are reported as unresolved. Loading the includes (paths / embedded Device & Roland schemas) would give the real result.
- [ ] Editing a node def or processor def does not refresh the Processors/Properties tabs of a node that uses it until that node is selected again.
- [ ] Data type defs and processor defs only show their own entries (no `basedOn` inheritance shown).
- [ ] Since processor names lost the `ref:` prefix, a processor `namespace.name` can be a processor def of an included schema (`midi.sysexbyte`, a def in `Midi.xml`) or a processor key (`midi.sysex`); both are used as processor (`<processor name="..."/>`) with the same namespace. `EffectiveNodeResolver` tells them apart with the processor keys of the `ProcessorCatalog` (`IsProcessorKey`): a name that no loaded processor factory knows is a processor def of another schema. Without a catalog nothing is reported. The properties of processor defs of included schemas are still not merged (see the item above).
- [ ] Use the Jacobi.BinarySerializer objects as much as possible instead of re-implementing its rules in the editor: e.g. resolve the effective processors and properties through a compiled `SchemaSet` of the edited document and its includes (`SchemaProcessorRef.Definition` / `Key`, `EffectiveProperties`), rather than `EffectiveNodeResolver` looking up names itself.

## Document items

- [ ] Renaming a node def, data type def or processor def does not update the nodes and processor refs that reference it.
- [ ] Data type def options: when two rows have the same value the last label silently wins; there is no warning.

## Schema tree

- [ ] The Document view categories hold the document collection instances from when the document was loaded; replacing a collection (instead of changing its contents) is not reflected.
- [ ] Nodes can only be arranged with commands (Arrange menu, context menu, Alt+arrow keys); drag & drop in the schema tree is not supported.
- [ ] The groups of node defs (below the fixed Node Defs item) offer *Instantiate…* and *Match…* like the members; whether a node def instantiates and serializes on its own is not verified.

## Reverse-engineering workbench

- [x] Phase 1 (Compare): `Schema | Workbench` mode switch, session (title, save/open), captures from .syx / pasted hex / Receive, baseline and variants with change notes, message slots, byte view (`HexView`) with difference, changed-bits, derived and annotation marks, interpret strip, annotations.
- [ ] A message slot of a variant with a different length is compared byte by byte up to the shorter length; the proposal's alignment on the longest common subsequence is not done.
- [ ] The layers of the byte view cannot be toggled; there is only a legend.
- [ ] The interpret strip does not check the rows against the change notes (they are free text, not parsed) and there are no sweeps yet. Needs a way to enter the noted value.
- [ ] Keyboard: `Ctrl+Tab` (next unknown span), `Ctrl+1…9` (focus a variant), `Enter`/`F2` (define/rename a field) are not implemented; arrows, Shift+arrows, Home/End and Tab/Shift+Tab are.
- [ ] Receive only assembles SysEx7 UMPs (`SysEx7Assembler`), like Match; SysEx8 is not received.
- [x] The selection is also shown in the ASCII column (and bytes can be selected there); the panels are sized with splitters (with a minimum size).
- [x] Phase 2 (Build), first part: one root per message slot (`Message<n>`, `midi.sysex` framing) with `Wip_<offset>` placeholders, Define / Apply / Remove field, a live preview of the field's value in every capture (on a copy of the schema), field coverage under the bytes, double-click selects a field or unknown span, interpret strip rows set the data type.
- [x] Decimal display (Hex/Decimal switch: the bytes and offsets of the byte view; the tooltip shows both), find a value (decimal, hex or hex bytes; as one byte, 7-bit packed or nibbles; in the focused variant or the baseline, changed bytes first) and notes on slots (in the session, the slots list and the slot headers).
- [x] Moving the slots splitter moved the slots list down (the splitter resized the header row); fixed values for fields (checked by the reader in every capture); define the selected field at the same position in the other slots (where the bytes are the same, or where they are unknown).
- [ ] Find only looks in the focused variant (or the baseline); finding in all captures, and finding a value in the slots that changed only, may help.
- [ ] Frame proposal: header constants, checksum detection (`roland.checksum`, `yamaha.checksum`, `sys.crc`) and address fields are not proposed yet.
- [ ] A slot is matched to its root by name (`Message<n>`); a renamed root is not found. Matching by reading (the root that reads the baseline message) would be more robust.
- [ ] Slots with the same layout cannot be turned into a repeat or a shared node def yet; fields cannot be grouped (e.g. a checksum scope) from the workbench.
- [ ] The field preview copies and compiles the schema on every change of the name, data type or processor; fine for small schemas, may need a debounce for large ones.
- [ ] The field panel edits one processor per field: *Apply* replaces all processors of the field with that one, so a field that got more processors in Schema mode loses the others.
- [ ] `WorkbenchSchema.EnsureInclude` only adds an include of `WorkbenchSchema.MidiSchema` for a processor named `Midi.*` (without the `ref:` prefix a processor name does not tell a schema from a processor namespace). Review which references need an include: node def and data type values, and processor def names of another document (the `SchemaSetBuilder` only treats a namespace as a document when that document is in the schema set; otherwise `Midi.sysexbyte` is taken as a processor key).

- [ ] The serializer does not report which input bytes a field was read from (`IValueSink.SetField` gets no byte position), so the workbench computes positions itself from the schema. That fails for fields of variable size and for fields below processors that transform the bytes (7-bit packing, nibbles). Possible solution: a read trace in Jacobi.BinarySerializer (e.g. a service via `ValueContext.Services`), with transforming processors mapping positions back to the input. See `workbench.md`.

## Build & test

- [ ] `dotnet test` fails on the .NET 11 SDK: the test project uses Microsoft.Testing.Platform and needs the opt-in in a `global.json`. Run `Jacobi.Midi.Tests.exe` directly until then.

## Instantiate dialog

- [ ] Field values are free text. They get a (non-blocking) warning from `DataTypeValueValidator`, but data type def `options` are not offered as a list.
- [ ] Field value validation (`DataTypeValueValidator`) does not take the processors of the field into account: a value processor such as `sys.map` makes the logical value another type than the data type (e.g. `Gp8.upDown`: `Up`/`Down` on a `sys.uint8`), so a valid value gets a warning. *Save As*/*Send* hand such a value to the processors (`InstanceValueSource`); the validation should do the same, or validate against the logical values (the map's results).
- [x] The confirmed instance (`MainViewModel.LastInstance`) is not used yet; it should implement `IValueSource`/`IOpenChoiceSource` so it can be written by the serializer. Fixed: `LastInstance` is removed; `InstanceValueSource` writes the instance for *Save As* and *Send*.
- [ ] Processors are not checked: only includes (open documents) and schema compilation are verified before the dialog opens.
- [ ] Match (deserialize): the body of an open choice must be selected by the user before matching; it is not detected from the data (e.g. by trying each conforming body).
- [ ] Match: fields with a constant or `ref:`/`pub:` value are not reported to the value model by the reader (`valueFieldsUseModel` is off), so derived fields (lengths, checksums) stay empty after a match; *Save As*/*Send* derive them again.
- [ ] Match: *Receive* only assembles SysEx7 UMPs (`SysEx7Assembler`); SysEx8 is not received.
- [ ] Address map, phase 2: select individual fields to send (checkboxes on fields). Needs data messages that carry any aligned block (see the Device todo): `AddressMap.CreateRange` covers one field, but `AddressMap.CreateData`/`Decode` only handle one whole group, so one field of a larger group throws `NotSupportedException`; the fix starts in Jacobi.BinarySerializer (an open choice that targets a range). Use case: one parameter in the GX-700 `IndividualTemporary` area. A selection that cuts through an atomic group is widened to the whole group with a note; offline there is no read-modify-write, so the values of the whole group come from the instance.
- [ ] Fields with `midi.role="address"` or `midi.role="size"` (e.g. the `Address` of a Roland RQ1) take the linear value (`sysex32`: 4 x 7 bits), while the schema writes addresses in the wire form (`midi.address="0x04-00-00-00"`); entering the wire bytes as one number (`0x04000000`) does not fit. Accept and show the wire form for these fields, parsed and formatted by `AddressSpace`, for every address width (16/24/32 bits).
- [ ] Address map, *Send*: the messages are sent over the connection of the MIDI bar with the message interval as pause; replies of an acknowledged device (ack/nak) are not handled. To be decided: send through `MessageExchange` instead.
- [ ] Open choice bodies mismatch (also applies to Match: `InstanceValueSink` reads the selected body the same way): `SchemaInstantiator` offers groups at any depth that declare the member node def as bodies of an open choice, but the serializer (`ExecutionPlan.BuildOpenChoiceBodies`) only accepts roots. *Save As*/*Send* with a nested body fails with "is not a known group that conforms to the nodeDef". Either offer only roots, or let the serializer accept nested groups (possibly through the `FromTarget` of `OpenChoiceSourceResult`).
- [ ] `SchemaInstantiator.FindConforming` searches the authored documents (`EditDocument`) for the bodies of an open choice and then looks each one up in the compiled schema (`FindCompiled`). Compiled nodes now keep their (qualified) `NodeDef` (`SchemaNode.IsNodeDefInstance` marks the nodes merged with their node def), so the bodies can be found in the compiled `SchemaSet` directly, as `ExecutionPlan.BuildOpenChoiceBodies` does for roots. Decide together with the item above which groups (roots only, or nested groups) are bodies.
