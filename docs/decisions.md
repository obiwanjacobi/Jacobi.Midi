# User Design decisions

The user design decisions made for the project, per topic: the issue, the decided direction and briefly why. The code
is the source of truth for how a decision was implemented; this file keeps the *why*.

## Following node references in the Schema Editor (2026-11-13)

Issue: node def names and `ref:` paths in the node editor are plain text; finding the node they refer to means
searching the schema tree by hand.

1. **A button next to the property follows the reference** and selects the node it refers to. First for node def names
   (NodeDef, MemberNodeDef) and `ref:` paths; data type defs, processor defs, `pub:` and *Back* come later (see the
   Schema Editor todo). *Why:* a button is the most obvious for the user; start simple.
2. **The lookup is editor-side, on `SchemaSetWorkspace`, with the serializer's rules** (`SchemaName`,
   `SchemaNodeRef.TryParse`/`Resolve`). Not on the compiled `SchemaSet`. *Why:* the schema being edited often does not
   compile, and the compiled nodes are merged copies that do not lead back to the `EditNode` the tree shows.
   `SchemaSetWorkspace` already decides which loaded schema (open document or embedded schema) answers to a name.
3. **A reference into an embedded schema is followed too**: the *Assemblies* tab is shown with the node selected.
   *Why:* follow the breadcrumbs; *Back* will lead the way back later.
4. **A *Back* button returns to the node a reference was followed from** (navigation history, 2026-11-13). Only a
   followed reference (→) is recorded, not every selection; there is no *Forward* and no keyboard shortcut (yet). A node
   that is no longer in a loaded schema (deleted, its document closed) is skipped and the status says so.
   *Why:* start with the known need; a button is the most obvious for the user.

## Included nodes of an address map in the Schema Editor (2026-11-13)

Issue: the editor kept the included nodes of an address map body one contiguous run (`KeepContiguous`): including a
node also included the nodes in between, leaving one out also left out everything after it. That did not work well.

1. **The included nodes need not be adjacent; a left-out node stays left out.** Each included node is a range of the
   transfer; the map splits it (`IMessageMap.Split`), so a gap is never sent and the messages conform to the schema
   (atomic groups, cuts, maximum size). *Why:* the map already splits; the editor only says what is included.
2. **Adjacent included nodes are not joined into one message** (for now). *Why:* a few extra messages that conform
   to the schema are no problem; keep it simple.
3. **The editor uses the message API (`MessageMaps`), not the message exchange.** *Why:* the editor describes and
   writes the messages; the exchange belongs to a device session.
4. **The editor waits a fixed 500 ms between the messages it sends** (`SchemaInstantiator.MessageInterval`), instead of
   the `messageInterval` of the device's exchange file; the editor does not load the exchange files. *Why:* follows
   from 3; a generous pause suits any device.
5. **A match that is not read through a message map suggests where it is** (always, also when the read succeeds):
   when a map recognises the bytes as one of its data messages, a warning names the data message and the root it
   carries (e.g. a DT1 matched in the `Gx700.AddressMap` root itself). Midi.Device finds them with
   `MessageMaps.FindDataMessage(sysEx, device)`. *Why:* matching in the address map root instead of the data message is
   an easy mistake, and reading raw bytes can also "succeed" with nonsense values; identifying a message is useful
   beyond the editor (e.g. the console).

## Saving a new rig and a new dashboard (2026-11-12)

Issue: *Save* with a new rig and a new dashboard shows two save dialogs in a row; it is not clear which is which, nor
that two files are written.

1. **Before the two dialogs, a message explains that the rig and the dashboard are separate files, and the order:
   first the rig, then the dashboard** (it refers to where the rig is saved); *Cancel* saves nothing. *Why:* the
   user typically keeps a few central rigs for many dashboards, so the rig is not placed next to the dashboard
   automatically; a message is the simplest step for now.
2. **A new rig with an already saved dashboard gets no message and is not placed next to the dashboard.** *Why:* only
   the two dialogs in a row are confusing; the rig's place is the user's choice.

## Collapsible sections in the console (2026-11-12)

Issue: a section always shows all its controls; the user wants to fold one away in Play mode.

1. **`collapsible` is a setting of a section (default off), saved in the dashboard; collapsing is Play mode only.**
   A click on the title collapses it to its title and expands it again. *Why:* an option per section, as asked; Edit
   mode always shows the controls to edit.
2. **In a grid the controls below a collapsed section move up, in Play mode only** (`GridLayout.ShiftsUp`): a control
   moves up as far as every control directly above it moved up and shrank, the least of those. A stack closes up,
   the gaps stay, nothing comes to overlap; the positions in the dashboard do not change. In a row or column the
   others close up by themselves. *Why:* the dashboard's root is a grid, and closing its main groups was the reason
   for the feature. (First decided as "the cells stay taken"; changed the same day.)
3. **Collapsed or not is not saved:** a dashboard opens with its sections expanded. *Why:* it is a view state.
4. **The grid around a collapsed section keeps its size** (also a section around it: it does not shrink, the space is
   at its bottom); every section can be collapsible, not only those on the root. *Why:* passing the shrink up goes
   through every kind of parent (grid, row, column, tabs) and is not needed yet.

## No schema sources on a rig device (2026-11-12)

Issue: a device of the rig had sources of its own (schema files and assemblies, `DeviceInstance.Sources`), added in
its panel. A device is made from a root group the library already has, so they were not needed.

1. **A device has no sources; the rig (and the dashboard) list them.** `DeviceInstance.Sources` and the device panel's
   *Its own schemas* are removed; a `schema` or `assembly` in a device of a rig file is reported as an error, not
   ignored. *Why:* one place that decides what is loaded (see *A dashboard refers to its rig*); exchange files follow
   the same sources (decision 11 of *Message exchange in the console*).

## The label and the selector's options in the console (2026-11-12)

Issue: the size of a control's content (its face) followed from the length of its label and, for a selector, from
the number of options, so controls of the same kind and plate size looked different.

1. **The label never sizes the control**; a label longer than the plate is cut off ("…") and shown in full in a tip.
   Splitting it over more lines was dropped: the extra rows would size the face again (in height).
2. **The selector's presentation (buttons or list) is a setting of the control**, in its display settings, without
   an *Auto* that picks one by the number of options. *Why:* an automatic choice is almost never what is wanted.
