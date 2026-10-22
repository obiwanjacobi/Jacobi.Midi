# Korg — SysEx analysis

Overview

Korg uses manufacturer id `0x42`. Korg has no single SysEx scheme; the devices analysed here use three:

1. **Function-code objects** (DS-8, R3, Prophecy, i5M): data is exchanged as whole objects (the current voice or
   program, program *n*, a bank, all programs, global), selected by a **function code** and sometimes narrowed by index
   or scope fields in the message. Single parameters are changed with **keyed** parameter change messages (a parameter
   number or id, not an address) whose value encoding differs from the dump. Received data is acknowledged (ACK/NAK).
   Dump data is packed 7-to-8 in the newer devices.
2. **Address-based parameter change** (N1/N5): `F0 42 3n 4C 12 a1 a2 a3 dd… F7`, a 3-byte address like Roland DT1 and
   Yamaha XG, with the same parameters also reachable through XG and GS (Roland) messages at other addresses.
3. **A single memory dump** (EX-800, 1985): one request, one dump of the whole RAM, nibble packed with a checksum.

Sources (in `C:\My\My Manuals\Korg`):

| Device | Source | Readable | Korg SysEx |
|--------|--------|----------|------------|
| EX-800 | `Korg_EX-800_OM_E_MIDI_OCR.pdf` (MIDI implementation, manual pages 41-43) | OCR text | yes: memory dump request, memory dump, data error |
| DS-8 | `KORG_DS-8_OM_E_MIDI_OCR.pdf` (MIDI implementation, manual pages 25-33) | OCR text | yes: voice/combi dumps, modes, parameter change, ACK/NAK, write |
| R3 | `Korg_R3_MIDIImplementation_EFGJ1.pdf` | text | yes: dumps, parameter change, ACK/NAK, write |
| Prophecy | `Korg_Prophecy_BasicGuide_E2.pdf`, 6.6 MIDI Implementation (PDF pages 220-224) | scanned, read as page images | yes: dumps with unit/bank/program, parameter change, ACK/NAK, write |
| i5M | `KORG_I5M_Reference_Guide_MIDI_OCR.pdf` | OCR text (noisy) | yes: "all" dumps, mode, parameter change, ACK/NAK |
| N1/N5 | `Korg_N1_N5_OM_E_MIDI_OCR.pdf` (manual pages 103-107) | OCR text (tables noisy) | yes: address-based part, drum and display parameters (receive only), plus XG and GS |
| EK-50 | `Korg_EK-50_MIDIimp.TXT` | text | no: only universal messages (device inquiry, GM On) and XG On |
| XE20 | `Korg_XE20_MIDIimp.TXT` | text | no: universal messages, plus XG (Yamaha) and GS (Roland) messages |

Header format

```text
F0 42 3g <model> <function> [fields...] [data...] F7      DS-8, R3, Prophecy, i5M, N1/N5
F0 42 21 01 <function> [data...] F7                       EX-800
```

- `42` Korg id.
- `3g`: format id `3` in the high nibble, the global MIDI channel in the low nibble (a constant nibble plus a device value, like the Yamaha device number byte). The EX-800 uses format id `21` (no channel). The DS-8 device id request uses format id `4n`: `F0 42 4n F7`, answered with `F0 42 3n 13 F7`.
- Model id: DS-8 `13`, EX-800 `01`, R3 `7D`, Prophecy `41`, i5M `3C`, N5/N1 `4C` (the N1/N5 also accept the NS5R id `42`). Device inquiry replies: Prophecy family `41 00`, member `01 00`; N5/N1 family `4C 00`, member `05` (N5) / `0F` (N1).
- In the function-code devices the function code selects the message: requests in the `0x` / `1x` / `3x` range, dumps in the `4x` / `5x` / `6x` range, replies in the `2x` range. The same codes recur across devices (`10` current, `1C` all/program, `11` write request, `12` mode request, `41` parameter change, `23`/`24` ACK/NAK, `21`/`22` write completed/error).
- The N1/N5 Korg format has command `12` and a 3-byte address, like a Roland DT1 (but without checksum).

