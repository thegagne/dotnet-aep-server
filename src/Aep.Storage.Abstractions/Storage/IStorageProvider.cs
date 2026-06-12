using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aep.Storage.Abstractions.Storage;

/// <summary>
/// A self-registering storage backend. The host keeps an explicit list of
/// providers and activates exactly the one whose <see cref="Name"/> matches the
/// <c>Storage:Provider</c> configuration value. The selected provider is
/// responsible for registering an <see cref="IResourceStore"/> (and any of its
/// own dependencies, options, etc.) in the DI container.
/// </summary>
public interface IStorageProvider
{
    /// <summary>Provider key, matched case-insensitively against <c>Storage:Provider</c> (e.g. "sqlite").</summary>
    string Name { get; }

    /// <summary>Registers this provider's services into the DI container.</summary>
    void Register(IServiceCollection services, IConfiguration configuration);
}
