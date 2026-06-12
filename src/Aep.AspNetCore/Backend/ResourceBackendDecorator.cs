namespace Aep.Server.Backend;

/// <summary>
/// Convenience base for a backend decorator: every method delegates to <see cref="Backend"/>
/// by default, so you override only the operations you care about — analogous to embedding
/// the base backend and overriding a method.
/// <code>
/// sealed class MyBackend(IResourceBackend backend) : ResourceBackendDecorator(backend)
/// {
///     public override async Task&lt;CreateResponse&gt; CreateAsync(CreateRequest request)
///     {
///         // pre: validate, enrich, call an external service
///         var response = await Backend.CreateAsync(request);
///         // post: emit event, update cache, ...
///         return response;
///     }
/// }
/// </code>
/// </summary>
public abstract class ResourceBackendDecorator(IResourceBackend backend) : IResourceBackend
{
    /// <summary>The wrapped backend (the next decorator, or the built-in default).</summary>
    protected IResourceBackend Backend { get; } = backend;

    public virtual Task<ListResponse> ListAsync(ListRequest request) => Backend.ListAsync(request);
    public virtual Task<GetResponse> GetAsync(GetRequest request) => Backend.GetAsync(request);
    public virtual Task<CreateResponse> CreateAsync(CreateRequest request) => Backend.CreateAsync(request);
    public virtual Task<UpdateResponse> UpdateAsync(UpdateRequest request) => Backend.UpdateAsync(request);
    public virtual Task<ApplyResponse> ApplyAsync(ApplyRequest request) => Backend.ApplyAsync(request);
    public virtual Task<DeleteResponse> DeleteAsync(DeleteRequest request) => Backend.DeleteAsync(request);
}
