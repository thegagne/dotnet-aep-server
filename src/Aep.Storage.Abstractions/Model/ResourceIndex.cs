namespace Aep.Storage.Abstractions.Model;

/// <summary>
/// A secondary index over one or more fields of a resource, declared in code (not in
/// resources.yaml). Relational backends (SQLite, Postgres) create a btree index per entry to
/// speed up filtering and ordering; backends without server-side filtering may ignore it.
/// </summary>
public sealed class ResourceIndex
{
    /// <summary>The field(s) covered, in order. A single field is the common case; more than one is a composite index.</summary>
    public required IReadOnlyList<string> Fields { get; init; }
}
