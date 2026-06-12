using Aep.Server.Backend;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers per-(resource, method) interceptors. Each handler receives the request and a
/// <c>next</c> delegate (the built-in operation, including hooks). Call <c>next</c> to run it
/// — before/after your own logic — or skip it to fully replace the operation. Resolve
/// dependencies via <c>request.Services</c>.
/// </summary>
public static class ResourceInterceptorServiceCollectionExtensions
{
    /// <summary>Intercept Create for one resource (by singular name).</summary>
    public static IServiceCollection OnCreate(this IServiceCollection services, string singular,
        Func<CreateRequest, Func<CreateRequest, Task<CreateResponse>>, Task<CreateResponse>> handler)
        => services.Configure<ResourceInterceptorOptions>(o => o.Create.Add(singular, handler));

    /// <summary>Intercept Get for one resource.</summary>
    public static IServiceCollection OnGet(this IServiceCollection services, string singular,
        Func<GetRequest, Func<GetRequest, Task<GetResponse>>, Task<GetResponse>> handler)
        => services.Configure<ResourceInterceptorOptions>(o => o.Get.Add(singular, handler));

    /// <summary>Intercept List for one resource.</summary>
    public static IServiceCollection OnList(this IServiceCollection services, string singular,
        Func<ListRequest, Func<ListRequest, Task<ListResponse>>, Task<ListResponse>> handler)
        => services.Configure<ResourceInterceptorOptions>(o => o.List.Add(singular, handler));

    /// <summary>Intercept Update (PATCH) for one resource.</summary>
    public static IServiceCollection OnUpdate(this IServiceCollection services, string singular,
        Func<UpdateRequest, Func<UpdateRequest, Task<UpdateResponse>>, Task<UpdateResponse>> handler)
        => services.Configure<ResourceInterceptorOptions>(o => o.Update.Add(singular, handler));

    /// <summary>Intercept Apply (PUT) for one resource.</summary>
    public static IServiceCollection OnApply(this IServiceCollection services, string singular,
        Func<ApplyRequest, Func<ApplyRequest, Task<ApplyResponse>>, Task<ApplyResponse>> handler)
        => services.Configure<ResourceInterceptorOptions>(o => o.Apply.Add(singular, handler));

    /// <summary>Intercept Delete for one resource.</summary>
    public static IServiceCollection OnDelete(this IServiceCollection services, string singular,
        Func<DeleteRequest, Func<DeleteRequest, Task<DeleteResponse>>, Task<DeleteResponse>> handler)
        => services.Configure<ResourceInterceptorOptions>(o => o.Delete.Add(singular, handler));
}
