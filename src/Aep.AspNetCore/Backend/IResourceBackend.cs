namespace Aep.Server.Backend;

/// <summary>
/// The operation interface the HTTP layer calls — one method per AEP standard method.
/// The built-in <see cref="DefaultResourceBackend"/> implements validation results,
/// storage, and hooks. Wrap it with <c>DecorateResourceBackend(...)</c> to add your own
/// logic: a decorator implements this interface, holds the inner backend, and may
/// transform the request, call (or not call) <c>inner</c>, transform the response, or
/// wrap the call (transaction, retry, cache, external service).
/// </summary>
public interface IResourceBackend
{
    Task<ListResponse> ListAsync(ListRequest request);
    Task<GetResponse> GetAsync(GetRequest request);
    Task<CreateResponse> CreateAsync(CreateRequest request);
    Task<UpdateResponse> UpdateAsync(UpdateRequest request);
    Task<ApplyResponse> ApplyAsync(ApplyRequest request);
    Task<DeleteResponse> DeleteAsync(DeleteRequest request);
}
