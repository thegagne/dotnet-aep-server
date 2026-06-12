using Aep.Storage.Abstractions.Storage;
using Aep.Storage.TestKit;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aep.Storage.Postgres.Tests;

/// <summary>Runs the shared cross-backend conformance suite (#06) against a real PostgreSQL container.</summary>
public sealed class PostgresConformanceTests(PostgresFixture fixture)
    : ResourceStoreConformanceTests, IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private PostgresResourceStore _store = null!;
    protected override IResourceStore Store => _store;

    public async Task InitializeAsync()
    {
        await using (var conn = new NpgsqlConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE IF EXISTS books CASCADE"; // fresh slate per test
            await cmd.ExecuteNonQueryAsync();
        }
        _store = new PostgresResourceStore(Options.Create(new PostgresStorageOptions { ConnectionString = fixture.ConnectionString }));
        await _store.EnsureSchemaAsync([Book]);
    }

    public async Task DisposeAsync() => await _store.DisposeAsync();
}
