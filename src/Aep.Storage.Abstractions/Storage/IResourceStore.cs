using Aep.Storage.Abstractions.Model;

namespace Aep.Storage.Abstractions.Storage;

/// <summary>
/// Persistence contract for AEP resources. One implementation per backend
/// (SQLite by default; Postgres/DynamoDB later). All resource instances are
/// addressed by their AEP resource name (<see cref="StoredResource.Path"/>).
/// </summary>
public interface IResourceStore
{
    /// <summary>
    /// Ensures backing storage exists for every resource (e.g. creating tables and
    /// indexes). Idempotent; called once at startup.
    /// </summary>
    Task EnsureSchemaAsync(IEnumerable<ResourceDefinition> resources, CancellationToken ct = default);

    /// <summary>Fetches a single resource by path, or null if it does not exist.</summary>
    Task<StoredResource?> GetAsync(ResourceDefinition resource, string path, CancellationToken ct = default);

    /// <summary>Lists resources within a parent scope, with pagination and optional filtering.</summary>
    Task<ListResult> ListAsync(
        ResourceDefinition resource,
        IReadOnlyDictionary<string, string> parentIds,
        ListOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts a new resource. <paramref name="directParentIds"/> maps each direct
    /// parent's id parameter (e.g. <c>publisher_id</c>) to its value.
    /// </summary>
    /// <exception cref="DuplicateResourceException">A resource with the same path already exists.</exception>
    Task InsertAsync(
        ResourceDefinition resource,
        StoredResource stored,
        IReadOnlyDictionary<string, string> directParentIds,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the given user-defined fields and the update timestamp on an existing
    /// resource. Returns false if the resource does not exist — or, when
    /// <paramref name="expectedUpdateTime"/> is supplied, if the stored update timestamp no
    /// longer matches it (an optimistic-concurrency / precondition failure, AEP-154). The
    /// match is part of the same write statement, so the check is atomic.
    /// </summary>
    Task<bool> UpdateAsync(
        ResourceDefinition resource,
        string path,
        IReadOnlyDictionary<string, object?> fields,
        string updateTime,
        string? expectedUpdateTime = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a resource by path. Returns false if it did not exist — or, when
    /// <paramref name="expectedUpdateTime"/> is supplied, if the stored update timestamp no
    /// longer matches it (precondition failure, AEP-154).
    /// </summary>
    Task<bool> DeleteAsync(
        ResourceDefinition resource,
        string path,
        string? expectedUpdateTime = null,
        CancellationToken ct = default);
}