Messages of the function-code devices:

| Function | DS-8 | R3 | Prophecy | i5M |
|----------|------|----|----------|-----|
| Current program/voice dump request → dump | 1 voice `10` → `40` (80 bytes); 1 combi `19` → `49` (56 bytes) | `10` → `40` | `10` → `40` (dump starts with a program type byte) | — |
| Program dump request → dump | all voices `1C` → `4C` (7544 bytes); all combis `1D` → `4D` (4344 bytes) | `1C pp PP` → `4C pp PP data` (program number, 14 bits) | `1C ub pp 00` → `4C ub pp 00 data` (unit, bank, program) | `1C` → `4C` (all programs) |
| Other object dumps | — | formant motion `18 f 00` → `48 f ss SS 00 data`, current formant motion `13` → `43` | arpeggio pattern `34 us 00` → `69 us 00 data` | arrangements `30` → `64`, styles `31` → `65`, backing sequences `32` → `66 ss … data`, drums `0D` → `52` |
| Global dump | — | `0E` → `51` | `0E` → `51` | `0E` → `51` |
| All data dump | — | — | `0F` → `50` | `0F` → `50 ss ss data` |
| Parameter change | `41 pp vv V` (number, value LSB, value MSB bit 7) | `41 pp PP qq QQ vv VV` | `41 0m pp PP vv VV` | `41 pp vv VV`; drum kit `53 0k ss 0p vv VV` |
| Write request (store) | voice `11 pp` (program 00-63H), combi `1A pp` (00-09H) | program `11 pp PP`, formant motion `03 f 00` | program `11 0b pp` | — |
| Mode | panel mode request `12` → `42 0m`, change `4E 0m` (program, voice parm, combi parm, function, combi); keyboard mode request `1B` → `4B 0m`, change `4A 0m` (single, layer, double, multi) | — | — | mode request `12` → `42 0m 00`, change `4E 0m` |
| Replies | `23` ACK, `24` NAK, `21` write completed, `22` write error | `23` ACK, `24` NAK, `26` format error, write completed / error | `23` ACK, `24` NAK `0c`, `26` format error, `21` write completed, `22` write error `0c` | `23` ACK, `24` NAK, `26` format error |
| Other | device id request `F0 42 4n F7` | — | — | `67` chord (real-time) |

EX-800 messages (`F0 42 21 01 …`):

| Message | Bytes |
|---------|-------|
| Memory dump request | `F0 42 21 01 10 F7` |
| Memory dump | `F0 42 21 01 <RAM data> <checksum> F7`; the dump starts about 300 ms after the request |
| Data error (checksum error during a memory load) | `F0 42 21 01 20 F7` |

(The dump request table on manual page 42 shows `20` as "data request"; the notes on page 41 give `10` for the request and `20` for the error message. The notes are probably right.)

N1/N5 messages (receive only):

```text
N5   (Korg) F0 42 3n 4C 12 a1 a2 a3 dd… F7
NS5R (Korg) F0 42 3n 42 12 a1 a2 a3 dd… F7
XG          F0 43 1n 4C a1 a2 a3 dd… F7
GS          F0 41 1n 42 12 a1 a2 a3 dd… ss F7     ss = checksum: (12 + a1 + a2 + a3 + dd + … + ss) & 7F = 00
```

Addressing

**Function-code devices: no address field.** An object is selected by:

