# MIDI Processors

- The SysEx byte-(sub)stream may have to be converted before (for read, or after for write) individual fields can be processed. This conversion has to be scoped to a sub-stream of the SysEx data. Multiple different conversions may exist in the same SysEx message. Examples are nibble-packing (LE or BE), 7-bit packing, and 14-bit packing.
  - A custom record type defines the conversion and custom processor-code implements it.
- A field is coupled to a dataType. It represents a single logical value. The dataType defines the field's size and how to convert it to and from a byte-stream. The processor-code implements the conversion.
- DataTypes and recordTypes may be derived by custom types and code. The processor mechanism is extensible and will find the correct processor-code to use based on the dataType or recordType.


## Address Mapping

- When the requested address overlaps multiple recordTypes, but not all fields of the start- and/or end-recordType are included in the request. To handle this, the recordType that is used to know what fields to map is to be duplicated and trimmed, so that the start- and end-recordType only contain the fields that are included in the request. The trimmed recordTypes are then used to map the requested address to the fields of the recordTypes. This trimmed recordType is not exposed to the outside via the API. The calling application receives the original recordType as metadata. Alternatively an enable-list of fields can be used to indicate which fields are included in the request - that way no dynamically generated recordTypes are needed.
- 