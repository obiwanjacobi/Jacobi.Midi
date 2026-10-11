# Roland GP-8 MIDI Implementation

Structure extracted from the GP-8 Owner's Manual (MIDI Implementation, Version 1.00, 1987). The source PDF text is OCR output with errors: values marked *(verify)* were reconstructed and should be checked against the manual.

Schemas: [RolandType4.xml](Schemas/RolandType4.xml) (message envelope), [Gp8.xml](Schemas/Gp8.xml) (memory map).

## MIDI implementation chart

| Function | Transmitted | Recognized | Remarks |
|----------|-------------|------------|---------|
| Basic channel | - | 1-16 | Memorized |
| Mode | - | OMNI ON/OFF | Memorized |
| Control change 16 | - | yes | EV-5 |
| Control change 80 | - | yes | Control pedal |
| Program change | 0-127 | 0-127 | |
| System exclusive | yes | yes | Bulk dump / bulk load (Roland "one way" format) |

Everything else (notes, velocity, aftertouch, pitch bend, system common, real time, aux) is not used.

## Channel messages

| Direction | Status | Data 1 | Data 2 | Description |
|-----------|--------|--------|--------|-------------|
| Transmit, receive | `1100 nnnn` | `0ppp pppp` | - | Program change, p = 0-127 |
| Receive | `1011 nnnn` | `0001 0000` (16) | `0vvv vvvv` | Controls the parameter selected with EV-5 PARAMETER, v = 0-127 |
| Receive | `1011 nnnn` | `0101 0000` (80) | `0vxx xxxx` | Control pedal: v = 1 on, v = 0 off |

## Exclusive messages (Roland Exclusive Format Type IV)

All exclusive communication is "one way": the unit sends or recognizes DT1 and recognizes RQ1. There is no handshaking.

### Frame

| Byte | Bits | Description |
|------|------|-------------|
| a | `1111 0000` | Exclusive status (`F0`) |
| b | `0100 0001` | Roland ID (`41`) |
| c | `0000 nnnn` | Device ID = MIDI basic channel - 1 (channel = nnnn + 1) |
| d | `0001 0011` | Model ID GP-8 (`13`) |
| e | `0001 0001` / `0001 0010` | Command ID: RQ1 (`11`) or DT1 (`12`) |
| f | `0aaa aaaa` | Address MSB |
| g | `0bbb bbbb` | Address LSB |
| h... | | RQ1: Size MSB, Size LSB. DT1: Data |
| i | `0ddd dddd` | Checksum |
| j | `1111 0111` | End of exclusive (`F7`) |

### Commands

| Command | ID | Direction | Body after the address |
|---------|----|-----------|------------------------|
| RQ1 (request data) | `11` | Recognized only | Size MSB, Size LSB |
| DT1 (data set) | `12` | Transmitted and recognized | Data (one byte) |

- Data of one parameter is sent at one time; only the data of one parameter is recognized at one time.
- Address, size and data bytes are 7-bit values (MSB first).

### Checksum

The sum of all bytes between the Command ID and EOX (address, size or data, and checksum) must be `00H` (7 bits). The Command ID and EOX are not included.

`checksum = (128 - ((address + data) mod 128)) mod 128`

- In DT1 the data is written the moment it is received, so the checksum is not checked.
- In RQ1, even when the address is in the middle of one sound parameter, or the size does not cover a whole sound, the data of one whole sound is output.

## Address mapping

Two 7-bit bytes carry a 14-bit address:

```text
MSB: 0 m g b b b n n
LSB: 0 n p p p p p p
```

| Field | Bits | Meaning |
|-------|------|---------|
| m | 1 | 0 = temporary parameter, 1 = memory parameter |
| g | 1 | Group: 0 = A, 1 = B |
| bbb | 3 | Bank: 0-7 = 1-8 |
| nnn | 3 | Number: 0-7 = 1-8 (2 high bits in the MSB, low bit in the LSB) |
| pppppp | 6 | Parameter number `00`-`31` (hex) |