1. **The function code**: each object type has its own request and dump message (current voice/program, all programs, global, ...). A request names its reply by convention: "receives this message, and transmits Func=4C or Func=24".
2. **Index fields**: the R3 program number (14 bits), the R3 formant motion number, the Prophecy bank and program number, the Prophecy pattern number, the i5M drum kit number and drum index, the DS-8 write request program/combi number.
3. **A scope field** (Prophecy): the program dump (request) has a Unit field, `00` one program, `01` a bank, `10` all programs; the program number is ignored for a bank or all. So **one message type carries content of three different scopes**, chosen by a field in the message.
4. **Edit buffer vs memory**: the "current" / "1 voice" messages access the edit buffer, the program messages the stored programs; a write request stores the edit buffer to a program (DS-8, R3, Prophecy). The DS-8 has no request for one stored voice: only the edit buffer or all voices.

The dump layout is documented as offsets in the *unpacked* data ("No." in the R3 tables), including bit fields:

```text
R3 TABLE 1 (program, 452 bytes):
  0~7      program name (ASCII)                 ID:SubID 00:00~07
  8 B6~7   Voice Mode                           00:19
  8 B4~5   Arp Timb select                      00:18
  16~179   TIMBRE1 DATA (TABLE 2, 164 bytes)    1x:00~
  180~343  TIMBRE2 DATA (TABLE 2, 164 bytes)    2x:00~
  344~421  VOCODER DATA                         4x:00~
```

**EX-800: relative RAM addresses**, documented but not sent (the dump is the whole RAM):

```text
MIDI CH          0000H     1 byte
SEQUENCER DATA   0001H   256 bytes
UNDEFINED        0101H     9 bytes
BEND DEPTH       010AH     1 byte
UNDEFINED        010BH    21 bytes
PROGRAM DATA     0120H  1345 bytes   (21 bytes per program, bit fields across byte boundaries, "no space between notes")
```

**N1/N5: a 3-byte address space, three times.** The same parameters have an address in the Korg (N5) map, the XG map
and the GS map, each with its own layout:

```text
                      N5          XG          GS
All Parameter Reset   00,00,7C    —           —
XG System ON          00,00,7E    00,00,7E    —
GS Reset              00,00,7F    —           40,00,7F
Master Volume         00,00,04    00,00,04    40,00,04
Part nn Volume        01,nn,10    08,nn,0B    40,1x,19
Part nn Expression    01,nn,11    08,nn,0C    —
Drum rr Coarse Tune   3n,rr,00    3n,rr,00    —
Drum rr Level         3n,rr,02    3n,rr,02    41,m2,rr
Display Letter 0-31   08,00,00…   06,00,00…   10,00,00…
```

- Parts are a repeat in the middle address byte (N5 `01,nn,xx`, XG `08,nn,xx`, `nn` = part 00-1F). Drums are indexed by drum setup in the high byte and note number in the middle byte (`3n,rr,xx`, `rr` = 0C-6C).
- **GS block numbers are not linear in the part number**: in `40,1x,xx`, x = 0 is part 10 and x = 1-9 are parts 1-9; parts 17-32 use `50,1x,xx`, where x = 0 is part 26, x = 1 part 17, x = 2 part 18 (the manual shows only these rows). The address of a repeat item is not `start + index × stride` there.
- "For address items of `.... xx`, the value section can be set consecutively. Settings can also be started from a mid-point address", e.g. `F0 42 30 42 12 01 00 36 3C 3B 3A F7` sets the scale of part 1 for E, F and F# (addresses `01,00,36`-`38`) to -4, -5, -6 cent: a range write, like a Roland DT1.
- Master tune is 4 addresses of 4 bits each (as in Yamaha XG). Panpot "OFF" is a special two-byte value at the 1-byte panpot address: `F0 42 3n 4C 12 01 nn 14 11 mm F7`.

Parameter keys (parameter change of the function-code devices):

