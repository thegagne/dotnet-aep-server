using System.Text.Json;
using Aep.Server.Backend;
using Aep.Server.Configuration;
using Aep.Server.Http;
using Aep.Storage.Abstractions.Filtering;
using Aep.Storage.Abstractions.Model;
using Microsoft.AspNetCore.Mvc;

namespace Aep.Server.Controllers;

/// <summary>
/// A single controller serving the AEP standard methods for every resource. It is a thin
/// adapter: it parses the HTTP request (body, query, route) into a backend request, calls
/// the (possibly decorated) <see cref="IResourceBackend"/>, and shapes the response. All
/// domain logic — validation results, storage, hooks — lives in the backend. The resource
/// is identified by the <c>resourceKey</c> route data token; routing is conventional and
/// registered at startup (see <see cref="Routing.ResourceRouteRegistration"/>).
/// </summary>
public sealed class ResourceController(
    IResourceRegistry registry, IResourceBackend backend, PageTokenProtector pageTokens) : ControllerBase
{
    // GET {parents}/{plural}/{id}
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var resource = CurrentResource();
        var routeValues = RouteValues();
        var path = resource.BuildResourceName(routeValues);

        RejectUnsupportedConditionals();

        var response = await backend.GetAsync(new GetRequest
        {
            Resource = resource, Http = HttpContext, RouteValues = routeValues, Path = path, CancellationToken = ct,
        });
        if (response.Resource is null)
            throw new ResourceNotFoundException(path);

        var ifMatch = Request.Headers.IfMatch.ToString();
        if (!string.IsNullOrEmpty(ifMatch) && !ETag.Matches(ifMatch, response.Resource))
            throw PreconditionFailed(path);

        SetETag(response.Resource);
        return Ok(ResourceResponse.ToBody(response.Resource, resource));
    }

    // GET {parents}/{plural}
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var resource = CurrentResource();
        var routeValues = RouteValues();

        var response = await backend.ListAsync(new ListRequest
        {
            Resource = resource,
            Http = HttpContext,
            RouteValues = routeValues,
            Path = CollectionPath(resource, routeValues),
            ParentIds = resource.DirectParentIds(routeValues),
            Options = BuildListOptions(resource),
            CancellationToken = ct,
        });

        // The store returns the raw cursor; hand the client an opaque, resource-bound token.
        var nextToken = response.Result.NextPageToken is { } cursor
            ? pageTokens.Protect(resource.Singular, cursor)
            : null;
        return Ok(ResourceResponse.ToListBody(response.Result.Items, nextToken, resource));
    }

    // POST {parents}/{plural}
    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var resource = CurrentResource();
        var routeValues = RouteValues();
        var fields = SchemaValidator.ValidateForWrite(resource, await ReadBodyAsync());

        var requestedId = Request.Query["id"].ToString();
        var id = resource.Methods.SupportsUserSettableCreate && !string.IsNullOrEmpty(requestedId)
            ? requestedId
            : GenerateId();

        var nameValues = new Dictionary<string, string>(routeValues, StringComparer.Ordinal)
        {
            [resource.IdParamName] = id,
        };

        var response = await backend.CreateAsync(new CreateRequest
        {
            Resource = resource,
            Http = HttpContext,
            RouteValues = nameValues,
            Path = resource.BuildResourceName(nameValues),
            Id = id,
            ParentIds = resource.DirectParentIds(routeValues),
            Fields = fields,
            CancellationToken = ct,
        });
        SetETag(response.Resource);
        return Ok(ResourceResponse.ToBody(response.Resource, resource));
    }

    // PATCH {parents}/{plural}/{id}
    [HttpPatch]
    public async Task<IActionResult> Update(CancellationToken ct)
    {
        var resource = CurrentResource();
        var routeValues = RouteValues();
        var patch = SchemaValidator.ValidateForPatch(resource, await ReadBodyAsync());
        var path = resource.BuildResourceName(routeValues);

        var response = await backend.UpdateAsync(new UpdateRequest
        {
            Resource = resource,
            Http = HttpContext,
            RouteValues = routeValues,
            Path = path,
            Patch = patch,
            ExpectedUpdateTime = await ResolvePreconditionAsync(resource, routeValues, path, ct),
            CancellationToken = ct,
        });
        SetETag(response.Resource);
        return Ok(ResourceResponse.ToBody(response.Resource, resource));
    }

    // PUT {parents}/{plural}/{id} — declarative create-or-replace (AEP-137)
    [HttpPut]
    public async Task<IActionResult> Apply(CancellationToken ct)
    {
        var resource = CurrentResource();
        var routeValues = RouteValues();
        var fields = SchemaValidator.ValidateForWrite(resource, await ReadBodyAsync());

        var path = resource.BuildResourceName(routeValues);
        var response = await backend.ApplyAsync(new ApplyRequest
        {
            Resource = resource,
            Http = HttpContext,
            RouteValues = routeValues,
            Path = path,
            ParentIds = resource.DirectParentIds(routeValues),
            Fields = fields,
            ExpectedUpdateTime = await ResolvePreconditionAsync(resource, routeValues, path, ct),
            CancellationToken = ct,
        });
        SetETag(response.Resource);
        return Ok(ResourceResponse.ToBody(response.Resource, resource));
    }

    // DELETE {parents}/{plural}/{id}
    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        var resource = CurrentResource();
        var routeValues = RouteValues();
        var path = resource.BuildResourceName(routeValues);

        await backend.DeleteAsync(new DeleteRequest
        {
            Resource = resource,
            Http = HttpContext,
            RouteValues = routeValues,
            Path = path,
            ExpectedUpdateTime = await ResolvePreconditionAsync(resource, routeValues, path, ct),
            CancellationToken = ct,
        });
        return NoContent();
    }

    // Any verb on a resource declared `not_implemented` — the resource is a routing parent only.
    public IActionResult NotImplemented() =>
        throw new AepStatusException(
            StatusCodes.Status501NotImplemented,
            $"the \"{CurrentResource().Singular}\" resource is not implemented");

    // ---- preconditions (AEP-154) ----

    /// <summary>
    /// Resolves an <c>If-Match</c> precondition for a mutation: returns the stored update
    /// timestamp to guard the write on (so the actual write is atomic), or null when no
    /// <c>If-Match</c> was sent. Throws 412 if the resource is absent or its ETag doesn't match.
    /// </summary>
    private async Task<string?> ResolvePreconditionAsync(
        ResourceDefinition resource, Dictionary<string, string> routeValues, string path, CancellationToken ct)
    {
        RejectUnsupportedConditionals();
        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrEmpty(ifMatch))
            return null;

        var current = (await backend.GetAsync(new GetRequest
        {
            Resource = resource, Http = HttpContext, RouteValues = routeValues, Path = path, CancellationToken = ct,
        })).Resource;
        if (current is null || !ETag.Matches(ifMatch, current))
            throw PreconditionFailed(path);
        return current.UpdateTime;
    }

    /// <summary>
    /// We support <c>If-Match</c> only. Per AEP-154, an unsupported conditional header
    /// (<c>If-None-Match</c>) must be rejected rather than silently ignored.
    /// </summary>
    private void RejectUnsupportedConditionals()
    {
        if (!string.IsNullOrEmpty(Request.Headers.IfNoneMatch.ToString()))
            throw new ResourceValidationException("the If-None-Match conditional header is not supported");
    }

    private void SetETag(StoredResource stored) => Response.Headers.ETag = ETag.Compute(stored);

    private static AepStatusException PreconditionFailed(string path) =>
        new(StatusCodes.Status412PreconditionFailed, $"the If-Match precondition for \"{path}\" failed");

    // ---- HTTP parsing helpers ----

    private ResourceDefinition CurrentResource()
    {
        var key = (string)RouteData.DataTokens["resourceKey"]!;
        return registry.Get(key);
    }

    private Dictionary<string, string> RouteValues()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in RouteData.Values)
            if (key is not ("controller" or "action") && value is not null)
                values[key] = value.ToString()!;
        return values;
    }

    /// <summary>The AEP collection name for a List/Create scope, e.g. <c>publishers/acme/books</c>.</summary>
    private static string CollectionPath(ResourceDefinition resource, IReadOnlyDictionary<string, string> routeValues)
    {
        var elems = resource.PatternElems;
        var parts = new List<string>(elems.Count - 1);
        for (var i = 0; i < elems.Count - 1; i++)
        {
            var e = elems[i];
            parts.Add(e.StartsWith('{') ? routeValues.GetValueOrDefault(e.Trim('{', '}'), "") : e);
        }
        return string.Join('/', parts);
    }

    private ListOptions BuildListOptions(ResourceDefinition resource)
    {
        var query = Request.Query;

        var pageSize = ListOptions.DefaultPageSize;
        var maxPageSize = query["max_page_size"].ToString();
        if (!string.IsNullOrEmpty(maxPageSize))
        {
            if (!int.TryParse(maxPageSize, out var ps))
                throw new ResourceValidationException("max_page_size must be an integer");
            if (ps < 0)
                throw new ResourceValidationException("max_page_size must not be negative");
            if (ps > 0)
                pageSize = ps; // 0 means "unspecified" -> default
        }

        var skip = 0;
        if (resource.Methods.SupportsSkip && int.TryParse(query["skip"], out var s) && s > 0)
            skip = s;

        FilterExpression? filter = null;
        if (resource.Methods.SupportsFilter)
        {
            try { filter = FilterParser.Parse(query["filter"]); }
            catch (FilterParseException ex) { throw new ResourceValidationException(ex.Message); }
        }

        // Decrypt the opaque page token to its cursor; reject forged/foreign/tampered tokens.
        string? cursor = null;
        var pageToken = query["page_token"].ToString();
        if (!string.IsNullOrEmpty(pageToken))
            cursor = pageTokens.Unprotect(resource.Singular, pageToken)
                ?? throw new ResourceValidationException("invalid page_token");

        return new ListOptions
        {
            PageSize = pageSize,
            PageToken = cursor,
            Skip = skip,
            Filter = filter,
        };
    }

    private async Task<JsonElement> ReadBodyAsync()
    {
        // Buffering is enabled upstream, so rewind in case a value-provider already read the body.
        if (Request.Body.CanSeek)
            Request.Body.Position = 0;

        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text))
            return default; // Undefined => treated as an empty object

        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new ResourceValidationException("invalid JSON in request body");
        }
    }

    private static string GenerateId() =>
        DateTime.UtcNow.Ticks.ToString("x16") + Random.Shared.Next(0x10000).ToString("x4");
}
