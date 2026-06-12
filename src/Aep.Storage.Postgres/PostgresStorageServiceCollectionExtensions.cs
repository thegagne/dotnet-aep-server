using Aep.Storage.Postgres;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI helper for the PostgreSQL storage backend.</summary>
public static class PostgresStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL <see cref="Aep.Storage.Abstractions.Storage.IResourceStore"/>,
    /// binding options from the <c>Storage:Postgres</c> configuration section.
    /// </summary>
    public static IServiceCollection AddAepPostgresStore(this IServiceCollection services, IConfiguration configuration)
    {
        new PostgresStorageProvider().Register(services, configuration);
        return services;
    }
}
