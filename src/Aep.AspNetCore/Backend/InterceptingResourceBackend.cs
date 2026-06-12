using Microsoft.Extensions.Options;

namespace Aep.Server.Backend;

/// <summary>
/// Dispatches each operation through any per-(resource, method) interceptors registered via
/// <c>OnCreate</c>/<c>OnGet</c>/… before reaching the wrapped backend. When none are registered
/// for the resource+method, it calls straight through. Sits between user decorators and the
/// built-in <see cref="DefaultResourceBackend"/>.
/// </summary>
public sealed class InterceptingResourceBackend(IResourceBackend backend, IOptions<ResourceInterceptorOptions> options)
    : ResourceBackendDecorator(backend)
{
    private readonly ResourceInterceptorOptions _options = options.Value;

    public override Task<ListResponse> ListAsync(ListRequest request)
        => _options.List.Run(request.Resource.Singular, request, Backend.ListAsync);

    public override Task<GetResponse> GetAsync(GetRequest request)
        => _options.Get.Run(request.Resource.Singular, request, Backend.GetAsync);

    public override Task<CreateResponse> CreateAsync(CreateRequest request)
        => _options.Create.Run(request.Resource.Singular, request, Backend.CreateAsync);

    public override Task<UpdateResponse> UpdateAsync(UpdateRequest request)
        => _options.Update.Run(request.Resource.Singular, request, Backend.UpdateAsync);

    public override Task<ApplyResponse> ApplyAsync(ApplyRequest request)
        => _options.Apply.Run(request.Resource.Singular, request, Backend.ApplyAsync);

    public override Task<DeleteResponse> DeleteAsync(DeleteRequest request)
        => _options.Delete.Run(request.Resource.Singular, request, Backend.DeleteAsync);
}
