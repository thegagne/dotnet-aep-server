using Aep.Server.Backend;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Wraps the resource backend with a decorator, like the Go ResourceBackend pattern.</summary>
public static class ResourceBackendServiceCollectionExtensions
{
    /// <summary>
    /// Decorates the registered <see cref="IResourceBackend"/>. The factory receives the
    /// inner backend (the current registration) and the request services, and returns a
    /// wrapper. Call after <c>AddAepServer(...)</c>; multiple calls stack (last registered
    /// is outermost).
    /// </summary>
    public static IServiceCollection DecorateResourceBackend(
        this IServiceCollection services, Func<IResourceBackend, IServiceProvider, IResourceBackend> decorator)
    {
        var inner = services.LastOrDefault(d => d.ServiceType == typeof(IResourceBackend))
            ?? throw new InvalidOperationException(
                "No IResourceBackend is registered. Call AddAepServer(...) before DecorateResourceBackend(...).");
        services.Remove(inner);

        services.Add(new ServiceDescriptor(typeof(IResourceBackend), sp =>
        {
            var instance = (IResourceBackend)(
                inner.ImplementationInstance
                ?? inner.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.CreateInstance(sp, inner.ImplementationType!));
            return decorator(instance, sp);
        }, inner.Lifetime));

        return services;
    }

    /// <summary>
    /// Decorates the backend with <typeparamref name="TDecorator"/>, whose constructor takes
    /// the inner <see cref="IResourceBackend"/> (plus any DI dependencies):
    /// <code>sealed class MyBackend(IResourceBackend inner) : IResourceBackend { ... }</code>
    /// </summary>
    public static IServiceCollection DecorateResourceBackend<TDecorator>(this IServiceCollection services)
        where TDecorator : class, IResourceBackend
        => services.DecorateResourceBackend((inner, sp) => ActivatorUtilities.CreateInstance<TDecorator>(sp, inner));
}
