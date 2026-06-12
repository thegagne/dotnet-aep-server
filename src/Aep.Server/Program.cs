var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAepServer(builder.Configuration);

// Select a storage backend by config. Providers are referenced at build time, so
// SQLite is only an option when compiled in (-p:IncludeSqlite, default on).
#if STORAGE_SQLITE
const string defaultProvider = "sqlite";
#else
const string defaultProvider = "inmemory";
#endif
var providerName = builder.Configuration["Storage:Provider"] ?? defaultProvider;
switch (providerName.ToLowerInvariant())
{
    case "inmemory":
        builder.Services.AddAepInMemoryStore();
        break;
#if STORAGE_SQLITE
    case "sqlite":
        builder.Services.AddAepSqliteStore(builder.Configuration);
        break;
#endif
#if STORAGE_POSTGRES
    case "postgres":
        builder.Services.AddAepPostgresStore(builder.Configuration);
        break;
#endif
#if STORAGE_DYNAMODB
    case "dynamodb":
        builder.Services.AddAepDynamoDbStore(builder.Configuration);
        break;
#endif
    default:
        throw new InvalidOperationException(
            $"unknown or unavailable storage provider '{providerName}' in this build.");
}

var app = builder.Build();

await app.MapAepServerAsync();

app.Run();

// Exposed so WebApplicationFactory-based integration tests can reference the entry point.
public partial class Program;