- **DS-8**: a 1-byte parameter number, a 7-bit value LSB and the MSB in bit 0 of the next byte (8 bits).
- **R3**: Parameter ID (14 bits) and Sub ID (14 bits), both separate from the dump offset. The ID is structured: timbre 1 uses `1x`, timbre 2 `2x` (a stride in id space), within a timbre `x1`~`x2` program, `x3` insert FX, `x4` motion sequence. A field of the dump can be several parameters: the 8-character program name is one dump field and eight parameters `00:00`~`00:07`.
- **Prophecy**: a group (`00` global, `01` program, `10` pattern) and a 12-bit Parameter ID. The ids follow the dump order, and the program parameter table gives each id its size and bits: e.g. id 1-16 program name (16 bytes), id 17 program category (1 byte, bits 0-3), id 18 user category (bits 4-7 of the same byte), id 19 oscillator mode (bits 0-1). Repeated blocks: EG1 ids 30-47, EG2 from 48, EG3 from 66, EG4 from 84 (18 ids each).
- **i5M**: a 1-byte parameter number (Table 8). The drum kit parameter change has the kit number, a drum index (00-59) and a parameter number: a key with an instance.

Packing patterns

- **7-to-8 packing** of all dump data (R3 NOTE 5, Prophecy NOTE 1, i5M NOTE 1): every 7 data bytes become 8 MIDI bytes, first a byte with the 7 high bits (bit 7 of bytes 7n+0 … 7n+6), then the 7 low parts. R3 program: 452 bytes = 7·64 + 4 → 8·64 + (4+1) = 517 bytes; R3 global: 256 → 293 bytes.
- **Nibble packing** (EX-800): each RAM byte is sent as two nibbles, LSB first (`0000****`, `0000****`); the checksum likewise.
- Bit fields within a byte (R3 "8 B6~7", Prophecy "1(Bit0-3)"), and bit fields across byte boundaries (EX-800 program parameters, "parameter with * exceeds byte unit").
- Parameter change values are 14-bit (LSB bits 6-0, MSB bits 13-7), signed on the R3; the DS-8 uses 8 bits (LSB 7 bits, MSB 1 bit). The dump stores the same parameter in its own width (e.g. 2 bits). The value encoding of a parameter change is **not** the dump encoding.
- Multi-byte values in the dump (Prophecy tempo LSB/MSB, R3 arpeggio tempo LSB/MSB); 4-bit nibbles over 4 addresses (N1/N5 master tune).

Checksum

- EX-800: the least significant 8 bits of the sum of the 1633 RAM bytes, sent as two nibbles (LSB first). A checksum error on a memory load is answered with the data error message.
- None in the DS-8, R3, Prophecy, i5M or N1/N5 (Korg format) messages. The GS messages the N1/N5 accept carry the Roland checksum.

Fragmentation / multi-packet flows

- Each dump is one message, sized by its object (no splitting by the device in these manuals). Variable-length dumps carry a size field (R3 formant motion data size, i5M sequence and backing sequence data sizes). The large dumps are single messages: DS-8 all voices 7544 bytes, EX-800 RAM 1633 bytes (3266 nibbles).
- i5M: *"When transmitting a series of exclusive messages to the i5M, wait until DATA LOAD COMPLETED or WRITE COMPLETED is received."* Sending is flow-controlled by the acknowledgements.

Partial-op flows (request/response)

- **Request → dump**: each request has one reply dump, or `24` (NAK) when the device cannot comply. EX-800: the dump starts about 300 ms after the request.
- **Dump → ACK/NAK**: a received dump, parameter change (R3, i5M) or mode change is answered with `23` DATA LOAD COMPLETED or `24` DATA LOAD ERROR (Prophecy: with an error code, `001` program memory protected, `010` pattern memory protected). `26` reports a format error (e.g. a wrong data length). EX-800: only a data error message on a checksum error.
- **Write request → write completed / error** (DS-8, R3, Prophecy): stores the edit buffer to a program.
- **Device-initiated**: the R3 and Prophecy send a parameter change when a parameter is changed on the panel (Prophecy: when "SysEx Transmit" is on), the R3 sends the current program dump when entering edit mode, the Prophecy sends it on a program change.
- **Mode dependence**: the i5M: *"Some request messages are not received in some modes"*; the DS-8 and i5M can be asked for their mode and switched by SysEx. The DS-8 transmits and receives exclusive messages only when "Exclusive" is enabled.
- **Receive only** (N1/N5): the parameter changes are only received; there is no request and no acknowledgement.
- The Prophecy and N1/N5 also have NRPN parameters (channel messages).