3. **The buttons of all selectors have the same size**, whatever their number; more buttons than fit in a row go on
   to the next row. *Why:* two selectors should look alike, not differ by their number of options.
4. **The size of the face is a setting of every parameter control**, apart from the size of its plate (cells): the
   face is no longer fitted (scaled) to the plate. *Why:* controls of the same kind and setting look alike, whatever
   their plate.
   The setting sizes the face only: the plate, its label, value text and the parts of the look (screws) keep their
   size.
5. **A face larger than its plate is cut off**, within the room the label and value text leave for it. *Why:* the user sees it and makes the plate (or the face) fit; no
   automatic scaling that makes one control differ from the others.
6. **The face size is a free number** (a scale), edited with a numeric up/down in steps of 0.1. *Why:* fine control
   where needed.
7. **The face size is set per control only**, not inherited from a section or the dashboard. *Why:* the dashboard's
   zoom (its cell size) already scales everything; start minimal.
8. **The face size runs from 0.5 to 3.0**, defined in one place. 1.0 is the face's own size (its cells).
9. **A selector shows a list unless it is set to buttons.** *Why:* a list fits any number of options.
10. **A selector's face always takes the width of its plate** (amends 3 and 4 for the width): the list's display and
    the buttons stretch across the room the chrome gives the face, never narrower than the face's own size; the
    buttons of a row share the width equally (all equally wider), their height and minimum width stay the same for
    every selector. No setting for it. *Why:* making the plate wider is how the user makes room to show the option
    names in full.
11. **The ▲/▼ step arrows of a display have a fixed width** (a selector's list and the number box), whatever the
    width of the display; the extra width goes to the text. *Why:* a wider list showed wide, mostly empty arrows.
12. **The number box takes the width of its plate too**, as a selector's list (10, 11). *Why:* a number of a choice
    shows the name of its option, which needs the same room.

## Dismissing notifications in the console (2026-11-10)

Issue: the notifications could not be cleared; they hold both reports of what happened and the problems of the
current state (rig, dashboard, schema library), which are derived again on every refresh.

1. **Only reports can be dismissed**, one by one (✕) or all (*Clear*); the problems of the current state stay until
   their cause is gone. *Why:* those describe what is wrong now; hiding them would hide a real problem.

## The console always has a rig (2026-11-10)

Issue: the console started without a rig; devices could not be added until a rig file was opened.

1. **The console starts with a new, empty rig** that has no file yet (*Save rig* asks where). A rig is always open.
   *Why:* devices can be added right away, and no part of the console has to handle "no rig".
2. **A new rig is started from the + menu of the rig (*New rig*),** after confirming to discard unsaved changes.
   *Why:* next to adding devices, where a rig is put together.
3. **A schema file or assembly from the + menu of the rig only brings in its schemas** (and exchange files): it
   becomes a source of the rig (`Rig.Sources`, saved with it) and its root groups are offered by the + menu; no device
   is added. *Why:* an assembly holds many devices; the rig has only those the user adds. Listing and removing the
   rig's sources, and keeping a long + menu manageable, wait for later.

## Unsaved changes when the console is closed (2026-11-10)

Issue: closing the console window discarded the unsaved changes of the rig and the open dashboards without asking.

1. **Closing the window asks one confirmation for all changed files** (the rig and the open dashboards, by name),
   with the console's own confirmation dialog; *Quit* discards them, *Cancel* keeps the window open. No *Save all*
   from the dialog (for now). *Why:* the same discard-confirmation as *Open rig*, *New rig* and *Close dashboard*.
2. **Desktop only.** A browser page cannot wait for an in-app dialog when it is closed, and an OS shutdown does not
   wait either. *Why:* only the desktop window can be held open for the answer.

## A selector with pages in the console (2026-11-10)

Issue: some parameters are only valid for some options of another parameter (e.g. the wave parameters per oscillator
type); the dashboard should show only the parameters of the selected option.

1. **A new selector control that shows a page per option instead of buttons**; the existing tabs layout control stays
   as it is. *Why:* it is a selector at heart (bound to a parameter with `options`); tabs remain plain layout.
2. **The pages are taken from the options in the schema (at design time, like everything else), each tab named by
   the option's name. On creation every option gets a page, in the schema's order; after that the user removes and
   adds pages for options of the schema. The dashboard file keeps the pages as designed, each with the option it
   belongs to. At runtime (Play) only the selected page is determined: the page of the current value; no value (or an
   option without a page) is no page selected.** *Why:* the dashboard is a design on top of the schema; not every
   option needs a page.
3. **One notion of "a control that holds controls" in the model**, so the walks over the control tree do not depend
   on `LayoutControl`; the new control is a `ParameterControl` that also holds its pages. *Why:* one place of truth for
   walking the tree, instead of teaching either the binding code (layout kind with a slot) or every `Children` walk
   (parameter control with pages) about a special case.
4. **The pages hold controls only; no index variables (for now).** *Why:* no known need.
5. **Each page is a plain grid `LayoutControl`, as a page of tabs is; the editor does not offer variables on them.**
   *Why:* drops, moves and placement on a page work unchanged (the page is the target); only the tree walks change.
6. **The controls a control holds are a virtual `Children` on `DashboardControl`** (empty by default), overridden by
   `LayoutControl` and the new control; every tree walk uses it without a type test. *Why:* simplest for the walks;
   one member, no interface.
7. **A page records its option by name, matched without case.** *Why:* readable in the file and the term the console
   uses for a choice; a page survives a change of the stored values.
8. **A page whose option is no longer in the schema is reported, kept (showing its stored name) and never selected**;
   the user removes it or points it to another option. *Why:* the page may hold a design; nothing is lost silently.
9. **A page keeps its option name in its own `Label`**, as a page of tabs is named. *Why:* uses what is there; no new
   member or type.
10. **The new control is called `SelectorTabs`** (the `ParameterControlKind`, the palette item and the file element).
11. **The pages are chosen in a check-droplist on the `SelectorTabs` properties**: every option of the bound parameter
    in the schema's order, checked when it has a page. Checking adds an empty grid page (named by the option, at its
    schema position among the pages), unchecking removes it (an undoable edit). A page's `Label` is not edited
    otherwise. A page whose option is gone is listed checked and marked missing. This replaces pointing a page to
    another option (decision 8). *Why:* one editor covers adding, removing and the fixed names.
