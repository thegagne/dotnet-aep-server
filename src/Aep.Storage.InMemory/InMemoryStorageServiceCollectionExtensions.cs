using Aep.Storage.Abstractions.Storage;
using Aep.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI helper for the in-memory storage backend.</summary>
public static class InMemoryStorageServiceCollectionExtensions
{
    /// <summary>Registers the zero-dependency in-memory <see cref="IResourceStore"/>.</summary>
    public static IServiceCollection AddAepInMemoryStore(this IServiceCollection services)
    {
        services.TryAddSingleton<IResourceStore, InMemoryResourceStore>();
        return services;
    }
}
