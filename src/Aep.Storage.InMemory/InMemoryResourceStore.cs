using System.Text;
using Aep.Storage.Abstractions.Filtering;
using Aep.Storage.Abstractions.Model;
using Aep.Storage.Abstractions.Storage;

namespace Aep.Storage.InMemory;

/// <summary>
/// A process-memory <see cref="IResourceStore"/> backed by dictionaries. Requires no
/// native libraries or external services, so it's ideal for tests, local dev, and
/// ephemeral deployments. Data is lost when the process exits. Thread-safe via a
/// single lock; filtering uses <see cref="FilterEvaluator"/> in-process.
/// </summary>
public sealed class InMemoryResourceStore : IResourceStore
{
    private sealed record Entry(StoredResource Resource, IReadOnlyDictionary<string, string> ParentIds);

    private static readonly string[] StandardFields = ["id", "uid", "path", "create_time", "update_time"];

    private readonly object _gate = new();
    // plural -> (path -> entry)
    private readonly Dictionary<string, Dictionary<string, Entry>> _tables = new(StringComparer.Ordinal);

    public Task EnsureSchemaAsync(IEnumerable<ResourceDefinition> resources, CancellationToken ct = default)
    {
        lock (_gate)
            foreach (var r in resources)
                _tables.TryAdd(r.Plural, new Dictionary<string, Entry>(StringComparer.Ordinal));
        return Task.CompletedTask;
    }

    public Task<StoredResource?> GetAsync(ResourceDefinition resource, string path, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var table = Table(resource);
            return Task.FromResult(table.TryGetValue(path, out var e) ? Clone(e.Resource) : null);
        }
    }

    public Task<ListResult> ListAsync(
        ResourceDefinition resource,
        IReadOnlyDictionary<string, string> parentIds,
        ListOptions options,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            var allowed = AllowedFields(resource);
            FilterEvaluator.Validate(options.Filter, allowed); // fail fast on unknown fields, even with no rows
            var matches = Table(resource).Values
                .Where(e => ParentsMatch(e.ParentIds, parentIds))
                .Where(e => FilterEvaluator.Matches(options.Filter, EvalFields(e.Resource), allowed))
                .OrderBy(e => e.Resource.Id, StringComparer.Ordinal)
                .Select(e => e.Resource)
                .ToList();

            // PageToken is the raw cursor (last id); the API layer handles opacity.
            if (!string.IsNullOrEmpty(options.PageToken))
                matches = matches.Where(r => string.CompareOrdinal(r.Id, options.PageToken) > 0).ToList();

            if (options.Skip > 0)
                matches = matches.Skip(options.Skip).ToList();

            var pageSize = Math.Clamp(options.PageSize, 1, ListOptions.MaxPageSize);
            string? nextToken = null;
            if (matches.Count > pageSize)
            {
                nextToken = matches[pageSize - 1].Id;
                matches = matches.GetRange(0, pageSize);
            }

            return Task.FromResult(new ListResult
            {
                Items = matches.Select(Clone).ToList(),
                NextPageToken = nextToken,
            });
        }
    }

    public Task InsertAsync(
        ResourceDefinition resource,
        StoredResource stored,
        IReadOnlyDictionary<string, string> directParentIds,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            var table = Table(resource);
            if (table.ContainsKey(stored.Path))
                throw new DuplicateResourceException(stored.Path);

            var entry = new Entry(Normalize(stored), new Dictionary<string, string>(directParentIds, StringComparer.Ordinal));
            table[stored.Path] = entry;
        }
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(
        ResourceDefinition resource,
        string path,
        IReadOnlyDictionary<string, object?> fields,
        string updateTime,
        string? expectedUpdateTime = null,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            var table = Table(resource);
            if (!table.TryGetValue(path, out var e))
                return Task.FromResult(false);
            if (expectedUpdateTime is not null && e.Resource.UpdateTime != expectedUpdateTime)
                return Task.FromResult(false); // optimistic-concurrency guard (AEP-154)

            foreach (var (name, value) in fields)
                e.Resource.Fields[name] = JsonValue.ToClr(value);
            e.Resource.UpdateTime = updateTime;
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteAsync(
        ResourceDefinition resource, string path, string? expectedUpdateTime = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var table = Table(resource);
            if (expectedUpdateTime is not null &&
                (!table.TryGetValue(path, out var e) || e.Resource.UpdateTime != expectedUpdateTime))
                return Task.FromResult(false);
            return Task.FromResult(table.Remove(path));
        }
    }

    // ---- helpers ----

    private Dictionary<string, Entry> Table(ResourceDefinition resource)
    {
        if (!_tables.TryGetValue(resource.Plural, out var table))
            throw new InvalidOperationException($"EnsureSchemaAsync was not called for '{resource.Plural}'");
        return table;
    }

    private static bool ParentsMatch(IReadOnlyDictionary<string, string> entryParents, IReadOnlyDictionary<string, string> requested)
    {
        foreach (var (key, value) in requested)
        {
            if (value == ResourceDefinition.WildcardCollectionId)
                continue; // AEP-159: list across all values of this parent
            if (!entryParents.TryGetValue(key, out var v) || v != value)
                return false;
        }
        return true;
    }

    private static IReadOnlySet<string> AllowedFields(ResourceDefinition resource)
    {
        var set = new HashSet<string>(resource.Schema.Properties.Keys, StringComparer.Ordinal);
        set.UnionWith(StandardFields);
        return set;
    }

    /// <summary>Fields plus the standard server-managed fields, so filters may reference either.</summary>
    private static Dictionary<string, object?> EvalFields(StoredResource r)
    {
        var f = new Dictionary<string, object?>(r.Fields, StringComparer.Ordinal)
        {
            ["id"] = r.Id,
            ["path"] = r.Path,
            ["create_time"] = r.CreateTime,
            ["update_time"] = r.UpdateTime,
        };
        return f;
    }

    private static StoredResource Normalize(StoredResource s)
    {
        var stored = new StoredResource
        {
            Id = s.Id,
            Uid = s.Uid,
            Path = s.Path,
            CreateTime = s.CreateTime,
            UpdateTime = s.UpdateTime,
        };
        foreach (var (name, value) in s.Fields)
            stored.Fields[name] = JsonValue.ToClr(value);
        return stored;
    }

    private static StoredResource Clone(StoredResource s)
    {
        var copy = new StoredResource
        {
            Id = s.Id,
            Uid = s.Uid,
            Path = s.Path,
            CreateTime = s.CreateTime,
            UpdateTime = s.UpdateTime,
        };
        foreach (var (k, v) in s.Fields)
            copy.Fields[k] = v;
        return copy;
    }

}
