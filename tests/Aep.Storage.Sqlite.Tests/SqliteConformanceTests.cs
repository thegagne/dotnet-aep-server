using Aep.Storage.Abstractions.Storage;
using Aep.Storage.TestKit;
using Microsoft.Extensions.Options;

namespace Aep.Storage.Sqlite.Tests;

/// <summary>Runs the shared cross-backend conformance suite (#06) against the SQLite store.</summary>
public sealed class SqliteConformanceTests : ResourceStoreConformanceTests, IAsyncLifetime
{
    private SqliteResourceStore _store = null!;
    protected override IResourceStore Store => _store;

    public async Task InitializeAsync()
    {
        _store = new SqliteResourceStore(Options.Create(new SqliteStorageOptions { ConnectionString = "Data Source=:memory:" }));
        await _store.EnsureSchemaAsync([Book]);
    }

    public async Task DisposeAsync() => await _store.DisposeAsync();
}
