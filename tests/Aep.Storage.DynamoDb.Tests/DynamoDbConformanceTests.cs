using Aep.Storage.Abstractions.Storage;
using Aep.Storage.TestKit;
using Microsoft.Extensions.Options;

namespace Aep.Storage.DynamoDb.Tests;

/// <summary>Runs the shared cross-backend conformance suite (#06) against a real DynamoDB emulator.</summary>
public sealed class DynamoDbConformanceTests(FlociFixture fixture)
    : ResourceStoreConformanceTests, IClassFixture<FlociFixture>, IAsyncLifetime
{
    private DynamoDbResourceStore _store = null!;
    protected override IResourceStore Store => _store;

    public async Task InitializeAsync()
    {
        // Unique table prefix per test => isolated tables in the shared emulator.
        _store = new DynamoDbResourceStore(Options.Create(new DynamoDbStorageOptions
        {
            ServiceUrl = fixture.ServiceUrl,
            TablePrefix = $"t{Guid.NewGuid():N}_",
        }));
        await _store.EnsureSchemaAsync([Book]);
    }

    public Task DisposeAsync()
    {
        _store.Dispose();
        return Task.CompletedTask;
    }
}
