# Jacobi.Midi.VirtualDevice

A virtual MIDI 2.0 device (Windows MIDI Services) that answers MIDI-CI Property Exchange, to test the Device API
against, Set included: the in-box General MIDI synthesizer answers a Set with a NAK.

Run it, and use the endpoint `Jacobi Test Device` (e.g. detect it in the console). Stop it with Enter or Ctrl+C (or by
closing its terminal): the device is removed from the MIDI service. Not with the debugger's Stop: that kills the
process, which can leave the device in the service.

```text
dotnet run -p:Platform=x64
```

## Resources

| Resource | canSet | |
| -------- | ------ | - |
| `DeviceInfo`, `ChannelList`, `ProgramList` (`main`) | none | the standard resources, answered by the platform |
| `X-Tempo` | full | a single value |
| `X-Globset` | full | an object; its `links` name the entries of `X-ProgramEdit` |
| `X-ProgramEdit` (`patch1`, `patch2`) | partial | entries (`requireResId`), set by JSON Pointer; subscribable |

The resources and their JSON Schemas are in `CustomResource.cs` and `Standard.cs`.

## How it works

Built on the `capability-inquiry-virtual-device` sample of Windows MIDI Services. The platform's responder
(`MidiVirtualDevice.CapabilityInquiry`) answers Discovery, Get and Subscribe from the resources it is given. It does not
answer a Set: that arrives in `MessageReceived`, where `PropertySetResponder` assembles its chunks, applies the data
(whole, or with `setPartial` by JSON Pointer), publishes it to the responder (so a Get and the subscribers see it) and
replies with the status (200, or 400/404/405 with a message). The data of the resources is kept in memory only.

## Known problem

A device that is not removed (the process was killed) stays in the MIDI service, and creating it again waits forever.
The tool checks for it first: when `Jacobi Test Device` already exists it says so and exits, instead of hanging. When
no other run of the tool is left, restart the service as administrator:

```powershell
Stop-Process -Name MidiSrv -Force; Start-Service midisrv
```

When the service is in a bad state, creating the device can still hang: `Creating the virtual device...` stays the last
line. The same restart clears it.