Example dumps

```text
# DS-8 1 voice dump request (channel 1)
F0 42 30 13 10 F7
# DS-8 device id request (channel 1) and its reply
F0 42 40 F7
F0 42 30 13 F7
# EX-800 memory dump request (from the manual)
F0 42 21 01 10 F7
# R3 program dump request, program 5
F0 42 30 7D 1C 05 00 F7
# Prophecy program dump request, unit = one program, bank A, program 3
F0 42 30 41 1C 00 03 00 F7
# N1/N5 (NS5R id): Scale E, F, F# of part 1 (from the manual)
F0 42 30 42 12 01 00 36 3C 3B 3A F7
# Korg data load completed (ACK) from an R3
F0 42 30 7D 23 F7
```

(Except where marked "from the manual", constructed from the message formats, not captured from a device.)

Links & references

- KORG EX-800 Owner's Manual: MIDI Implementation (pages 41-43): system exclusive messages, RAM data chart, program parameter per note.
- KORG DS-8 Owner's Manual: MIDI Implementation (pages 25-33): system exclusive messages and reference.
- KORG R3 MIDI Implementation (EFGJ1): MIDI exclusive format, NOTE 1-6, TABLE 1-8.
- KORG Prophecy Basic Guide (E2): 6.6 MIDI Implementation (manual pages 161-165): function code lists, MIDI exclusive format, NOTE 1-2, program parameters.
- KORG i5M Reference Guide (MIDI part, OCR): 1-5 structure of Korg system exclusive messages, 3. MIDI exclusive format.
- KORG N1/N5 Owner's Manual: Appendices (pages 103-107): bank list, MIDI channel messages, part parameter change (N5/XG/GS addresses), drum parameters, display.

Notes

What this means for the address map mechanism (see `address-map-proposal.md`):

- **No address on the wire** (DS-8, R3, Prophecy, i5M, EX-800): a message reaches its object through its function code (contract), index fields and sometimes a scope field. The `addressStart` role cannot be required; an index role (→ the repeat instance of the target) and a scope role are needed. The layout offsets are still useful inside the mechanism (atomic groups, widening), just not sent. The EX-800 RAM chart even documents such offsets.
- **The N1/N5 fits the address map** (3-byte addresses, part and drum repeats, range writes from a mid-point), but has **several address maps over the same parameters** (N5, XG, GS: different envelopes, different layouts). A device map should be reachable through more than one address map, or the parameters must be modelled once with per-envelope addresses.
- **Non-linear repeat addressing** (N1/N5 GS block numbers): the item → address relation can be a permutation, not `start + index × stride`.
- **A request names its reply**: an envelope has several data messages, so the request → reply pairing must be explicit.
- **Packing**: 7-to-8 packing (R3, Prophecy, i5M) and nibble packing with a checksum (EX-800) need layout processors on the data (e.g. `korg.pack7`, a nibble processor), chained with the bit-packer of the dump layout. The EX-800 has bit fields that cross byte boundaries.
- **The EX-800 is a dump-only device**: one request, one dump of everything, a checksum and an error message: an atomic map root.
- **Keyed parameter changes** need a separate binding: a key per field (with strides for repeated blocks), a generic value encoding, and parameters that are part of a field (the R3 name characters).
- **The exchange is a state machine**: ACK/NAK after every message with wait-before-next flow control (i5M), write confirmation with error codes (DS-8, Prophecy), device modes that decide which requests are accepted (i5M, DS-8 panel modes), receive-only devices (N1/N5), a delayed dump (EX-800), device-initiated dumps and parameter changes. This, together with the Roland handshake (retries) and the Yamaha header/footer framing, argues for explicit exchange protocols separate from the content binding.
