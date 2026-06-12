using Aep.Storage.Abstractions.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aep.Storage.Postgres;

/// <summary>The PostgreSQL storage provider. Selected via <c>Storage:Provider=postgres</c>.</summary>
public sealed class PostgresStorageProvider : IStorageProvider
{
    public string Name => "postgres";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PostgresStorageOptions>()
            .Bind(configuration.GetSection("Storage:Postgres"))
            .Configure(o =>
            {
                var shared = configuration["Storage:ConnectionString"];
                if (string.IsNullOrWhiteSpace(o.ConnectionString) && !string.IsNullOrWhiteSpace(shared))
                    o.ConnectionString = shared;
            })
            .Validate(o => o.MaxPoolSize is null or >= 0, "Storage:Postgres:MaxPoolSize must be >= 0.")
            .Validate(o => o.MinPoolSize is null or >= 0, "Storage:Postgres:MinPoolSize must be >= 0.")
            .Validate(
                o => o.MinPoolSize is not { } min || o.MaxPoolSize is not { } max || min <= max,
                "Storage:Postgres:MinPoolSize must be <= MaxPoolSize.")
            .Validate(o => o.CommandTimeoutSeconds is null or >= 0, "Storage:Postgres:CommandTimeoutSeconds must be >= 0.")
            .Validate(o => o.IsSslModeValid, "Storage:Postgres:SslMode is not a valid SSL mode.")
            .ValidateOnStart();

        services.TryAddSingleton<IResourceStore, PostgresResourceStore>();
    }
}
