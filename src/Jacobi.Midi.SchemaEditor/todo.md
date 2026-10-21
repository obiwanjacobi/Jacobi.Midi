# Schema Editor - To Do

Known gaps and loose ends, collected while building the editor.

## Node editor

- [x] Editing `description` or `sample` on the Properties tab does not refresh the staged values on the General tab; pressing *Apply* there writes the old values back. Fixed: the description and sample value fields are removed from the General tab; both are edited as properties only.
- [ ] The node def, count, selected index, member node def, byte size/length/offset and value fields are plain text boxes; offer the document's node defs (and the reachable `ref:` paths) as suggestions.
- [x] `SchemaWorkspaceDocument.ApplyNodeEdits` takes the type-specific properties as an optional trailing `NodeFacets` parameter; fold name, data type, description and sample into the staged model so there is one apply path. Fixed: there is no apply step any more; `NodeFacets` edits the name, data type and type-specific values of the node live.
- [ ] Value processors (repeat/choice) are not shown or editable; to be redesigned.
- [ ] The data type options and the field Value validation (`DataTypeValueValidator`) only know the registered types and the data type defs of the edited document; data type defs of included schemas are not listed and their types count as custom (not validated). Resolve the includes' documents to fix this.
- [x] The built-in data type descriptors have no `Min`/`Max`, so the Value range warning only applies to data type defs that define them. Fixed: the validation falls back to the range of the descriptor's `ClrType`.
- [ ] The Value validation compares the typed value to the `Options` keys (physical values) and to `Min`/`Max` (logical values) without applying `Scale` and `Shift`; decide whether options should be matched on the physical value after scaling.
- [x] *Delete* has no confirmation. Fixed: *Delete* asks for confirmation (`ConfirmViewModel`).
- [ ] There is no undo.
- [ ] The Add menu has no keyboard shortcuts (e.g. Ctrl+S for Save in the ☰ menu).

## Effective processors and properties

- [ ] Resolution only uses definitions in the edited document; definitions in included schemas are reported as unresolved. Loading the includes (paths / embedded Device & Roland schemas) would give the real result.
- [ ] Editing a node def or processor def does not refresh the Processors/Properties tabs of a node that uses it until that node is selected again.
- [ ] Data type defs and processor defs only show their own entries (no `basedOn` inheritance shown).

## Document items

- [ ] Renaming a node def, data type def or processor def does not update the nodes and processor refs that reference it.
- [ ] Data type def options: when two rows have the same value the last label silently wins; there is no warning.

## Schema tree

- [ ] The Document view categories hold the document collection instances from when the document was loaded; replacing a collection (instead of changing its contents) is not reflected.

## Build & test

- [ ] `dotnet test` fails on the .NET 11 SDK: the test project uses Microsoft.Testing.Platform and needs the opt-in in a `global.json`. Run `Jacobi.Midi.Tests.exe` directly until then.

## Instantiate dialog

- [ ] Field values are free text. They get a (non-blocking) warning from `DataTypeValueValidator`, but data type def `options` are not offered as a list.
- [x] The confirmed instance (`MainViewModel.LastInstance`) is not used yet; it should implement `IValueSource`/`IOpenChoiceSource` so it can be written by the serializer. Fixed: `LastInstance` is removed; `InstanceValueSource` writes the instance for *Save As* and *Send*.
- [ ] Processors are not checked: only includes (open documents) and schema compilation are verified before the dialog opens.
- [ ] Match (deserialize): the body of an open choice must be selected by the user before matching; it is not detected from the data (e.g. by trying each conforming body).
- [ ] Match: fields with a constant or `ref:`/`pub:` value are not reported to the value model by the reader (`valueFieldsUseModel` is off), so derived fields (lengths, checksums) stay empty after a match; *Save As*/*Send* derive them again.
- [ ] Match: *Receive* only assembles SysEx7 UMPs (`SysEx7Assembler`); SysEx8 is not received.
- [ ] Open choice bodies mismatch (also applies to Match: `InstanceValueSink` reads the selected body the same way): `SchemaInstantiator` offers groups at any depth that declare the member node def as bodies of an open choice, but the serializer (`ExecutionPlan.BuildOpenChoiceBodies`) only accepts roots. *Save As*/*Send* with a nested body fails with "is not a known group that conforms to the nodeDef". Either offer only roots, or let the serializer accept nested groups (possibly through the `Target` of `OpenChoiceSourceResult`).
