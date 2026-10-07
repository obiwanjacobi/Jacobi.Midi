# MIDI Messages v1

## TODO

- SysExMessage must probably originate from MidiMessage (union) otherwise a collection of MidiMessages will not be able to contain SysExMessages. Probably the same for MetaMessages.
- Reader/Writer: add realtime support. Add MetaMessage support. Add SysExMessage support.
- make internal accessor for uint32 _data.
- 