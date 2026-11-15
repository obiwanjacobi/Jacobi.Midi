using System.Diagnostics.CodeAnalysis;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Device.Commands;

/// <summary>
/// The kinds of channel message a field of a command root can be transferred with (<c>midi.cmd</c>).
/// </summary>
internal enum CommandKind
{
    /// <summary>A Control Change (<c>cc</c>); the number is the controller number.</summary>
    ControlChange,
    /// <summary>A Registered Parameter Number (<c>rpn</c>); the number is the 14-bit parameter number.</summary>
    RegisteredParameter,
    /// <summary>A Non-Registered Parameter Number (<c>nrpn</c>); the number is the 14-bit parameter number.</summary>
    NonRegisteredParameter,
    /// <summary>A Program Change (<c>pc</c>); it has no number.</summary>
    ProgramChange,
}

/// <summary>
/// The channel message of a field of a command root, as its <c>midi.cmd</c> tag gives it: the kind and its number
/// (<c>cc#74</c>, <c>rpn#0</c>, <c>nrpn#1234</c> or <c>nrpn#3/12</c> as MSB/LSB, <c>pc</c>). It identifies the field on
/// the wire, together with the group and channel (device values).
/// </summary>
internal readonly record struct Command(CommandKind Kind, int Number)
{
    private const char NumberSeparator = '#';
    private const char ByteSeparator = '/';

    /// <summary>The largest number of a kind.</summary>
    public static int MaxNumber(CommandKind kind) => kind switch
    {
        CommandKind.ControlChange => 127,
        CommandKind.RegisteredParameter or CommandKind.NonRegisteredParameter => 0x3FFF,
        _ => 0,
    };

    /// <summary>The MSB of a 14-bit parameter number (RPN/NRPN).</summary>
    public byte Msb => (byte)((Number >> 7) & 0x7F);

    /// <summary>The LSB of a 14-bit parameter number (RPN/NRPN).</summary>
    public byte Lsb => (byte)(Number & 0x7F);

    public override string ToString() => Kind switch
    {
        CommandKind.ControlChange => $"cc{NumberSeparator}{Number}",
        CommandKind.RegisteredParameter => $"rpn{NumberSeparator}{Number}",
        CommandKind.NonRegisteredParameter => $"nrpn{NumberSeparator}{Number}",
        _ => "pc",
    };

    /// <summary>Parses the value of a <c>midi.cmd</c> tag; the error says what is wrong.</summary>
    public static bool TryParse(string? text, out Command command, [NotNullWhen(false)] out string? error)
    {
        command = default;
        text = text?.Trim();
        if (String.IsNullOrEmpty(text))
        {
            error = "the command is empty: give cc#<number>, rpn#<number>, nrpn#<number> or pc.";
            return false;
        }

        var separator = text.IndexOf(NumberSeparator);
        var kindText = separator < 0 ? text : text[..separator];
        var numberText = separator < 0 ? null : text[(separator + 1)..];
        CommandKind? kind = kindText.ToLowerInvariant() switch
        {
            "cc" => CommandKind.ControlChange,
            "rpn" => CommandKind.RegisteredParameter,
            "nrpn" => CommandKind.NonRegisteredParameter,
            "pc" => CommandKind.ProgramChange,
            _ => null,
        };
        if (kind is not { } known)
        {
            error = $"'{text}': '{kindText}' is not a kind of command (cc, rpn, nrpn or pc).";
            return false;
        }

        if (known == CommandKind.ProgramChange)
        {
            error = numberText is null ? null : $"'{text}': a program change has no number; write 'pc'.";
            command = new Command(known, 0);
            return error is null;
        }

        if (numberText is null || !TryParseNumber(known, numberText, out var number))
        {
            error = known == CommandKind.ControlChange
                ? $"'{text}': a control change needs a controller number from 0 to 127: cc#<number>."
                : $"'{text}': a parameter number needs a number from 0 to 16383, or <msb>/<lsb> from 0 to 127 each: {kindText}#<number>.";
            return false;
        }

        error = null;
        command = new Command(known, number);
        return true;
    }

    private static bool TryParseNumber(CommandKind kind, string text, out int number)
    {
        number = 0;
        var byteSeparator = text.IndexOf(ByteSeparator);
        if (byteSeparator >= 0)
        {
            if (kind == CommandKind.ControlChange
                || !MidiProperties.TryParseNumber(text[..byteSeparator], out var msb) || msb > 127
                || !MidiProperties.TryParseNumber(text[(byteSeparator + 1)..], out var lsb) || lsb > 127)
            {
                return false;
            }
            number = (int)((msb << 7) | lsb);
            return true;
        }

        if (!MidiProperties.TryParseNumber(text, out var value) || value > (ulong)MaxNumber(kind))
        {
            return false;
        }
        number = (int)value;
        return true;
    }
}