12. **Unchecking a page that holds controls removes it without a prompt**; undo brings it back. *Why:* every
    dashboard edit is undoable. **The only page stays** (its check and its Delete button are disabled, the Delete key
    says why): selector tabs without pages show nothing; to remove them, delete the selector tabs. Plain tabs may be
    empty: new tabs start so, and a dropped control becomes a page.
13. **(Re)binding the `value` slot replaces the pages**: all pages are removed and a page is added for every option of
    the new parameter. *Why:* the pages belong to the options of the bound parameter; simple and predictable. Only
    when the resolved parameter changes: a binding that is written again for the same parameter (a move, a device
    alias renamed, another `base`) keeps the pages. The same parameter is the same field of the same device, whatever
    repeat item (`BoundField.IsSameField`): its options are the same. Unbinding keeps the pages.
14. **In Edit mode the pages behave as those of tabs** (the page shown is UI state, a header click selects the page,
    selecting a control shows its page; nothing is sent). **In Play mode the value decides**: a header click sends its
    option, a received or loaded value shows its page, an unknown value or an option without a page shows none.
    *Why:* every page can be designed whatever the device's value. A click on the header of the selected page selects
    the (selector) tabs themselves (both kinds of tabs): their headers may cover all of a small control, which could
    then not be selected, sized or edited any more.
15. **`SelectorTabs` has no chrome**, as tabs: the headers are the selector, the pages hold their own controls.
16. **A single-value control can change its kind to `SelectorTabs` and back** (Properties): it keeps its binding and
    gets a page per option of the bound parameter, as on a first binding; selector tabs that become another kind lose
    their pages (the status says so; undo brings them back). The size stays, except that selector tabs get at least
    their default size (pushing others aside, as a resize; the pages follow). *Why:* a dropped options field becomes
    a selector (the first choice); turning it into selector tabs is the quick way to pages, which need room.
17. **Resizing tabs or selector tabs sizes their pages** to the space below the headers (`GridLayout.PageSize`), but a
    page never gets smaller than its controls need; then the tabs grow around it. *Why:* a page is the drop area for
    its controls; it should fill the control the user sized. Both kinds of tabs behave the same. **A page is not sized
    itself** (no handle, its size fields disabled, `Place` refuses): it has the size of its tabs. **No control is sized
    smaller than its controls need** (`GridLayout.MinimumSize`: for tabs the largest page content below the headers);
    the handle stops there. *Why:* one size for all pages; a resize that would snap back is not offered. A dashboard
    that is opened gets the same (`GridLayout.Complete`): pages that do not fill their tabs are sized (reported, save to
    keep it). The page area of selector tabs is the drop target of the page shown; their headers are the selector tabs.

## An edit that fails in the console (2026-11-10)

Issue: an exception in an edit (e.g. a page of selector tabs changed to a section) crashed the app.

1. **Edits are all or nothing**: an `EditGroup` that fails undoes what it did; `EditHistory` remembers an edit (and
   moves it between undo and redo) only when it succeeded; a refresh of the views that fails takes the edit back.
   *Why:* the dashboard, its file and the history always agree.
2. **A failed edit, undo or redo is a problem in the notifications** ("'…' failed and is not done", with the
   message); the app goes on, and the status says no success. *Why:* nothing goes wrong silently, nor fatally.

## Documentation in the schemas (2026-11-10)

Issue: the schemas have no place for documentation (e.g. text from the manual) that the Schema Editor can author and
the console can show on its controls (as a tooltip).

1. **One tag, `midi.doc`: plain text, possibly multi-line, no markup (for now).** Allowed on any node, any value is
   valid; it describes the node it is on and is not inherited. *Why:* a group's text is about the group, not its
   members.
2. **A newline in XML is written as a character reference in the attribute** (`midi.doc="Line one.&#10;Line two."`),
   not as a `<property>` element and not as a `\n` escape of our own. *Why:* not verbose; standard XML, so no decoding
   code; the serializer writes it back the same way (round trip tested); JSON (`\n`) and YAML hold the same text.
3. **The `midi.*` tags are not address-map specific: `AddressMapTags` is folded into a public `MidiProperties`**
   (`Messages`): the names, `Descriptors`, `IsTag`, `Validate`, `RoleOf`, `IsData` and `DocOf`; the helpers that read
   tags from nodes stay internal. An unknown tag is *"not a midi property"*. *Why:* one place for all tags; `midi.doc`,
   `midi.role` and `midi.device.*` have nothing to do with address maps.
4. **The Schema Editor edits `midi.doc` in a box of its own, not in the property list.** Every node's details (repeat,
   choice, group, field, also node defs) have a multi-line *Documentation* box with the note that it is stored as the
   `midi.doc` property; the Properties tab hides it. One place reads and writes it (`EditNode.Doc`). *Why:* a long
   multi-line text does not fit a property row; one place to edit it.
