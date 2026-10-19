# Jacobi.Midi

A .NET library and tool set for working with MIDI devices, focused on describing
manufacturer-specific SysEx messages through schemas.
See [src/readme.md](src/readme.md) for the product vision and [docs/analysis](docs/analysis/readme.md) for vendor research.

## Platform-specific backends

The MIDI I/O API (`IMidiApi` and friends) is defined in the platform-neutral `Jacobi.Midi.Core` (`net11.0`).
Each OS gets its own implementation project, for example `Jacobi.Midi.Windows` on Windows MIDI Services.
Libraries and shared UI code only depend on Core. They never reference a platform backend.

A platform backend may need an OS-specific target framework. `Jacobi.Midi.Windows` targets
`net11.0-windows10.0.26100.0` because of the CsWinRT projection of Windows MIDI Services.
A plain `net11.0` project cannot reference it.

### Multi-targeted application heads

The choice of backend is made in the **application head** (the executable project),
which builds once per OS family:

| Target framework              | Used on        | MIDI backend                                                  |
|-------------------------------|----------------|---------------------------------------------------------------|
| `net11.0-windows10.0.26100.0` | Windows        | `Jacobi.Midi.Windows`                                         |
| `net11.0`                     | Linux, macOS   | selected at runtime with `OperatingSystem.IsLinux()` / `IsMacOS()` (not implemented yet) |

The head multi-targets and only references the Windows backend for the Windows target framework:

```xml
<PropertyGroup>
  <TargetFrameworks>net11.0;net11.0-windows10.0.26100.0</TargetFrameworks>
</PropertyGroup>

<ItemGroup Condition="'$(TargetPlatformIdentifier)' == 'windows'">
  <ProjectReference Include="..\..\Jacobi.Midi.Windows\Jacobi.Midi.Windows.csproj" />
</ItemGroup>
```

The SDK defines the `WINDOWS` symbol for the Windows target framework, so the head picks the backend
in a single place and hands it to the platform-neutral code as an `IMidiApi`:

```csharp
private static IMidiApi? CreateMidiApi()
{
#if WINDOWS
    return new Jacobi.Midi.Windows.WindowsMidiApi();
#else
    return null;    // Linux/macOS backends go here, selected with OperatingSystem.IsXxx().
#endif
}
```

Linux and macOS backends will not need their own target framework. They can be plain `net11.0` assemblies
marked with `[SupportedOSPlatform("linux")]` / `[SupportedOSPlatform("macos")]`.
The platform compatibility analyzer then flags any call that is not guarded by a runtime check.

`Jacobi.Midi.SchemaEditor.Desktop` follows this pattern and passes the backend to `App.MidiApi`.
When no backend is available, the MIDI bar is disabled.
The browser head (`Jacobi.Midi.SchemaEditor.Browser`) is a separate head and supplies its own backend (none for now).

### Building, running and publishing

A plain `dotnet build` builds both target frameworks. For `dotnet run` and `dotnet publish`, choose one with `-f`,
and publish per runtime identifier (RID):

```
dotnet run --project src/Jacobi.Midi.SchemaEditor/Jacobi.Midi.SchemaEditor.Desktop -f net11.0-windows10.0.26100.0

dotnet publish src/Jacobi.Midi.SchemaEditor/Jacobi.Midi.SchemaEditor.Desktop -f net11.0-windows10.0.26100.0 -r win-x64
dotnet publish src/Jacobi.Midi.SchemaEditor/Jacobi.Midi.SchemaEditor.Desktop -f net11.0 -r linux-x64
dotnet publish src/Jacobi.Midi.SchemaEditor/Jacobi.Midi.SchemaEditor.Desktop -f net11.0 -r osx-arm64
```

This gives two builds (one per target framework) and one published output per RID.
Only the Windows outputs contain `Jacobi.Midi.Windows.dll` and `WinRT.Runtime.dll`.
All other assemblies (Core, Messages, the shared SchemaEditor library, ...) are the same `net11.0` binaries everywhere.
