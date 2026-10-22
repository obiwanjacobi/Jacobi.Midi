# Yamaha — SysEx analysis

Overview

Yamaha uses manufacturer id `0x43`. Most models since the XG era describe their settings as an **address map**:
3-byte addresses (7 bits per byte) with parameter tables per block, as Roland does. Requests carry **no size**.
The size follows from the target: one parameter, or one dump block. The message type is coded in the high nibble
of the device number byte.

Sources (owner's manuals, "MIDI Data Format" section, in `C:\My\My Manuals\Yamaha`):

| Device | Scheme | Group / Model ID | Notes |
|--------|--------|------------------|-------|
| CP88/CP73 | address map, 3 x 7 bits | Group `7F 1C`, Model `08` | Live Set Sounds via Bulk Header/Footer |
| CK88/CK61 | address map, 3 x 7 bits | Group `7F 1C`, Model `0B` | same scheme as CP88 |
| YC61 | address map, 3 x 7 bits | Group `7F 1C`, Model `09` | same scheme as CP88 |
| YC88/YC73 | address map, 3 x 7 bits | Group `7F 1C`, Model `09` | same scheme as CP88 |
| QY100 | XG address map, 3 x 7 bits | Model `4C` (no group id) | XG tables 1-1 … 1-9 |
| G50 | named bulk dumps (`7A` format), 2-byte parameter numbers | `7A` (bulk), `56` (parameter change) | not address based for bulk data |

Header format

The four address-based message types. `n` = device number (low nibble), and the high nibble is the message type:

| Message | CP88 / CK88 / YC (`gh gl` = `7F 1C`, `id` = model) | XG (QY100) |
|---------|------------------------------------------------------|------------|
| Bulk Dump (`0n`) | `F0 43 0n gh gl bh bl id ah am al dt … cc F7` | `F0 43 0n 4C bh bl ah am al dt … cc F7` |
| Parameter Change (`1n`) | `F0 43 1n gh gl id ah am al dt … F7` | `F0 43 1n 4C ah am al dt … F7` |
| Dump Request (`2n`) | `F0 43 2n gh gl id ah am al F7` | `F0 43 2n 4C ah am al F7` |
| Parameter Request (`3n`) | `F0 43 3n gh gl id ah am al F7` | `F0 43 3n 4C ah am al F7` |

- `bh bl` = Byte Count (2 x 7 bits): the number of data bytes in the bulk dump.
- `ah am al` = Address High / Mid / Low (3 x 7 bits).
- A parameter change carries all data bytes of one parameter: "For parameters with a Data Size of 2 or 4, the corresponding amount of data will be transmitted" (QY100 3-6-3).
- The QY100 also has non-XG messages in the same family (MIDI Master Tuning `F0 43 1n 27 30 00 00 mm ll cc F7`) and universal messages (GM On, Master Volume).
- The G50 uses the older format: a bulk dump `F0 43 00 7A bb bb "LM  0256OM" … dt … cc F7`, where an ASCII header names the dump type (`…OM` 1 memory, `…MA` all memory, `…SY` system). Its dump request is `F0 43 20 7A "LM  0256xx" F7`. A parameter change is `F0 43 10 56 gg pp dd dd F7` (group + parameter number, always 2 data bytes).

Addressing

Addresses are numbers in a 7-bit digit space (`00 00 7F` + 1 = `00 01 00`), as with Roland. The manuals give a
**Parameter Base Address** table (the top-level blocks) and a parameter table per block (relative addresses, size,
range, default, "TOTAL SIZE").

CP88 base addresses (CK88 and YC use the same structure with their own blocks, e.g. YC organ/key sections):

```text
20 00 00  System                         TOTAL SIZE 30h
20 40 00  Master EQ                      TOTAL SIZE 14h
20 70 00  Contents Unlock
0E pp 0n  Bulk Header  (pp = Live Set page 0-19, n = sound 0-7; 7F 00 = current sound buffer)
0F pp 0n  Bulk Footer
0D 00 00  Store To Flash
46 00 00  Live Set Sound Common          TOTAL SIZE 30h
4A zz 00  Zone (zz = 0-3)                TOTAL SIZE 10h
50 0p 00  Section Common   (p = 0 Piano, 1 E.Piano, 2 Sub)
50 1p 00  Section Specific (p = 0 Piano, 1 E.Piano, 2 Sub)
```

XG base addresses (QY100, Table 1-1):

```text
00 00 00  System (TOTAL SIZE 06)
00 00 7D  Drum Setup Reset   (write only, data = drum setup number)
00 00 7E  XG System On       (write only)
00 00 7F  All Parameter Reset (write only)
01 00 00  System Information (TOTAL SIZE 10; bulk dump only, transmit only)
02 01 00  Effect 1: Reverb 02 01 00 (0E) + 02 01 10 (06), Chorus 02 01 20 (0F) + 02 01 30 (06), Variation 02 01 40 …
08 nn 00  Multi Part nn (nn = part number): two blocks, nn 00 (TOTAL SIZE 29) and nn 30 (TOTAL SIZE 3F)
3n rr 00  Drum Setup n+1 (30 / 31), rr = note number 18h (24) … 54h (84)
```

Observations:

1. **Repeats are indexed through an address byte**, usually the middle one: Zone `4A zz 00` and Multi Part `08 nn 00` have a stride of `00 01 00`. Drum Setup has two levels: setup in the high byte (stride `01 00 00`) and note in the middle byte (stride `00 01 00`). The note index **starts at `18h`**, so the first item is at `30 18 00`, not at `30 00 00`.
2. **Dump blocks are not the same as the structure.** One Multi Part consists of two dump blocks (`nn 00` and `nn 30`) with a reserved gap between them. Reverb, Chorus and Variation are split the same way. A Dump Request must name the **Top Address of a dump block**, and the reply is the whole block ("TOTAL SIZE").
3. **Write-only (trigger) addresses**: XG System On, Drum Setup Reset, All Parameter Reset, Store To Flash, Bulk Header/Footer. They lie outside any block and hold no stored value.
4. **Read-only blocks**: System Information (`01 00 00`) is only transmitted, in response to a dump request.
5. **Bulk Header/Footer indirection** (CP88, CK88, YC). The stored Live Set Sounds have no addresses of their own:
   - A stored sound is transferred as Bulk Header `0E pp 0n`, then the blocks at the *edit buffer* addresses (`46 00 00`, `4A zz 00`, `50 0p 00`, `50 1p 00`), then Bulk Footer `0F pp 0n`.
   - The header/footer address selects the memory slot; `7F 00` selects the current sound buffer.
   - "The block from the Bulk Header to the Bulk Footer … can be received regardless of their order; however, they cannot be received if an irrelevant Block is included."
   - "To execute 1 Live Set Sound bulk dump request, designate its corresponding Bulk Header address." One request returns header, blocks and footer.
   - The header address follows the usual stride rules (page `00 01 00`, sound `00 00 01`), but it selects content that lives at other addresses.
6. **Type-dependent parameters**: Reverb/Chorus/Variation parameters 1-16 "depend on the effect type". The addresses are fixed; only the meaning of the values changes.

Packing patterns

- One address is one 7-bit data byte.
- Nibbles over several addresses: XG/CP88 Master Tune is 4 addresses, "1st bit3-0: bit15-12 … 4th bit3-0: bit3-0".
- MSB/LSB pairs as one parameter of size 2: XG Reverb/Chorus Type, Variation Param 1-10.
- Multi-character names as consecutive addresses (QY100 System Information Model Name, size `0E`).
- G50: "Internal parameters are all 8 bit data … MSB and LSB are always separated into 2 bytes".

Checksum

- Bulk Dump only: "Checksum is the value that results in a value of 0 for the lower 7 bits when the Byte Count, Start Address, Data and Checksum itself are added." This differs from Roland, which sums Address + Data only and has no byte count.
- Parameter Change, Dump Request and Parameter Request have no checksum.

Fragmentation / multi-packet flows

- QY100: "513 bytes or more must not be transmitted at once … divided into packets of 512 bytes or less, and transmitted at an appropriate timing interval (120 msec or longer)." Each packet is a Bulk Dump with its own address and byte count.
- G50: "When Data is divided into several blocks for transmission, there is an interval of 150ms between the transmission of each block."
- CP88/CK88/YC: a stored Live Set Sound is a sequence of bulk dumps framed by Bulk Header/Footer (see Addressing, observation 5).

Partial-op flows (request/response)

- **Parameter Request** (`3n`) at the start address of one parameter: the device answers with a Parameter Change (`1n`) holding that parameter's data (1, 2 or 4 bytes, see the Size column of the tables).
- **Dump Request** (`2n`) at the Top Address of a dump block (or a Bulk Header address): the device answers with Bulk Dump(s) (`0n`).
- QY100: parameter changes are "transmitted only if a Parameter Change Request is received"; bulk dumps only if a Bulk Dump Request is received.
- G50: bulk dumps and dump requests are only recognised in Play mode.

Example dumps

```text
# XG System On (QY100, verbatim from the manual)
F0 43 10 4C 00 00 7E 00 F7
# CP88 dump request for the System block (constructed from the format: device number 0)
F0 43 20 7F 1C 08 20 00 00 F7
# CP88 dump request for Live Set page 1, sound 1 (constructed: Bulk Header address 0E 00 00)
F0 43 20 7F 1C 08 0E 00 00 F7
```

Links & references

- CP88/CP73, CK88/CK61, YC61, YC88/YC73 owner's manuals: "MIDI Data Format" (Parameter Change, Bulk Dump, Dump Request, Parameter Request), "Bulk Dump Block", "Parameter Base Address" and the "MIDI PARAMETER CHANGE TABLE".
- QY100 owner's manual: "MIDI Data Format" (3-6-x XG messages) and Tables 1-1 … 1-9.
- G50 owner's manual: "MIDI Data Format", sections 1-2.

Notes

What this means for the address map mechanism (see `src/Jacobi.Midi.Device/Addressing/readme.md`):

- The same envelope tagging works. The device number + message type byte is a bit-packed byte: a constant type nibble plus a device parameter nibble. Group ID and Model ID are constants or device parameters.
- **Requests without a size.** The mechanism must request at known boundaries: one parameter (Parameter Request) or one dump block (Dump Request). This needs a tag for groups that are dump blocks (e.g. `midi.block`).
- **Several message kinds per envelope**: parameter messages for single fields, bulk messages for blocks.
- **Access**: write-only trigger addresses and read-only blocks need a tag (e.g. `midi.access`), so a receive never requests a trigger and a send never writes a read-only block.
- **Bulk Header/Footer indirection** needs its own design: a repeat whose items share the content addresses (the edit buffer) and are selected by a header address with its own strides.
- The G50 bulk format is a plain schema (named dump types). Only its parameter change (2-byte parameter number) could use an address map.
