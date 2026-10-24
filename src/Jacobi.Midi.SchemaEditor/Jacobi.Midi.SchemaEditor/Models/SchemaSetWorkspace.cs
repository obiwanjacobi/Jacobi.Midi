using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Assemblies;

namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// A loaded schema of the <see cref="SchemaSetWorkspace"/>: an open document (a file) or an embedded schema of an opened assembly.
/// </summary>
/// <param name="Document">The workspace document of the schema (read-only for an embedded schema).</param>
/// <param name="Embedded">The embedded schema; null for an open document.</param>
internal sealed record SchemaSetEntry(SchemaWorkspaceDocument Document, LoadedSchema? Embedded)
{
    public EditDocument Schema => Document.Schema;

    public string Name => Schema.Name;

    public bool IsEmbedded => Embedded is not null;

    /// <summary>How the schema is named in messages: "the open document 'X'" or "the embedded schema 'X' of Assembly".</summary>
    public string Description => Embedded is { } embedded
        ? $"the embedded schema '{Name}' of {embedded.Assembly.Name}"
        : $"the open document '{Name}'";
}

/// <summary>
/// The result of compiling a document with the loaded schemas (see <see cref="SchemaSetWorkspace.Compile(EditDocument, SchemaWorkspaceDocument?)"/>).
/// </summary>
/// <param name="Document">The compiled document.</param>
/// <param name="Schemas">The compiled schema set.</param>
/// <param name="Documents">The documents in <paramref name="Schemas"/>: the document, its includes and the other schemas that compile with them.</param>
/// <param name="Warnings">
/// The situations the user may not expect: a schema that is used instead of another one with the same name,
/// a schema that is left out (an include is not loaded or it does not compile). Empty when there are none.
/// </param>
internal sealed record SchemaSetCompilation(EditDocument Document, SchemaSet Schemas, IReadOnlyList<EditDocument> Documents, IReadOnlyList<string> Warnings);

/// <summary>
/// All loaded schemas: the open documents (files) and the embedded schemas of the opened assemblies.
/// The one place that tells which schema answers to a name: an open document is used instead of an embedded schema
/// with the same name (of two open documents, or two embedded schemas, with the same name the first is used).
/// Compiles a document with all loaded schemas (<see cref="Compile(EditDocument, SchemaWorkspaceDocument?)"/>) and keeps the status the views show:
/// whether an include is loaded (<see cref="EditDocumentRef.IsLoaded"/>) and which embedded schema is replaced (<see cref="LoadedSchema.ReplacedBy"/>).
/// The status is refreshed when a document is opened, closed or changed or an assembly is opened; <see cref="Changed"/> is raised after that.
/// </summary>
internal sealed class SchemaSetWorkspace
{
    private readonly ObservableCollection<SchemaWorkspaceDocument> _documents;
    private readonly AssemblyWorkspace _assemblies;
    private readonly HashSet<SchemaWorkspaceDocument> _observed = [];

