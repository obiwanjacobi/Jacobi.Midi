
# SysEx Analysis

This folder collects analysis artifacts for SysEx variations across vendors: header formats, packing schemes, addressing, fragmentation, checksum algorithms, and partial request/response flows.

Purpose
- Store vendor summaries, sample hex dumps, and links to public documentation.
- Define canonical packing/message primitives used by the profile schema.

Conventions
- Filenames and headings use lower-case `readme.md` where indicated.
- Sample dumps are plain hex tokens separated by spaces, one message per line. Comments may be added with `#` at the start of a line.
- Place vendor PDFs or scanned material under `docs/analysis/docs/` and add a short reference in the vendor file.

Index
- roland.md
- yamaha.md
- korg.md
- elektron.md
- novation.md
- dumps/readme.md

---

## Sound Diver

An old program that sort of acomplishes the same.

Rules from the SoundDiver programming manual:

- (older) SysEx may not end with F7. The appearance of another MIDI status byte (except real-time) indicate the end.

- Manufacturer Id (byte after SOX) can be one byte up to three bytes.

- Transmission formats (how do I sent 8 bits in 7-bit bytes?)
  - Split Nibble: hi and lo nibbles are sent separately. Exist in LE/BE format.
  - ACII Nibbles: Same as split nibble but the binary value is translated to an ascii char.
  - 8x7-bit packed: the seventh bit of max 7 bytes is packed into the last byte (at resp. position).
  - 7 byte bit-field: all bits packed into a bit array and send as 7-bit bytes.
  - 1+ 7 bit packed: the 7-bit is send in bit0 of an extra byte. Exist in LE/BE format.
  - (n)- 7-bit (short/int) values: various ways exist to pass multi-byte values.

- Checksum formats:
  - Checksums may work on physical midi stream bytes or logical (say before nibble split) values.
  - Number of checksum bytes may vary from one to four.
  - Checksums may be added to or subtracted from constant values (Kawai)
  - 2's complement: make checksum negative.
  - 1's complement: invert value

- Parameter (Field) values:
  -Bit field may also come in LE/BE. Bit0 could be most significant.
  - Different ways of indicating a negative number:
    - 2's complement: invert all bits and add 1
    - neg sign bit (magnitude): one bit indicates a negative, remaining bits indicate number
    - constant field-specific offset: a value of 24 with offset 12 becomes 0

  - String values can be encoded:
    - ASCII. most use this.
    - Custom conversion table.

- Parameter (Field) identification:
  - Address location
  - Specialized message to change only on parameter (field).
  - a mask is transmitted to mask out bits not part of the parameter value change.
  - Might need to send PC's and CC's so select current banks/ sounds/patches.

Manual Intervention:
- The user might need to perform some manual tasks in order to complete the data exchange.

Address Maps:
- Max length to message (128 or 256 bytes).
- A configurable delay is needed between sending packets.
- Roland uses 7-bit Hex address (and size) values.

Message Handshake protocols:
- not many implement it (Roland)
- Some implement it incompatible (Casio starts of with SysEx but sends plain data without Sysex compatibility. Will software drivers even receive that?)
- Other devices use a simple message exchange mechanism.
- Some devices do not function without.
