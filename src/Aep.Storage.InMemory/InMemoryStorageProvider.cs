using Aep.Storage.Abstractions.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aep.Storage.InMemory;

/// <summary>
/// A zero-dependency, process-memory storage provider. Selected via
/// <c>Storage:Provider=inmemory</c>. Carries no native libraries, so it lets the
/// host ship without the SQLite binding when persistence isn't needed.
/// </summary>
public sealed class InMemoryStorageProvider : IStorageProvider
{
    public string Name => "inmemory";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IResourceStore, InMemoryResourceStore>();
    }
}
