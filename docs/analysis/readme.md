
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

Additions
- When adding new vendor notes, follow the template in each vendor file and include at least one representative sample dump under `docs/analysis/dumps/`.

