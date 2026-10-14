# MIDI

## Schema Editor

An application that lets you author Midi Schemas.

- Easily create new schema definitions with an interactive GUI.
- Load/Save schema files in Json, Xml and Yaml.
- Work on multiple (related) files at once (SchemaSet).
- Import/Export Midi SysEx messages for testing schema definitions.
- Live MIDI connection with device for testing and probing.
- Reverse Engineer existing Midi SysEx messages incrementally.
- Add descriptions and sample values per field for documentation and testing.
- Register processor assmeblies and base schema assemblies.

### New Schema from scratch

A rich UI that guides the user through the schema making process.
The editor presents the options applicable at that node, which prevents invalid constructions.
The nested/hierarchical structure of a schema is presented in such a way that it does not become cluttered or confusing for the user.

### Reverse Engineering

During reverse engineering of an existing Midi SysEx message, the bytes are presented as like in a binary editor.
The F0/F7 are recognized automatically and presented with the correct structure/processor.
The user can highlight one or more bytes and isolate them by giving it a name and potentially a datatype.
Multiple Midi messages may be collected and compared in order to detect differences between them and learning what bytes represent a certain device characteristic/setting.

## Midi Device Console

An application that provides central control over multiple Midi Devices.
Communication is either based on Midi v1 (with schemas) or Midi v2 capabilities inquiry.

### Patch Editor / Librariran

- Allow users to change patch settings remotely on their Midi devices.
- Provide categorization, tagging and naming individual patches in an off-device database.
- Provide a way to share patch libraries per Midi device.

## Remote Midi Access

A (server+client) tool to let an authenticated user (or application) interact with a Midi device over a remote internet connection for the purpose of creating/reverse engineering the Midi device schema.

- The server tool runs locally on the computer that has the Midi device in question connected.
- The user of the computer determines who gets access.
- The server tool transports the midi messages (two-way) over the internet to/from the client-tool.
- The server tool must be reachable from the internet (firewall/modem settings).
- The client tool starts the (outgoing) connection to the remote server tool.
- The server and client tools are cross-platform (win/mac/lin)
- The server tool should be a single package that simply works and contains code for Midi, Network and Security (certificate?)

## Resources

Windows:
https://microsoft.github.io/MIDI/


Cross platform UI for tools:
- avalonia >= 11.3: works on Win/Mac/Lin but not native rendered.
- Uno Platform 6: All the same Xaml but not native.
- MAUI 11: native but Linux support is unofficial/community solutions (GTK4)
