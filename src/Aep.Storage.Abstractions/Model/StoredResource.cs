namespace Aep.Storage.Abstractions.Model;

/// <summary>
/// A persisted resource instance: the standard AEP fields plus the user-defined
/// property values. <see cref="Fields"/> holds only user-defined properties;
/// the standard fields are modeled explicitly.
/// </summary>
public sealed class StoredResource
{
    public required string Id { get; init; }

    /// <summary>
    /// System-assigned unique identifier (AEP-148): output-only, immutable, and stable for the
    /// resource's lifetime — distinct from <see cref="Id"/>, which can be reused after deletion.
    /// Null only for rows created before the field existed.
    /// </summary>
    public string? Uid { get; init; }

    /// <summary>The AEP resource name, e.g. <c>publishers/acme/books/1984</c>.</summary>
    public required string Path { get; init; }

    /// <summary>RFC 3339 creation timestamp.</summary>
    public required string CreateTime { get; set; }

    /// <summary>RFC 3339 last-update timestamp.</summary>
    public required string UpdateTime { get; set; }

    /// <summary>User-defined property values, keyed by property name.</summary>
    public Dictionary<string, object?> Fields { get; init; } = new();
}
