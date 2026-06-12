using Aep.Storage.Abstractions.Model;

namespace Aep.Server.Http;

/// <summary>Shapes stored resources into AEP-standard JSON response bodies.</summary>
public static class ResourceResponse
{
    /// <summary>
    /// Builds a resource body: the standard fields (<c>id</c>, <c>path</c>,
    /// <c>create_time</c>, <c>update_time</c>) followed by user-defined fields.
    /// </summary>
    public static Dictionary<string, object?> ToBody(StoredResource stored, ResourceDefinition resource)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = stored.Id,
            ["path"] = stored.Path,
            ["create_time"] = stored.CreateTime,
            ["update_time"] = stored.UpdateTime,
        };
        if (stored.Uid is not null)
            body["uid"] = stored.Uid; // server-assigned; absent only on pre-existing rows
        foreach (var (name, prop) in resource.Schema.Properties)
            if (!prop.InputOnly && stored.Fields.TryGetValue(name, out var value))
                body[name] = value; // input-only fields are write-only; never returned (AEP-203)
        return body;
    }

    /// <summary>
    /// Builds an AEP-132 list body: <c>results</c> plus an optional <c>next_page_token</c>
    /// (the already-protected, opaque token; null/empty when there are no more results).
    /// </summary>
    public static Dictionary<string, object?> ToListBody(
        IReadOnlyList<StoredResource> items, string? nextPageToken, ResourceDefinition resource)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["results"] = items.Select(item => ToBody(item, resource)).ToList(),
        };
        if (!string.IsNullOrEmpty(nextPageToken))
            body["next_page_token"] = nextPageToken;
        return body;
    }
}