    /// <param name="documents">The open documents.</param>
    /// <param name="assemblies">The opened assemblies with their embedded schemas.</param>
    public SchemaSetWorkspace(ObservableCollection<SchemaWorkspaceDocument> documents, AssemblyWorkspace assemblies)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(assemblies);
        _documents = documents;
        _assemblies = assemblies;
        _documents.CollectionChanged += OnDocumentsChanged;
        _assemblies.Assemblies.CollectionChanged += (_, _) => Refresh();
        ObserveDocuments();
        Refresh();
    }

    /// <summary>Raised after the status was refreshed: a schema was opened, closed, changed or renamed.</summary>
    public event EventHandler? Changed;

    /// <summary>All loaded schemas: the open documents first, then the embedded schemas in the order of the assemblies.</summary>
    public IReadOnlyList<SchemaSetEntry> Entries
        => [.. _documents.Select(document => new SchemaSetEntry(document, null)),
            .. _assemblies.Assemblies.SelectMany(assembly => assembly.Schemas).Select(schema => new SchemaSetEntry(schema.Document, schema))];

    /// <summary>
    /// The schema that answers to <paramref name="name"/>, e.g. for an include of <paramref name="requester"/>;
    /// null when it is not loaded. A schema does not include itself: the name of <paramref name="requester"/> gives null.
    /// </summary>
    public SchemaSetEntry? Find(string name, EditDocument? requester = null)
        => requester is not null && SameName(requester.Name, name) ? null : Find(Entries, name);

    /// <summary>The entry of <paramref name="schema"/>; null when it is not a loaded schema (e.g. a copy).</summary>
    public SchemaSetEntry? FindEntry(EditDocument schema)
        => Entries.FirstOrDefault(entry => ReferenceEquals(entry.Schema, schema));

    /// <summary>
    /// Compiles <paramref name="document"/> with all loaded schemas: for every other name the schema that answers to it (see <see cref="Find"/>).
    /// The document itself answers to its own name, so an embedded schema, or a copy of an open document, can be compiled.
    /// A schema that is not used because of that, or that is left out, is reported in the <see cref="SchemaSetCompilation.Warnings"/>.
    /// </summary>
    /// <param name="copyOf">
    /// The open document <paramref name="document"/> is an edited copy of (e.g. the target schema of the workbench);
    /// it is left out without a warning.
    /// </param>
    /// <exception cref="InvalidOperationException">An include of the document is not loaded or the document does not compile.</exception>
    public SchemaSetCompilation Compile(EditDocument document, SchemaWorkspaceDocument? copyOf = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var entries = Entries;
        var others = entries.Select(entry => entry.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !SameName(name, document.Name))
            .Select(name => Find(entries, name)!.Schema);
        var compilation = Compile(document, others);

        var warnings = new List<string>();
        var self = entries.FirstOrDefault(entry => ReferenceEquals(entry.Schema, document));
        foreach (var used in compilation.Documents)
        {
            var description = ReferenceEquals(used, document) && self is null
                ? $"the edited copy of '{used.Name}'"
                : (entries.First(entry => ReferenceEquals(entry.Schema, used))).Description;
            foreach (var other in entries.Where(entry => SameName(entry.Name, used.Name) && !ReferenceEquals(entry.Schema, used)
                && !ReferenceEquals(entry.Document, copyOf)))
            {
                warnings.Add($"{Capitalize(description)} is used instead of {other.Description}.");
            }
        }
        return compilation with { Warnings = [.. warnings, .. compilation.Warnings] };
    }

    /// <summary>
    /// Compiles <paramref name="document"/> and (transitively) the documents it includes, found in <paramref name="schemas"/> by name,
    /// together with all other documents of <paramref name="schemas"/> that can be compiled with them:
    /// they can hold the bodies of its open choices (a device schema defines the bodies for the open choice of a vendor schema).
    /// The document answers to its own name; of the other documents with the same name the first is used.
    /// A document that is left out (an include is not loaded or it does not compile) is reported in the <see cref="SchemaSetCompilation.Warnings"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">An include of the document is not loaded or the document does not compile.</exception>
    public static SchemaSetCompilation Compile(EditDocument document, IEnumerable<EditDocument> schemas)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(schemas);
        // one document per name: the document itself first
        var candidates = new List<EditDocument> { document };
        foreach (var schema in schemas)
        {
            if (!candidates.Any(candidate => SameName(candidate.Name, schema.Name)))
            {
                candidates.Add(schema);
            }
        }

        var documents = GetClosure(document, candidates, out var missing);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The schema '{document.Name}' cannot be compiled: the included schema(s) {Names(missing)} are not loaded.");
        }

        var warnings = new List<string>();
        // the closure of each other document (the document first)
        var others = new List<List<EditDocument>>();
        foreach (var candidate in candidates.Where(candidate => !documents.Contains(candidate)))
        {
            var closure = GetClosure(candidate, candidates, out var candidateMissing);
            if (candidateMissing.Count > 0)
            {
                warnings.Add($"The schema '{candidate.Name}' is left out: the included schema(s) {Names(candidateMissing)} are not loaded.");
                continue;
            }
            others.Add(closure);
        }

        var all = new List<EditDocument>(documents);
        all.AddRange(others.SelectMany(closure => closure).Distinct().Where(item => !all.Contains(item)));
        if (TryCompile(all, out var compiled, out _))
        {
            return new SchemaSetCompilation(document, compiled, all, warnings);
        }

        // another document that does not compile must not prevent compiling the document itself: add them one by one
        if (!TryCompile(documents, out compiled, out var error))
        {
            throw new InvalidOperationException($"The schema '{document.Name}' does not compile: {error.Message}", error);
        }
        var accepted = new List<EditDocument>(documents);
        foreach (var closure in others)
        {
            var added = closure.Where(item => !accepted.Contains(item)).ToList();
            if (added.Count == 0)
            {
                continue;
            }
            if (TryCompile([.. accepted, .. added], out var extended, out var otherError))
            {
                accepted.AddRange(added);
                compiled = extended;
            }
            else
            {
                warnings.Add($"The schema '{closure[0].Name}' is left out: it does not compile: {otherError.Message}");
            }
        }
        return new SchemaSetCompilation(document, compiled, accepted, warnings);
    }

    /// <summary>
    /// The note for the details of <paramref name="document"/>: which schema with the same name is used instead of it,
    /// or which schemas it is used instead of, when schemas are compiled; null when no other schema has its name.
    /// </summary>
    public string? GetSourceNote(SchemaWorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var entries = Entries;
        if (entries.FirstOrDefault(entry => ReferenceEquals(entry.Document, document)) is not { } entry)
        {
            return null;
        }
        var used = Find(entries, entry.Name)!;
        if (used != entry)
        {
            return $"{Capitalize(used.Description)} is used instead of this schema when other schemas are compiled (Instantiate, Match, Workbench).";
        }
        var replaced = entries.Where(other => other != entry && SameName(other.Name, entry.Name)).Select(other => other.Description).ToList();
        return replaced.Count == 0
            ? null
            : $"This schema is used instead of {String.Join(", ", replaced)} when schemas are compiled (Instantiate, Match, Workbench).";
    }

    /// <summary>
    /// Flags the includes of all loaded schemas that are not loaded and the embedded schemas that another schema replaces,
    /// then raises <see cref="Changed"/>.
    /// </summary>
    private void Refresh()
    {
        var entries = Entries;
        foreach (var entry in entries)
        {
            foreach (var include in entry.Schema.Includes)
            {
                include.IsLoaded = !SameName(include.Schema, entry.Name) && Find(entries, include.Schema) is not null;
            }
            if (entry.Embedded is { } embedded)
            {
                var used = Find(entries, entry.Name)!;
                embedded.ReplacedBy = used == entry ? null : used.Description;
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ObserveDocuments();
        Refresh();
    }

    // follows the open documents: a rename or a changed include changes the status
    private void ObserveDocuments()
    {
        foreach (var closed in _observed.Where(document => !_documents.Contains(document)).ToList())
        {
            closed.PropertyChanged -= OnDocumentPropertyChanged;
            _observed.Remove(closed);
        }
        foreach (var opened in _documents.Where(_observed.Add))
        {
            opened.PropertyChanged += OnDocumentPropertyChanged;
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // the pin state only matters for the schema tree
        if (e.PropertyName != nameof(SchemaWorkspaceDocument.IsPinned))
        {
            Refresh();
        }
    }

    private static SchemaSetEntry? Find(IReadOnlyList<SchemaSetEntry> entries, string name)
        => entries.FirstOrDefault(entry => SameName(entry.Name, name));

    /// <summary>
    /// The document and (transitively) its included documents that are in <paramref name="candidates"/>;
    /// <paramref name="missing"/> receives the names of the included schemas that are not.
    /// </summary>
    private static List<EditDocument> GetClosure(EditDocument document, List<EditDocument> candidates, out List<string> missing)
    {
        var documents = new List<EditDocument>();
        missing = [];
        var queue = new Queue<EditDocument>();
        queue.Enqueue(document);
        while (queue.TryDequeue(out var current))
        {
            if (documents.Contains(current))
            {
                continue;
            }
            documents.Add(current);
            foreach (var include in current.Includes)
            {
                var included = candidates.Find(other => !ReferenceEquals(other, current) && SameName(other.Name, include.Schema));
                if (included is not null)
                {
                    queue.Enqueue(included);
                }
                else if (!missing.Contains(include.Schema, StringComparer.OrdinalIgnoreCase))
                {
                    missing.Add(include.Schema);
                }
            }
        }
        return documents;
    }

    private static bool TryCompile(List<EditDocument> documents,
        [NotNullWhen(true)] out SchemaSet? schemas, [NotNullWhen(false)] out Exception? error)
    {
        try
        {
            var builder = new SchemaSetBuilder();
            foreach (var item in documents)
            {
                builder.AddDocument(EditNodeMapper.ToSchemaDocument(item));
            }
            schemas = builder.Compile();
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            schemas = null;
            error = exception;
            return false;
        }
    }

    private static bool SameName(string left, string right) => String.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Names(IEnumerable<string> names) => String.Join(", ", names.Select(name => $"'{name}'"));

    private static string Capitalize(string text) => text.Length == 0 ? text : $"{Char.ToUpperInvariant(text[0])}{text[1..]}";
}
