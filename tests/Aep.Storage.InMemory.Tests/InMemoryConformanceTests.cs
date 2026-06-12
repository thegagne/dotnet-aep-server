using Aep.Storage.Abstractions.Storage;
using Aep.Storage.TestKit;

namespace Aep.Storage.InMemory.Tests;

/// <summary>Runs the shared cross-backend conformance suite (#06) against the in-memory store.</summary>
public sealed class InMemoryConformanceTests : ResourceStoreConformanceTests, IAsyncLifetime
{
    private readonly InMemoryResourceStore _store = new();
    protected override IResourceStore Store => _store;

    public async Task InitializeAsync() => await _store.EnsureSchemaAsync([Book]);
    public Task DisposeAsync() => Task.CompletedTask;
}
