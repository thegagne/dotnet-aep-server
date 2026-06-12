using Aep.Storage.Abstractions.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aep.Storage.Sqlite;

/// <summary>The default storage provider: SQLite. Selected via <c>Storage:Provider=sqlite</c>.</summary>
public sealed class SqliteStorageProvider : IStorageProvider
{
    public string Name => "sqlite";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SqliteStorageOptions>()
            .Bind(configuration.GetSection("Storage:Sqlite"))
            .Configure(o =>
            {
                // Allow a single shared connection string at the Storage root as a convenience.
                var shared = configuration["Storage:ConnectionString"];
                if (string.IsNullOrWhiteSpace(o.ConnectionString) && !string.IsNullOrWhiteSpace(shared))
                    o.ConnectionString = shared;
            })
            .Validate(o => o.BusyTimeoutMs >= 0, "Storage:Sqlite:BusyTimeoutMs must be >= 0.")
            .Validate(
                o => SqliteStorageOptions.JournalModes.Contains(o.JournalMode.ToUpperInvariant()),
                $"Storage:Sqlite:JournalMode must be one of: {string.Join(", ", SqliteStorageOptions.JournalModes)}.")
            .ValidateOnStart();

        services.TryAddSingleton<IResourceStore, SqliteResourceStore>();
    }
}
