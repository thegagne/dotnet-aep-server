namespace Aep.Server.Backend;

/// <summary>
/// Per-resource chain of interceptors for one method. Each interceptor receives the request
/// and a <c>next</c> delegate (the rest of the chain, ending at the built-in operation), so it
/// may run code before/after <c>next</c>, skip it, or replace the response. Interceptors run
/// in registration order (first registered is outermost).
/// </summary>
public sealed class InterceptorChain<TRequest, TResponse>
{
    private readonly Dictionary<string, List<Func<TRequest, Func<TRequest, Task<TResponse>>, Task<TResponse>>>> _byResource
        = new(StringComparer.Ordinal);

    internal void Add(string singular, Func<TRequest, Func<TRequest, Task<TResponse>>, Task<TResponse>> interceptor)
    {
        if (!_byResource.TryGetValue(singular, out var list))
            _byResource[singular] = list = [];
        list.Add(interceptor);
    }

    /// <summary>Runs the chain for <paramref name="singular"/>, ending at <paramref name="terminal"/>
    /// (the built-in operation). If no interceptor is registered, <paramref name="terminal"/> runs directly.</summary>
    internal Task<TResponse> Run(string singular, TRequest request, Func<TRequest, Task<TResponse>> terminal)
    {
        if (!_byResource.TryGetValue(singular, out var list) || list.Count == 0)
            return terminal(request);

        var next = terminal;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var current = list[i];
            var inner = next;
            next = req => current(req, inner);
        }
        return next(request);
    }
}

/// <summary>
/// Holds per-(resource, method) interceptors. Populated by the <c>OnCreate</c>/<c>OnGet</c>/…
/// extension methods and consumed by <see cref="InterceptingResourceBackend"/>.
/// </summary>
public sealed class ResourceInterceptorOptions
{
    internal InterceptorChain<ListRequest, ListResponse> List { get; } = new();
    internal InterceptorChain<GetRequest, GetResponse> Get { get; } = new();
    internal InterceptorChain<CreateRequest, CreateResponse> Create { get; } = new();
    internal InterceptorChain<UpdateRequest, UpdateResponse> Update { get; } = new();
    internal InterceptorChain<ApplyRequest, ApplyResponse> Apply { get; } = new();
    internal InterceptorChain<DeleteRequest, DeleteResponse> Delete { get; } = new();
}