- Program number = `g bbb nnn` (0-7F).
- Temporary parameter: the working sound (address with m = 0).
- Memory parameter 1 = group A, bank 1, number 1; memory parameter 128 = group B, bank 8, number 8.

## Parameters

Parameter number is the low 6 bits of the address. Every parameter is one 7-bit byte unless noted.

| No (hex) | Parameter | Range / notes |
|----------|-----------|---------------|
| 00 | Effect on/off (MSB) | bit 0 phaser, bit 1 equalizer, bit 2 delay, bit 3 chorus (0 = off, 1 = on) |
| 01 | Effect on/off (LSB) | bit 0 dynamic filter, bit 1 compressor, bit 2 turbo OD, bit 3 distortion (0 = off, 1 = on) |
| | **Dynamic filter** | |
| 02 | Sens | 0-100 |
| 03 | Cutoff freq | 0-100 |
| 04 | Q | 0-100 |
| 05 | Up/Down | 0 = down, 100 = up |
| | **Compressor** | |
| 06 | Attack | 0-100 |
| 07 | Sustain | 0-100 |
| | **Turbo overdrive** | |
| 08 | Tone | 0-100 |
| 09 | Drive | 0-100 |
| 0A | Turbo | 0 = off, 1 = on *(verify)* |
| | **Distortion** | |
| 0B | Tone | 0-100 |
| 0C | Dist | 0-100 |
| | **Phaser** | |
| 0D | Rate | 0-100 |
| 0E | Depth | 0-100 |
| 0F | Resonance | 0-100 |
| | **Equalizer** | |
| 10 | Hi level | 0-100 |
| 11 | Mid level | 0-100 |
| 12 | Lo level | 0-100 |
| 13 | Out level | 0-100 |
| | **Digital delay** | |
| 14 | E.level | 0-100 |
| 15 | D.time (MSB) | 0-1000, together with 16 (7 bits each) |
| 16 | D.time (LSB) | |
| 17 | Feedback | 0-100 |
| | **Digital chorus** | |
| 18 | Rate | 0-100 |
| 19 | Depth | 0-100 |
| 1A | E.level | 0-100 |
| 1B | Pre delay | 0-100 *(verify)* |
| 1C | Feedback | 0-100 |
| | **General** | |
| 1D | Master volume | 0-10 |
| 1E | EV-5 parameter | 0-27, 0 = off |
| 1F | Ext control out 1 | 0 = off, 100 = on |
| 20 | Ext control out 2 | 0 = off, 100 = on |
| 21-30 | Name (1)-(16) | ASCII 32-127 |
| 31 | End of string | 0 |

The address numbers 0D onward follow the order of the OCR text; the group boundaries (Phaser from 0D, EQ from 10, delay from 14, chorus from 18) are consistent with the contiguous 00-31 range *(verify)*.

## Communication sequences

| Situation | Behavior |
|-----------|----------|
| WRITE pressed in BULK DUMP (All) mode | All programs and temporary parameters are sent as DT1, about 20 ms apart |
| WRITE pressed in BULK DUMP (Current) mode | The current program and temporary parameters are sent as DT1, about 20 ms apart |
| One way data set received (DT1) | The sender must wait more than 20 ms between messages |
| One way request received (RQ1) | The unit answers with DT1 messages, about 20 ms apart |

## Mapping to the schemas

| Concept | Schema |
|---------|--------|
| Frame, command, address, size, data, checksum | `RolandType4.xml` (group `Message`) |
| Address bit fields | `Gp8.xml` (group `Address`) |
| Parameters 00-31 | `Gp8.xml` (group `Patch`) |

Open items (checksum processor, multi-byte data, number split across address bytes, reuse of the patch for the 128 memory programs) are marked `[TODO]` in the schemas.