5. **In the workbench, the slot note is the `midi.doc` of the slot's root** (`Message<n>`) and the field panel has a
   documentation box for the field (written with *Define field*/*Apply*, copied to other slots). The first note of a slot
   without a root creates the root (and the schema). The slot notes are no longer kept in the session file; old notes
   are dropped (no backward compatibility). The note on bytes stays a session note: it also covers bytes that are no
   field. *Why:* what the user learns about a message belongs in the schema; the console can show it.
6. **The console shows the `midi.doc` of the bound fields in the tip of a control's label** (2026-11-12), below the
   full label; read once with the rest of the field's info (`ParameterInfo.Doc`, from `MidiProperties.DocOf`), joined
   per control by `DashboardSession.DocOf` (a control with more documented fields: each under its field name, a field
   in two slots once). No change to Midi.Device was needed. *Why:* the label already has the tip with the full name; a
   tip on the whole control would pop up while the user turns a knob.
7. **Group docs: a layout control shows the doc of the node its own `base` refers to** (2026-11-12): in the tip of a
   section's title and of a tab page's header; a control that only inherits its base shows none. A header of selector
   tabs shows the page's doc and then the selector field's (the options have no node of their own to document). The
   rig tree shows every node's doc in its tip, instead of the detail it already shows as text. One tip for all of these
   (`DocTip`). Midi.Device got `MidiProperties.DocOf(NodeInfo)`: the doc of any plan node (field, group, repeat,
   choice), next to `DocOf(SchemaNode)`. *Why:* an inherited base would repeat the outer doc on every nested control;
   clients work with plan nodes, so reading a doc from one is a generic need, not a console one.

## The data of a root for a client (2026-11-10)

Issue: the console (dump files, `IsMap`, `ItemCount`) and the Schema Editor (instantiate, match, the role fields) both
still use the address-only `AddressMapAdapter`; both must also work with dump messages. A first proposal added an
adapter shaped after the Schema Editor's needs.

1. **Midi.Device is a generic API; the console and the Schema Editor are two example clients.** A change to its API is
   checked against both, not designed from one. *Why:* no API that fits one app only.
2. **A new type or layer is fine for a generic concept of Midi.Device, not to fix a problem of one app.** This is
   the intent of decision 7 of *Message exchange in the console* ("no new layer"); it is not a ban. *Why:* prevents
   layers upon layers of app-specific fixes.
3. **The offline read and write of a root's data (`Load`, `Save`) and the lookup of the root a message carries go on
   `MessageMaps`; the facts of a root go on `MessageMapInfo`. `IMessageMap` stays internal.** Built once from the
   internal `IMessageMap` members, so every kind of map has them. Revises decision 7 of *Message exchange in the
   console* (`Load`/`Save` on a public `IMessageMap`). *Why:* `MessageMaps` is already the public entry point its
   owner creates; the other members of `IMessageMap` (`Split`, `Match`, `IsComplete`, `CreateState`) are for the
   exchange, not for a client.
4. **`IsMap` is replaced by a kind on `MessageMapInfo`: how the data of the root travels** (in parts by address, whole
   as the carried body of a dump message, or the root is the message itself). Every root has a message map since the
   provider chain, so "is a map" no longer tells anything; each app decides what to do with the kind. *Why:* the
   console (*Add device* menu, root order, dump files) and the Schema Editor (the address map UI of a body) branch on
   the same question. Named **`MessageMapKind`** (`MessageMapInfo.Kind`) with `Address`, `CarriedDump` and
   `MessageDump`. *Why:* "kind of map" is the jargon already used; not `MessageKind` (there are no message kinds,
   decision 3 of *Exchange definitions*), not `...TransferKind` (`midi.transfer` means how a group is cut into blocks).
5. **The console offers every root that can be device data as a device**, also one without an exchange file: its
   messages are then sent as is, and received when the user dumps them from the device (Console todo, *Devices
   without message exchange patterns*). *Why:* a device without exchange patterns is still usable.
6. **The dump provider is for SysEx messages: a root that is a message itself must be a complete SysEx message**
   (`midi.sysex` on the root); a root that is neither carried by a data message nor a SysEx message gets an error (no
   valid map), so it is not device data. A range of a dump is widened to the whole root (`DumpMap.Split`; decision 23
   of *Device messages without an address map*). *Why:* otherwise every plain group counted as a dump and was offered
   as a device. Such a root keeps getting an error from the dump provider (it does not decline it; decision 5 of
   *Device messages without an address map*).
7. **The item count of a repeat is a fact of the map: `MessageMapInfo.ItemCount(repeat)`** (`IMessageMap.ItemCount`):
   the items of the address space for an address map, the constant count of the schema for a dump. *Why:* the
   console (parameter tree, index variables) and the Schema Editor (addressed repeats) both need it, whatever the kind.
8. **`MessageMapException.Kind`: the kind of map that claimed the root and found the problems.** A client tells an
   invalid address map (reported) from a root that is no address map (e.g. a plain group: the console takes the
   schema's count). No other context properties for now: `Root` and the `Problems` (document, path, severity) cover
   the known cases. *Why:* both apps need it (the console's item counts, the Schema Editor's note "is an address map,
   but it cannot be used as one").
9. **`MessageMaps.Save(range, source, device, message?)` writes one data message per block; the message is optional.**
   Given: that data message (e.g. the one the Schema Editor's user instantiated); omitted: the first data message of
   the map whose device values are known. The range is split (and widened) by the map for that message.
   `MessageMaps.Load(root, messages, device, sink, warnings)` decodes the data messages of the root and reports the
   others (with the reason of a near miss). They replace `AddressMapAdapter.ReadData`/`EncodeData`; the console's dump
   files use them, so they work for dump messages too (un-parks decision 8 of *Message exchange in the console*).
   *Why:* offline there is no exchange file to name the message; the default covers the console, the parameter the
   Schema Editor. Not the map's own choice per block (`AddressMap.SelectBlocks`): one message per call is predictable.
10. **How a role field is supplied is a fact of the map: `MessageMapInfo.DescribeRole(role)`**, replacing
    `AddressMapAdapter.DescribeRole`. The device values the root fixes are described for every kind of map; the roles
    of a map's own (`address`, `size`, `data`) by the map (`IMessageMap.DescribeRole`). `DeviceValues.NameOfRole`
    gives the device value of a role (`device.deviceId`: `deviceId`), so a client does not parse the role. *Why:* the
    same fact as `RequiredDeviceValues`, seen from the message; a carried dump's root fixes device values too.
11. **The Schema Editor writes a message that carries a dump with `MessageMaps.Save`**, with the instantiated data
    message, the body's values and the device values of the role fields the root does not fix. *Why:* a role field
    the root fixes is no longer typed in the instance, so the map must supply it, as `WriteData` does for an address
    map.
12. **`MessageMaps.FindCarriedRoot(message, sysEx, device)`: the root a data message of the device carries**, tried
    with the map of every root that message can carry (a dump's body selected by its device values, the address map
    of the model). The Schema Editor's Match selects the body of a data open choice with it before the read; another
    body than the selected one is selected with a warning (kept when accumulating). *Why:* the user no longer has to
    pick the body first; the maps recognise the messages, the editor does not.
13. **What is an address map is decided in one place: `AddressMapProvider.Claims`.** `AddressProtocol.FindMapRoots`
    (used by `AddressMapAdapter` and `AddressMaps`) listed every carried root, so a carried dump body was read as an
    address map by the adapter. *Why:* a single place of truth; found when the editor read a carried dump.
14. **`AddressMapValidator` becomes `MessageMapValidator` (in `Messages`)**: it already validated every carried root
    through the provider chain; it now also validates the dumps that are a message themselves (`DumpMap.IsMessageDump`:
    a SysEx root that no data message carries and that has no data open choice). A root that is no SysEx message is
    not validated as a map (it is no device data). The Schema Editor's check follows (`MessageMapCheck`, *Message map*
    in the schema panel). *Why:* the check shows the problems of every kind of map; a plain group or an envelope gives
    no noise.
15. **A root that is a dump message itself is handled like a carried dump in the Schema Editor:** its role fields
    are marked with `MessageMapInfo.DescribeRole` and the instance of the root is written by `MessageMaps.Save` (the
    root is its own data message). An instance of a nested node of it is written plainly. *Why:* the same rules for
    every kind of map; a part of a dump is no message of its own.
16. **`AddressMapAdapter.PlanData`/`WriteData` move into `MessageMaps`, not `MessageMapInfo`** (operations on
    `MessageMaps`, facts on `MessageMapInfo`; decision 3): `Save` takes several ranges (duplicate blocks once),
    reports widening in optional warnings and takes **one model source for the whole message** (envelope and body,
    as `WriteData` does) instead of the body's values only; a `Plan` with the same arguments returns the blocks
    without writing, each with a description by its map (`IMessageMap.DescribeBlock`). *Why:* generic for every kind
    of map (a dump is one block); closes the envelope gap of `Save`; *Describe transfer* is no longer address-only.
    One source rather than a separate envelope source: the clients have one model of the message.
17. **Two shapes of one model source, two methods (2026-11-10):** `Save(ranges, source, …)` takes a model of the
    root's values (the console's `DeviceState`, the exchange's state), `SaveMessages(ranges, source, …)` a model of
    the whole message (the Schema Editor's instance; the map supplies the block roles and the device values the root
    fixes, the model the rest). `Plan` is shared; the map orders the blocks (`IMessageMap.Order`, by address for an
    address map) and describes them (`IMessageMap.DescribeBlock`). `AddressMapAdapter` keeps only the read
    (`Deserialize`, see the Device todo); `PlanData`, `WriteData`, `DataPlan`, `EncodedDataMessage`, `MapOccurrence`
    and its `IsMap`/`ItemCount`/`MapRoots` are removed; `DeviceValues.NameOfRole` is internal again (the editor no
    longer reads device values from its role fields). *Why:* the console has no model of an envelope, the editor's
    model is the envelope; each method keeps one source and the shapes never mix.
18. **The Schema Editor's read loses two cases of `AddressMapAdapter.Deserialize`:** reading the plan of a map root
    directly (raw map bytes without an envelope; ends the map's repeats) is removed, as is the fallback for a message
    the map does not recognise (read into the largest group at the address, with a warning): such a message is a
    plain error with the reason. *Why:* not a typical use case; the Schema Editor works on one schema at a time, so
    messages of another model are very unlikely.
19. **`MessageMaps.ReadMessage(root, message, sysEx, sink, device)`: the mirror of `SaveMessages`.** The map recognises
    the message (`IMessageMap.Match`; a near miss fails with its reason) and the body the model selects for the data
    open choice is narrowed to the recognised block and wrapped by the map (`IMessageMap.WrapBody`: an address map
    ends its repeats there; a dump's body is the whole root). The Schema Editor reads a message with a map body
    through it (with no device values: any device id) and everything else with the serializer. `AddressMapAdapter`
    and `AddressMaps` are removed, with `AddressMap.WrapMap`/`LargestGroupAt` and `AddressProtocol.FindMapRoots`.
    *Why:* the rest of `Deserialize` was generic map knowledge; both apps now use only the generic `MessageMaps` API.

## Messages that are received but not accepted (2026-11-09)

Issue: every SysEx from the port is tried against the messages a transfer can receive; a message that does not fit is
dropped without a trace (`AddressMap.TryRead` catches the serializer's exceptions, `MessageExchange.OnReceived` drops
what nobody recognises). A reply with e.g. a byte outside `validBytes` ends in a timeout that cannot be told apart
from a device that does not answer. The serializer throws on a mismatch; the map treats every exception as "not this
message".

1. **Silent until the envelope and the device match; reported from there on.** Not our message (constants differ)
   or another device (device values differ): silent, a port carries other traffic. The envelope and device match but
   the message fails (checksum, address not in the map, invalid data, no block fits), or it is recognised but outside
   the running block: reported, with the reason. *Why:* a near miss is a problem to show; other traffic is not.
2. **Only the running transfer reports them** (in its result), no event on `MessageExchange` for messages outside a
   transfer. *Why:* the transfer is where a missing reply is noticed.
3. **Jacobi.BinarySerializer reports a complete failure: one exception type** (deriving from
   `InvalidOperationException`) with the direction, a category, the node (path and instance), the byte position, the
   source, and the expected and actual value where they apply. The engine fills in the location by wrapping at the
   boundary of each node and processor call; a processor can report its own category. *Why:* a general purpose library;
   a caller (here: the map telling "not this message" from a near miss) needs facts, not message text.
4. **`Category` is a string, not an enum.** The engine uses its own fixed categories (e.g. a constant that does not
   match, invalid data, end of data, schema); a processor can give its own specific value. *Why:* no telling what
   future processors need.
5. **`Origin` says who reported it: the engine (internal errors) or the processor key** (`sys.crc`, `midi.string`).
   *Why:* together with `Category` it identifies the failure without a central list. Named `Origin`, not `Source`:
   `Exception.Source` already exists (the assembly name).
6. **The engine's categories are string constants in one place:** `expectedValue`, `invalidData` (read),
   `invalidValue` (write), `endOfData`, `schema`; the engine's source is `engine` (a processor key always has a dot). A
   processor that gives no category gets the general one of the direction (`invalidData`, `invalidValue`) with its key
   as the source. *Why:* the existing processor throws need no change. The decisions about Jacobi.BinarySerializer are
   kept in this file.
7. **The block a message arrived during lists what it did not accept** (`BlockResult.Unaccepted`, each with why); a
   block that did not complete names the first in its reason (e.g. after "timeout in turn 'request' …"). *Why:* the
   console shows the reason without changes; the near miss is usually the cause of the timeout.

## How a group is cut into blocks (2026-11-09)

Issue: the GX-700 *Display* lines must be requested individually; a request for both lines times out. With the `range`
unit (Roland RQ1/DT1) `AddressMap.Split` joins neighbouring fields into one block; group boundaries are no reason to
split, and `midi.atomic` only forbids a split inside a group. The schema could not say "per member".

1. **One property, `midi.transfer="atomic|individual"`, replaces `midi.atomic`** (no backward compatibility).
   `atomic`: the group is always one block; `individual`: its members are transferred on their own. Without it the
   message's unit and `midi.maxDataSize` decide. *Why:* both are settings of the same thing; one property rules out
   having both on a group. `transfer` rather than `split`: `split="atomic"` reads as the opposite of what it means.
2. **A value is about the group's own level:** `atomic` forbids a cut inside the group; `individual` requires a cut at
   each boundary of its direct members, and at the group's own start and end (else the first or last member joins the
   neighbours outside). Per field below is reached by nesting `individual`. *Why:* symmetrical; each group decides for
   itself.
3. **On groups and repeats, not on fields** (an error there), for every value. The members of a repeat are its items:
   `atomic` transfers all items in one block, `individual` each item on its own. *Why:* the rules are those of the
   property, not per value; a repeat such as the GX-700 effect chain must be sent whole without an extra group level
   (which would change the paths).
4. **Nesting: a contradiction is an error, a redundancy is not.** `individual` inside `atomic` is an error (a cut that
   is forbidden); `atomic` inside `atomic` is allowed and has no effect (was an error); `atomic` inside `individual`
   is a member that goes whole. A value without effect is not reported (a debug output may come later). *Why:* reuse of
   nodeDefs with atomic sections inside an atomic group.

## A dashboard refers to its rig (2026-11-09)

Issue: a dashboard file did not say what it depends on; the user had to open the right rig first. The idea was to
reference every schema and exchange document (files, and resources of assemblies) from the dashboard.

1. **The dashboard refers only to the rig it was made with (`rig`, `Dashboard.RigPath`), not to the documents.** The
   rig has the schema sources and, through its assemblies, the exchange files. *Why:* simpler; the rig stays the one
   place that decides what is loaded.
2. **The rig is opened only when the references do not resolve:** the open rig does not have the dashboard's devices
   with their map roots (`Dashboard.DeviceProblems`, also used by `BindingResolver`), or no rig is open. Replacing an
   open rig is confirmed; a rig that is not found is located by the user. *Why:* a dashboard is portable between rigs
   that have its devices.
3. **An open rig with the devices that is another file than the reference is used; the notifications say so (info).**
   *Why:* nothing happens silently, but nothing is in the way either.
4. **A located rig updates the reference** (the dashboard counts as changed), and saving writes the open rig as the
   reference, relative to the dashboard file (`FilePaths`, also used for the source paths). *Why:* the file follows
   where the user keeps the rig.

## Device values in the console's device panel (2026-11-09)

Issue: the device values of a rig device were one text (`name=value, …`); the user should pick the names the schema
knows. The console only had `SchemaLibrary.Maps` (`AddressMapAdapter`); the generic `IMessageMap` and `MessageMaps` were
internal.

1. **A list of device values, a name and a value per row; the droplist offers the names the map root needs from the
   client** (`deviceId`), not the ones it fixes. Any other name can be typed; a name the map root does not need is
   marked, not dropped. *Why:* start with what the user must supply; a value kept from another map root stays visible.
2. **A client sees a message map through a public, read-only wrapper, `MessageMapInfo`; `IMessageMap` stays
   internal.** It exposes only what a client needs (now: `Root`, `Device`, `RequiredDeviceValues`) and grows with real
   needs. *Why:* `IMessageMap` is the generic abstraction over address maps and dumps, but its operations (and
   `IDeviceState`) are for the exchange; `IMessageMap.Device` alone holds only the values the root fixes.
3. **The entry point is a public `MessageMaps`, created per serializer by its owner: `maps.Info(root)`.** The members
   that hand out an `IMessageMap` stay internal. *Why:* the owner (e.g. `SchemaLibrary`) controls the cache's lifetime;
   no hidden static cache, as a static factory or an extension on `Serializer` would need.
4. **`AddressMapAdapter` is to be phased out of the console** in favour of `MessageMaps`; `SchemaLibrary` keeps `Maps`
   for `IsMap`/`ItemCount` until it is decided what they mean for every kind of map (Console todo). *Why:* one generic
   mechanism instead of address-only code in the console.

## Message exchange in the console (2026-11-09)

Issue: the console has a value model per device (`DeviceState`) and MIDI connections with a `DeviceSession` per
device (`RigConnections`), but no data is exchanged with the devices yet; only dumps are loaded and saved, and only for
address maps. See the [proposal](../src/Jacobi.Midi.Console/sync-proposal.md).

1. **Two value models:** the console's `DeviceState` (what the user sees and sets) and the session's state of the
   device (Midi.Device, internal). The console does not own or hand in the session's state. *Why:* keeps the
   separation between a client app and the device API.
2. **The operations with a device are configured in the dashboard and bound to message exchange patterns**, not
   hard-wired in the console. *Why:* devices differ in their patterns; the console stays data-driven.
3. **`DeviceState` supports every kind of root the Device API supports** (address maps and plain dump schemas), for
   dump files and for the exchange alike; no address-only code in the console. *Why:* the Device API already handles
   both; one way of working for all devices.
4. **Exchange with the devices in Play mode only, to start with.** *Why:* smallest step; Edit mode can follow.
5. **Midi.Device gets a public API to read the data of a root from SysEx bytes and to write it as SysEx bytes**,
   without a connection or exchange file, for every kind of message map (address-map data messages and dump
   messages). The console's dump files use it instead of `AddressMapAdapter`. *Why:* the messages in a dump file are
   the same as on the wire; one place recognises and writes them (see *Device messages without an address map*,
   decision 15).
6. **"Dump file" in the console, "dump message" in Midi.Device.** The console names its side explicitly as a dump
   file (e.g. *Open › Dump file…*, the `DeviceState` members and `ValueOrigin`); "dump" alone means the message.
   *Why:* the two were easily mixed up.
7. **No new layer: `IMessageMap` gets `Load` (and later `Save`)**, implemented once in the interface from the members
   every map has (`DataMessages`, `TryRead`, `Split`, `Write`). `IMessageMap` and `MessageMaps` become public; the
   members the exchange uses stay internal. `Save` is on the Device todo. *Why:* no extra type next to the maps; every
   map gets it without code of its own; the Device API is a generic API, not only for the console and Schema Editor.
   *Revised (2026-11-10):* `Load`/`Save` go on `MessageMaps`, `IMessageMap` stays internal (see *The data of a root
   for a client*).
8. **A dump file holds real SysEx messages**, as the device sends and accepts them: a file and a MIDI connection
   differ only in the target, both need the same message maps. Decisions 5 and 7 are **parked**: the console keeps
   `AddressMapAdapter.ReadData`/`EncodeData` for its dump files for now (address maps only); dump files with dump
   messages come with `Load`/`Save` (Device todo). *Why:* the message exchange is the goal now; dump files are a
   separate feature.
9. **The dashboard names the exchange it requests the data with** (where its exchange file comes from: decision 11).
   *Why:* decision 2; the console does not assume `requestData` or which exchange file a device has.
10. **One dashboard at a time for the exchange, to start with**; several open dashboards are on the Console todo.
    *Why:* smallest step.
11. **Exchange files are sources like schema files:** embedded in an assembly (loaded with the assembly, e.g. the
    Roland devices) or an `.exchange.xml`/`.exchange.json` file the user authors, listed in the sources; a later
    file for the same root replaces an earlier one. The dashboard names, per device, the exchange to receive with
    (to send with, later); it does not name the file. *Why:* the same way of working as for schemas; the user can
    write an exchange file for a device without an assembly.

## Device messages without an address map (2026-11-06)

Issue: the message exchange (`MessageExchange`, `DeviceSession`) finds, builds and recognises every message through
the address map, so a device whose dumps are ordinary schemas (e.g. Korg function-code dumps) cannot be exchanged.
See the Device todo, *Message exchange for schemas without an address map*.

1. **A generic mechanism for device messages, with address as one specialization.** Other specializations will
   follow (e.g. the R3 program index, keyed parameter changes). *Why:* one mechanism instead of a second one next to
   addresses; new device schemes plug in.
2. **Providers, not locators.** What differs per device is more than *which part* a message carries (also keyed
   parameter changes, when a block is complete). A provider owns the link between a plan range and messages; the
   pattern (order, acks, retries, timing) stays in the exchange files, the bytes (packing, checksum) in the processors.
   *Why:* clear boundaries, room for what is not discovered yet.
3. **Two concepts: `IMessageMapProvider` and `IMessageMap`.** The provider claims a plan (`TryCreate`) and returns
   the map of that root; the map holds the operations. `AddressMap` is one map, `DumpMap` the default.
   *Why:* claiming and working with a root are different responsibilities.
4. **A fixed, ordered provider chain in Midi.Device:** address first, the default dump provider last. No registry or
   factories until needed. *Why:* simplest; predictable.
5. **A provider claims when its tags are present, then validates.** It does not decline because its tags are wrong;
   the chosen provider is visible (`Validate`/`Prepare`). The default dump provider reports an error when it cannot
   handle the root. *Why:* no silent fallback that hides a schema error.
6. **Selection per range, i.e. per plan root, cached** (like `AddressMaps` today). *Why:* simplest option.
7. **A minimal `IMessageMap`:** `Root`, `Device`, `DataMessages`, `Message(name)`, `IsData(message)`,
   `Validate(range, splitBy)`, `Split(range, message)`, `Write(message, block, source, device)`,
   `TryRead(message, sysEx, device, out data)`, `IsComplete(block, received)`. Extended when a real need arises.
   *Why:* these are exactly the calls the exchange makes now; nothing theoretical.
8. **Names:** `Split` (not "cut"); `Write` symmetric with `TryRead`. *Why:* self-explanatory, matches the existing
   `AddressMap.Split`.
9. **Recognising and decoding are separate; `Decode` is on `ReceivedData`, not on the map.** The map creates its
   `ReceivedData` with its own way of decoding. *Why:* recognition runs on the receive thread, only data of a
   completed block reaches the sink, a recognised message is not always accepted, and the address map does trial
   reads; a separate member on the map is not needed for that.
10. **The map decides what is data (`map.IsData(message)`); `DeviceMessage` holds schema facts only** (name, range,
    roles). *Why:* a root that is itself a message carries data without a `data` role.
11. **`IsComplete` gets the received data; no coverage object.** *Why:* the exchange already keeps the list; no extra
    interface.
12. **A block is a `PlanRange`.** `AddressBlock` stays inside the address map (and `DataPlan`). *Why:* `PlanRange` has
    `Equals`, `Overlaps` and `Contains` (added to Jacobi.BinarySerializer for this).
13. **The message exchange has no knowledge of addressing.** It validates exchange concerns only (definition, received
    messages carry data, source/sink, which message splits: the first sent message, else the first received data
    message) and delegates the rest to the Device.Messages API (the registry's `Validate`: device values, and the
    map's `Validate` for the provider-specific checks). *Why:* layering; a new provider needs no change in the exchange.
14. **Providers are stateless:** a provider or map never interprets a call based on context of earlier calls. Caches
    for speed are fine (e.g. the learned message overhead of the address map). *Why:* predictable, thread-safe.
15. **Layering: Device.Messages is the base, the message exchange a layer on top** (namespace
    `Jacobi.Midi.Device.Messages`). The base must keep working without an exchange file; not built now, but no
    decision may make it impossible. *Why:* schema-based device messages are useful on their own (offline, tools).
16. **Open-choice lookups belong in Jacobi.BinarySerializer** (forward: choice → bodies; reverse: body → choices, at
    schema and plan level); `AddressProtocol` uses them instead of its own matching. The schema level finds the
    document, the plan level the choice and its path. *Why:* the serializer owns the schema and the open-choice
    mechanism; a single place of truth.
17. **The default dump provider handles (a) a root that is a message itself and (b) a root carried as the body of a
    message's open choice.** Carried only inside another body (nesting) is not supported yet. Case (b) is the address
    map without addresses: finding carriers, `SelectBody` and writing/reading the envelope become shared helpers.
    *Why:* covers the simple dumps; the address-specific code stays separate instead of a type hierarchy.
18. **Simple whole dumps first;** the R3/Prophecy index becomes another provider later. *Why:* smallest step that
    proves the mechanism.
19. **Generic types get generic names:** `AddressMessage` → `DeviceMessage`; `DeviceValues` and `FieldValueStore` move
    out of the `Addressing` namespace; `midi.unit`/`midi.maxDataSize` stay with the address map. No backward
    compatibility. *Why:* a clear split between generic and address code.
20. **The map creates the session's state of a root (`IMessageMap.CreateState()` → `IDeviceState`).** `DeviceSession`
    only orchestrates (answer from the state or fetch, send, raise `StateChanged`); how the state decides is the map's.
    The state is the sink and source of the exchange and `Listen`, and has `Contains(block)`, `CopyTo(range, sink)`,
    merging the client's values for a send (and what is missing to complete it), `Clear(range)` and `Clear()`. The
    address state works per field (access, atomic groups), the dump state per root. *Why:* `DeviceSession` was built
    for addresses only (`WidenToAtomic`, `FieldsOf`, `midi.access` around a generic store); one member on the map
    instead of three; a dump needs no field occurrences (repeats sized by the data are no problem). Named after what
    the user sees: the state of the device.
21. **One generic problem and exception type for all message maps** (`MessageMapProblem`, `MessageMapProblemSeverity`,
    `MessageMapException`, in `Messages`; they replace the `AddressMap*` ones). Every kind of map reports its schema
    problems the same way, located at a schema node. *Why:* the shared helpers (`DeviceMessages`, `ConformingBodies`,
    `DeviceValues`) report problems too, and a client handles an invalid map the same way whatever its kind; the
    base layer (`Messages`) no longer depends on `Addressing` (apart from the provider chain).
22. **The address provider claims a root by its tags:** an address-map tag (`midi.address`, `midi.stride`,
    `midi.repeat`, `midi.atomic`, `midi.access`) on a node of the root, or a data message with an `address` role that
    carries the root. Every other root goes to the dump provider. *Why:* decision 5; a broken address map still gets
    address errors.
23. **A dump is sent whole, from the client's values only** (for now): the dump state does not complete a partial
    send from what it holds or from the device; a source that does not supply every value is told so. The fields
    with a role (e.g. the device id) are not kept in the dump state: the device values supply them. *Why:* the
    smallest working step; completing a partial send of a dump can be added when a device needs it.

## Exchange definitions (2026-11-06)

Issue: the message exchange patterns (request and collect data, send data with or without an acknowledgement) were
code protocols, chosen through schema tags (`midi.message`, `midi.exchange`, `midi.messageInterval`). Devices need
other patterns (handshakes, framing, store requests, prompts) without new code. See the Device todo, *Message
exchange patterns*, and the [Exchange readme](../src/Jacobi.Midi.Device/Exchange/readme.md) for how it works.

1. **The app decides what, how and when:** the range, the name of the exchange and the call. Neither the exchange
   file nor the messages decide which data is transferred. *Why:* one entry point; the device only describes how.
2. **Standard names for the most important patterns** (`ExchangeNames`: `requestData`, `sendData`); a file can add
   names of its own. *Why:* an app asks for a pattern whatever the device.
3. **References go one way:** an exchange file names its root (`for`) and the messages it uses; a schema never refers
   to an exchange file. There are no message kinds: a request is a message like any other. `midi.message`,
   `midi.exchange` and `midi.messageInterval` are removed, with no backwards compatibility. *Why:* the same message
   can be used by several patterns.
4. **Three responsibilities:** the app (range, exchange name, source and sink), the content (the schemas through the
   root's message map: what a message looks like, the blocks, when a block is complete) and the exchange file (the
   pattern and its timing). The exchange is not bound to addresses. *Why:* one place for each concern.
5. **One file per root with the device's timing; XML or JSON, read into the same model.** Files stand on their own
   (no extension); messages are named by their full schema name, so each device gets its own small file. *Why:*
   simple, explicit files.
6. **An exchange is a plain state machine of turns** with `send` and `receive` actions and transitions on events;
   no conditions, no values from replies. Defaults cover the common case (`sent`, `received` → completed; `timeout` →
   timed out). *Why:* small definitions; explicit only where the rule is not enough.
7. **No reason texts in a definition:** the runner writes them from what happened. *Why:* consistent reasons.
8. **The response timeout and message interval are set in the definition**, with defaults; per turn and at run time
   later. *Why:* the timing is a property of the device.
9. **Blocks are split by the root's message map for the split message:** the first message the block turns send,
   else the first data message they receive. The map checks whether it can split for it. *Why:* the exchange knows
   nothing of what a message holds.
10. **An exchange runs once per block; the first block that does not complete ends the call.** No explicit loop and
    no way to go on after a failed block. *Why:* the author describes one block; no partial transfers to reason about.
11. **Retries are an attribute of the `on` (`retry`),** followed by its own `goto` or `result`. *Why:* one element
    says both.
12. **Messages that arrive between turns are queued;** a turn that only sends goes on with `sent`. *Why:* no reply
    is lost.
13. **Messages once per call go in an optional `before` and `after`,** built for the whole range; `after` only runs
    when all blocks completed. Fixed messages (no `address`, `size` or `data` role) can be sent in any turn. *Why:*
    framing and store requests without a loop construct.
14. **A prompt for the user belongs to the exchange** (a literal text on a `receive`), because the exchange must
    already wait for the data when the user is asked. A prompt that waits for the user to confirm is the app's, before
    the call. *Why:* the app decides when a transfer starts.
15. **A device mode is switched by the app**, with an exchange of its own name, before it calls the exchange that
    needs the mode. Dropped: `forEach` with nested blocks, going on after a failed block, values from replies and
    conditions. *Why:* keep the exchange a plain state machine.
16. **`MessageExchange.Execute` replaces `Receive`/`Send`; `DeviceSession` gets everything when it is created** and
    has `Execute`, `RequestData` and `SendData`. *Why:* one entry point per layer.
17. **The Schema Editor's *Send* through the runner is a separate item.** *Why:* not needed for the exchange itself.
18. **`before` and `after` are built together (2026-11-09)**, `before` with tests of its own, though no device uses it
    yet (the Yamaha Bulk Header also needs the address map indirection). They hold turns, like the blocks, so they can
    wait for an ack, retry and `goto` within their part. *Why:* decision 13 as one piece; the same turn model everywhere.
19. **An `after` that fails after a send leaves the sent values in the session's state** (2026-11-09); the result
    reports the failure. *Why:* the data reached the device.
20. **A message can be a root of its own** (named `document.root`, e.g. `Gx700.SoundChangeRequest`), a fixed
    message next to the address map. *Why:* a fixed message is not part of the device's memory.
