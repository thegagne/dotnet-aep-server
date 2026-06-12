using Aep.Storage.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI helper for the SQLite storage backend.</summary>
public static class SqliteStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite <see cref="Aep.Storage.Abstractions.Storage.IResourceStore"/>,
    /// binding options from the <c>Storage:Sqlite</c> configuration section.
    /// </summary>
    public static IServiceCollection AddAepSqliteStore(this IServiceCollection services, IConfiguration configuration)
    {
        new SqliteStorageProvider().Register(services, configuration);
        return services;
    }
}
