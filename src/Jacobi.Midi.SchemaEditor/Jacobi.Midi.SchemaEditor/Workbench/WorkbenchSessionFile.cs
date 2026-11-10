using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jacobi.Midi.SchemaEditor.Workbench;

/// <summary>
/// The contents of a workbench session: its title, the captures (with their messages), the baseline and the annotations.
/// A session is self-contained: the messages are stored in it (as hex), not referred to.
/// </summary>
/// <param name="Title">The name of the reverse-engineering work, e.g. 'GX-700'.</param>
/// <param name="Captures">The captures, in the order of the captures list.</param>
/// <param name="Baseline">The index of the baseline in <paramref name="Captures"/>; -1 when there is none.</param>
/// <param name="Annotations">The notes of the user on byte ranges.</param>
/// <remarks>
/// The note on a message slot is not part of the session: it is the documentation (<c>midi.doc</c>) of the slot's root
/// in the target schema.
/// </remarks>
internal sealed record WorkbenchSession(string Title, IReadOnlyList<Capture> Captures, int Baseline, IReadOnlyList<Annotation> Annotations);

/// <summary>
/// Reads and writes a <see cref="WorkbenchSession"/> as JSON (a '.workbench.json' file).
/// </summary>
internal static class WorkbenchSessionFile
{
    /// <summary>The file extension of a session file.</summary>
    public const string Extension = ".workbench.json";

    private const int CurrentVersion = 1;

    public static string Serialize(WorkbenchSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var file = new SessionDto
        {
            Version = CurrentVersion,
            Title = session.Title,
            Baseline = session.Baseline,
            Captures = session.Captures.Select(capture => new CaptureDto
            {
                Label = capture.Label,
                ChangeNote = capture.ChangeNote,
                Hidden = capture.IsHidden,
                Messages = capture.Messages.Select(message => HexText.Format(message)).ToList()
            }).ToList(),
            Annotations = session.Annotations.Select(annotation => new AnnotationDto
            {
                Slot = annotation.Range.Slot,
                Start = annotation.Range.Start,
                Length = annotation.Range.Length,
                Text = annotation.Text
            }).ToList()
        };
        return JsonSerializer.Serialize(file, WorkbenchJsonContext.Default.SessionDto);
    }

    /// <exception cref="FormatException">The text is not a (supported) session file.</exception>
    public static WorkbenchSession Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        SessionDto? file;
        try
        {
            file = JsonSerializer.Deserialize(json, WorkbenchJsonContext.Default.SessionDto);
        }
        catch (JsonException exception)
        {
            throw new FormatException($"The file is not a workbench session: {exception.Message}", exception);
        }
        if (file is null)
        {
            throw new FormatException("The file is not a workbench session.");
        }
        if (file.Version > CurrentVersion)
        {
            throw new FormatException($"The session file has version {file.Version}; this editor reads up to version {CurrentVersion}.");
        }

        var captures = new List<Capture>();
        foreach (var dto in file.Captures ?? [])
        {
            var messages = new List<byte[]>();
            foreach (var hex in dto.Messages ?? [])
            {
                if (String.IsNullOrWhiteSpace(hex))
                {
                    messages.Add([]);
                    continue;
                }
                if (!HexText.TryParse(hex, out var bytes, out var error))
                {
                    throw new FormatException($"Capture '{dto.Label}' has an invalid message: {error}");
                }
                messages.Add(bytes);
            }
            captures.Add(new Capture(dto.Label ?? "Capture", messages)
            {
                ChangeNote = dto.ChangeNote ?? String.Empty,
                IsHidden = dto.Hidden
            });
        }

        var annotations = (file.Annotations ?? [])
            .Where(dto => dto.Slot >= 0 && dto.Start >= 0 && dto.Length > 0 && !String.IsNullOrWhiteSpace(dto.Text))
            .Select(dto => new Annotation(new ByteRange(dto.Slot, dto.Start, dto.Length), dto.Text!))
            .ToList();
        var baseline = file.Baseline >= 0 && file.Baseline < captures.Count ? file.Baseline : captures.Count > 0 ? 0 : -1;
        return new WorkbenchSession(file.Title ?? String.Empty, captures, baseline, annotations);
    }

    internal sealed class SessionDto
    {
        public int Version { get; set; }
        public string? Title { get; set; }
        public int Baseline { get; set; } = -1;
        public List<CaptureDto>? Captures { get; set; }
        public List<AnnotationDto>? Annotations { get; set; }
    }

    internal sealed class CaptureDto
    {
        public string? Label { get; set; }
        public string? ChangeNote { get; set; }
        public bool Hidden { get; set; }
        public List<string>? Messages { get; set; }
    }

    internal sealed class AnnotationDto
    {
        public int Slot { get; set; }
        public int Start { get; set; }
        public int Length { get; set; }
        public string? Text { get; set; }
    }
}

/// <summary>
/// The source generated JSON (de)serialization of the workbench session file (no reflection, so it also works trimmed).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WorkbenchSessionFile.SessionDto))]
internal sealed partial class WorkbenchJsonContext : JsonSerializerContext;
