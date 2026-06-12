using Aep.Storage.Abstractions.Model;

namespace Aep.Server.Backend;

/// <summary>Common context for every backend request: the resource, the request, and where it points.</summary>
public abstract class ResourceRequest
{
    public required ResourceDefinition Resource { get; init; }
    public required HttpContext Http { get; init; }

    /// <summary>Parent ids + (for item ops) the resource id, from the route.</summary>
    public required IReadOnlyDictionary<string, string> RouteValues { get; init; }

    /// <summary>The AEP resource name for item ops, or the collection name for List/Create.</summary>
    public required string Path { get; init; }

    public CancellationToken CancellationToken { get; init; }

    /// <summary>Request-scoped services, for resolving dependencies inside a backend.</summary>
    public IServiceProvider Services => Http.RequestServices;
}

public sealed class ListRequest : ResourceRequest
{
    public required IReadOnlyDictionary<string, string> ParentIds { get; init; }
    public required ListOptions Options { get; init; }
}

public sealed class GetRequest : ResourceRequest;

public sealed class CreateRequest : ResourceRequest
{
    public required string Id { get; init; }
    public required IReadOnlyDictionary<string, string> ParentIds { get; init; }

    /// <summary>The validated write payload (mutable — a backend may enrich it before persisting).</summary>
    public required Dictionary<string, object?> Fields { get; init; }
}

public sealed class UpdateRequest : ResourceRequest
{
    /// <summary>The validated patch (mutable).</summary>
    public required Dictionary<string, object?> Patch { get; init; }

    /// <summary>
    /// When set (from a matched <c>If-Match</c>), the write must only apply if the stored
    /// update timestamp still equals this value, else it fails the precondition (AEP-154).
    /// </summary>
    public string? ExpectedUpdateTime { get; init; }
}

public sealed class ApplyRequest : ResourceRequest
{
    public required IReadOnlyDictionary<string, string> ParentIds { get; init; }

    /// <summary>The validated full replacement (mutable).</summary>
    public required Dictionary<string, object?> Fields { get; init; }

    /// <summary>The matched <c>If-Match</c> precondition; see <see cref="UpdateRequest.ExpectedUpdateTime"/>.</summary>
    public string? ExpectedUpdateTime { get; init; }
}

public sealed class DeleteRequest : ResourceRequest
{
    /// <summary>The matched <c>If-Match</c> precondition; see <see cref="UpdateRequest.ExpectedUpdateTime"/>.</summary>
    public string? ExpectedUpdateTime { get; init; }
}

public sealed class ListResponse
{
    public required ListResult Result { get; init; }
}

public sealed class GetResponse
{
    /// <summary>The resource, or null if it does not exist (the adapter maps null to 404).</summary>
    public required StoredResource? Resource { get; init; }
}

public sealed class CreateResponse
{
    public required StoredResource Resource { get; init; }
}

public sealed class UpdateResponse
{
    public required StoredResource Resource { get; init; }
}

public sealed class ApplyResponse
{
    public required StoredResource Resource { get; init; }
}

public sealed class DeleteResponse;
