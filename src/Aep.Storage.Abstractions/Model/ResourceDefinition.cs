namespace Aep.Storage.Abstractions.Model;

/// <summary>
/// A single resource declared in <c>resources.yaml</c>, plus its computed URL
/// pattern. Pattern info (<see cref="PatternElems"/>) is filled in by the
/// resource registry once all resources are known, so parent plurals can be
/// resolved into the path.
/// </summary>
public sealed class ResourceDefinition
{
    public required string Singular { get; init; }
    public required string Plural { get; init; }

    /// <summary>Singular names of parent resources (AEP-124). v1 follows the first parent for nesting.</summary>
    public IReadOnlyList<string> Parents { get; init; } = [];

    public required ResourceSchema Schema { get; init; }
    public ResourceMethods Methods { get; init; } = new();

    /// <summary>
    /// When true, this resource is a routing parent only: its own standard methods return
    /// <c>501 Not Implemented</c>, but its child resources are still served at their nested
    /// paths. Useful when a parent collection is owned by another system and exists here only
    /// as a path segment.
    /// </summary>
    public bool NotImplemented { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// Secondary indexes to create on this resource, declared in code (e.g. via
    /// <c>AddResourceIndex</c>). Relational backends create a btree index per entry;
    /// others may ignore them. Defaults to none.
    /// </summary>
    public IReadOnlyList<ResourceIndex> Indexes { get; set; } = [];

    /// <summary>
    /// Flattened URL pattern, alternating collection/id segments, e.g.
    /// <c>["publishers", "{publisher_id}", "books", "{book_id}"]</c>. Populated
    /// by the registry. Brace segments are route parameters.
    /// </summary>
    public IReadOnlyList<string> PatternElems { get; set; } = [];

    /// <summary>Route template for the collection, e.g. <c>publishers/{publisher_id}/books</c>.</summary>
    public string CollectionPattern => string.Join('/', PatternElems.Take(PatternElems.Count - 1));

    /// <summary>Route template for a single item, e.g. <c>publishers/{publisher_id}/books/{book_id}</c>.</summary>
    public string ItemPattern => string.Join('/', PatternElems);

    /// <summary>The id route-parameter name for this resource, e.g. <c>book_id</c>.</summary>
    public string IdParamName => Unbrace(PatternElems[^1]);

    /// <summary>Route-parameter names for all ancestors, in order (e.g. <c>publisher_id</c>).</summary>
    public IEnumerable<string> ParentIdParamNames
    {
        get
        {
            for (var i = 1; i < PatternElems.Count - 1; i += 2)
                yield return Unbrace(PatternElems[i]);
        }
    }

    /// <summary>Sanitized route-parameter name for a parent singular (hyphens -> underscores).</summary>
    public static string ParamNameFor(string singular) => singular.Replace('-', '_') + "_id";

    /// <summary>
    /// AEP-159 wildcard collection id. A parent id of <c>-</c> in a List request lists across all
    /// values of that parent (e.g. <c>GET /publishers/-/books</c> lists books of every publisher).
    /// </summary>
    public const string WildcardCollectionId = "-";

    /// <summary>
    /// The direct-parent ids for this resource (e.g. <c>{ publisher_id = "acme" }</c>),
    /// pulled from the supplied route values. Used to scope lists and to set foreign keys.
    /// </summary>
    public IReadOnlyDictionary<string, string> DirectParentIds(IReadOnlyDictionary<string, string> routeValues)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parent in Parents)
        {
            var param = ParamNameFor(parent);
            if (routeValues.TryGetValue(param, out var value))
                ids[param] = value;
        }
        return ids;
    }

    /// <summary>
    /// Builds the AEP resource name (path) from route values, e.g.
    /// <c>publishers/acme/books/1984</c>.
    /// </summary>
    public string BuildResourceName(IReadOnlyDictionary<string, string> routeValues)
    {
        var parts = new List<string>(PatternElems.Count);
        foreach (var elem in PatternElems)
        {
            if (elem.StartsWith('{'))
            {
                var name = Unbrace(elem);
                parts.Add(routeValues.TryGetValue(name, out var v) ? v : string.Empty);
            }
            else
            {
                parts.Add(elem);
            }
        }
        return string.Join('/', parts);
    }

    private static string Unbrace(string s) => s.Trim('{', '}');
}
