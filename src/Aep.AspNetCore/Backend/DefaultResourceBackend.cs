using Aep.Server.Http;
using Aep.Storage.Abstractions.Model;
using Aep.Storage.Abstractions.Storage;

namespace Aep.Server.Backend;

/// <summary>
/// The built-in backend: the standard AEP operations implemented against
/// <see cref="IResourceStore"/>. Wrap it with per-method interceptors
/// (<c>OnCreate</c>/…) or a global decorator (<c>DecorateResourceBackend</c>) to add logic.
/// </summary>
public sealed class DefaultResourceBackend(IResourceStore store) : IResourceBackend
{
    public async Task<ListResponse> ListAsync(ListRequest request)
    {
        var result = await store.ListAsync(request.Resource, request.ParentIds, request.Options, request.CancellationToken);
        return new ListResponse { Result = result };
    }

    public async Task<GetResponse> GetAsync(GetRequest request)
    {
        var stored = await store.GetAsync(request.Resource, request.Path, request.CancellationToken);
        return new GetResponse { Resource = stored };
    }

    public async Task<CreateResponse> CreateAsync(CreateRequest request)
    {
        var now = Rfc3339Now();
        var stored = new StoredResource
        {
            Id = request.Id,
            Uid = NewUid(),
            Path = request.Path,
            CreateTime = now,
            UpdateTime = now,
        };
        foreach (var (name, value) in request.Fields)
            stored.Fields[name] = value;

        await store.InsertAsync(request.Resource, stored, request.ParentIds, request.CancellationToken);
        return new CreateResponse { Resource = stored };
    }

    public async Task<UpdateResponse> UpdateAsync(UpdateRequest request)
    {
        var ok = await store.UpdateAsync(
            request.Resource, request.Path, request.Patch, Rfc3339Now(), request.ExpectedUpdateTime, request.CancellationToken);
        if (!ok)
            throw FailedWrite(request.Path, request.ExpectedUpdateTime);

        var stored = await store.GetAsync(request.Resource, request.Path, request.CancellationToken);
        return new UpdateResponse { Resource = stored! };
    }

    public async Task<ApplyResponse> ApplyAsync(ApplyRequest request)
    {
        var now = Rfc3339Now();
        var existing = await store.GetAsync(request.Resource, request.Path, request.CancellationToken);

        StoredResource result;
        if (existing is null)
        {
            // An If-Match precondition implies the resource must already exist (AEP-154 / RFC 9110).
            if (request.ExpectedUpdateTime is not null)
                throw Precondition(request.Path);
            var stored = new StoredResource
            {
                Id = request.RouteValues[request.Resource.IdParamName],
                Uid = NewUid(),
                Path = request.Path,
                CreateTime = now,
                UpdateTime = now,
            };
            foreach (var (name, value) in request.Fields)
                stored.Fields[name] = value;
            await store.InsertAsync(request.Resource, stored, request.ParentIds, request.CancellationToken);
            result = stored;
        }
        else
        {
            // Full replacement: every user-defined field is set, clearing any omitted ones.
            var replacement = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var name in UserPropertyNames(request.Resource))
                replacement[name] = request.Fields.GetValueOrDefault(name);
            var ok = await store.UpdateAsync(
                request.Resource, request.Path, replacement, now, request.ExpectedUpdateTime, request.CancellationToken);
            if (!ok)
                throw FailedWrite(request.Path, request.ExpectedUpdateTime);
            result = (await store.GetAsync(request.Resource, request.Path, request.CancellationToken))!;
        }

        return new ApplyResponse { Resource = result };
    }

    public async Task<DeleteResponse> DeleteAsync(DeleteRequest request)
    {
        if (!await store.DeleteAsync(request.Resource, request.Path, request.ExpectedUpdateTime, request.CancellationToken))
            throw FailedWrite(request.Path, request.ExpectedUpdateTime);
        return new DeleteResponse();
    }

    // A write returned "not applied": no such resource (404), unless an If-Match precondition
    // was in play, in which case it lost the optimistic-concurrency race (412).
    private static AepException FailedWrite(string path, string? expectedUpdateTime) =>
        expectedUpdateTime is null ? new ResourceNotFoundException(path) : Precondition(path);

    private static AepStatusException Precondition(string path) =>
        new(StatusCodes.Status412PreconditionFailed, $"the If-Match precondition for \"{path}\" failed");

    private static IEnumerable<string> UserPropertyNames(ResourceDefinition resource) =>
        resource.Schema.Properties.Keys.Where(n =>
            n is not ("id" or "uid" or "path" or "create_time" or "update_time"));

    // Microsecond precision so two writes in quick succession get distinct timestamps — the
    // update timestamp doubles as the optimistic-concurrency version token (AEP-154).
    private static string Rfc3339Now() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");

    private static string NewUid() => Guid.NewGuid().ToString();
}
