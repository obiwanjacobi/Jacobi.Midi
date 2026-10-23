# Instructions

> I give you permission to access all files in "..\Jacobi.BinarySerializer" (relative to this project root folder, where `.git` also lives). It is a dependecy of the project. You can use it to answer questions and provide code examples.

## Project Guidelines

- Read-only Git operations such as status, diff, log, and blame are allowed. Do not create branches, make commits, or push unless explicitly requested. This applies to the sibling Jacobi.BinarySerializer repository too.
- Coding standard: Async methods do not end in 'Async' (project-level preference)
- In this codebase targeting .NET 11 / C# 15, `closed` and `union` are intentionally used as new C# 15 keywords in type declarations.
- Don't call static methods on compiler aliases: e.g. use `String.IsNullOrEmpty()` instead of `string.IsNullOrEmpty()`.
- Write doc-comments on new lines. Not ///<summary>...</summary> on the same line. Except for very short descriptions.
- Add a doc-comment for every type newly created to describe its purpose and responsibilty.
- If a request is ambiguous, can be interpretted multiple ways -or- when the request mentions specifics that are not the complete picture: summarize and list the problems and ask for clarity from the user before continuing.
- If a tool does not work as expected, tell the user. Describe the problem and propose an alternative.
- Use the jargon of the project, how things are called in the code and documentation. When referencing code, use the exact terms used there, in the code itself or its comments.
- When an item on the todo list is done, mark it of - do not delete it. Marked items are read-only and will never change, even if the item they describe is no longer valid or accurate.
- Don't copy-paste code or data from project files to document a concept or mechanism. Reference to the files as examples or create stand-alone examples that don't go out of sync when the code evolves.

## Project Overview

Jacobi.Midi is a .NET library and tool set for working with MIDI devices, with a focus on
describing manufacturer-specific SysEx messages through schemas. The main goals are:

- A schema-driven way to read and write device SysEx data (built on Jacobi.BinarySerializer).
- A Schema Editor application to author those schemas, including reverse engineering of existing SysEx dumps.
- Cross-platform MIDI I/O, preferring MIDI 2.0 (UMP) where available.
- Later: a device console / patch librarian and remote MIDI access (see the vision in `src/readme.md`).

The code is in active development: APIs, file formats and project boundaries change frequently.
Treat the code itself as the source of truth and check the readme files for current details. But note that the readme files may be out of date.

## Repository Layout

- `src/` — the solution (`Jacobi.Midi.slnx`) and all projects. `src/readme.md` holds the product vision and backend notes.
- `docs/analysis/` — research notes on SysEx conventions per vendor (Roland, Yamaha, Korg, ...), common packing primitives and schema proposals.
- `.vscode/` — build/launch configuration for the desktop Schema Editor.
- `../Jacobi.BinarySerializer` — sibling repository referenced by project references (not packages); its projects are included in the solution.

## Projects

- **Core** — platform-neutral MIDI abstractions: API, sessions, endpoint discovery, connections and universal (UMP) packets.
- **Windows** — implementation of the Core contracts on Windows MIDI Services (MIDI 2.0 SDK preview). Windows-only target.
- **Messages** — typed MIDI message models: MIDI 1.0 channel/system/SysEx/meta messages, MIDI 2.0 UMP messages (`V2` namespace), and byte-stream event reading/writing. See its readme.
- **Device** — base `midi` schema (embedded resource) shared by device schemas, and the MIDI-specific BinarySerializer processors (e.g. SysEx framing) under the `midi` namespace (see `Processors/readme.md` for design notes). Will contain the main API to work with MIDI device capabilities.
- **Roland** — Roland-specific processors (e.g. checksum) and embedded schemas for the Roland message format and specific devices.
- **File** — placeholder for Standard MIDI File support (MIDI v1 & v2).
- **Cli** — prototype command line tool.
- **SchemaEditor** — Avalonia (MVVM) application with a shared UI library and Desktop and Browser (WebAssembly) hosts. Uses central package management within its folder.
- **Tests** — single xUnit test project covering messages, processors, schemas and the schema editor models.

## Key Concepts

- **Schemas** describe binary structure (fields, groups, repeats, choices) in XML, JSON or YAML; behaviour such as framing, 7-bit packing or checksums is handled by pluggable **processors**.
- Processors are discovered through a processor factory per assembly, each owning a namespace prefix (`sys`, `midi`, `roland`, ...).
- Schemas can include other schemas: device schemas build on vendor message schemas, which build on the base `midi` schema.
- The Schema Editor works on an editable draft model of a schema and maps it to the serializer's schema document when saving, preserving source formatting where possible.

## Build & Test

- Target framework is .NET 11 (preview) with C# 15 language features.
- Build from `src/`: `dotnet build Jacobi.Midi.slnx`.
- Test from `src/`: `dotnet test --project Jacobi.Midi.Tests`. The repo-local `global.json` (repo root) switches `dotnet test` to the Microsoft.Testing.Platform runner, which the xunit.v3 test project requires.
  - Do not run `dotnet test` on the solution: it also contains `Jacobi.BinarySerializer.Tests` (NUnit, VSTest runner), and in this mode all test projects must use Microsoft.Testing.Platform, so the run is refused.
  - Run the Jacobi.BinarySerializer tests from within that repository (its own `src/`), where the `global.json` of this repo does not apply.
- The sibling `Jacobi.BinarySerializer` repository must be present next to this one for the build to succeed.
- Changes in the schema format or processor API usually originate in Jacobi.BinarySerializer; keep the schemas in this repo (Roland, Device) and the Schema Editor in sync with it.
