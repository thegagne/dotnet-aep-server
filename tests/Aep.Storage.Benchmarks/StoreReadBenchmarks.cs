using Aep.Storage.Abstractions.Filtering;
using Aep.Storage.Abstractions.Model;
using Aep.Storage.Abstractions.Storage;
using Aep.Storage.InMemory;
using Aep.Storage.Sqlite;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Options;

namespace Aep.Storage.Benchmarks;

/// <summary>
/// Store read performance: point Get, first-page List, and filtered List — across the
/// Docker-free backends, dataset sizes, and with/without a declared index on the filter field
/// (so the index payoff is visible on SQLite). Seeded once per parameter combination.
/// </summary>
[MemoryDiagnoser]
public class StoreReadBenchmarks
{
    [Params("inmemory", "sqlite")]
    public string Backend = "inmemory";

    [Params(1_000, 10_000)]
    public int Rows;

    [Params(false, true)]
    public bool Indexed;

    private IResourceStore _store = null!;
    private ResourceDefinition _book = null!;
    private static readonly IReadOnlyDictionary<string, string> P1 =
        new Dictionary<string, string> { ["publisher_id"] = "p1" };
    private readonly FilterExpression _authorFilter = FilterParser.Parse("author == \"author7\"");

    [GlobalSetup]
    public async Task Setup()
    {
        _store = Backend == "sqlite"
            ? new SqliteResourceStore(Options.Create(new SqliteStorageOptions { ConnectionString = "Data Source=:memory:" }))
            : new InMemoryResourceStore();

        _book = new ResourceDefinition
        {
            Singular = "book",
            Plural = "books",
            Parents = ["publisher"],
            Schema = new ResourceSchema
            {
                Properties = new Dictionary<string, SchemaProperty>
                {
                    ["author"] = new() { Type = "string" },
                    ["price"] = new() { Type = "integer" },
                },
            },
            Indexes = Indexed ? [new ResourceIndex { Fields = ["author"] }] : [],
        };

        await _store.EnsureSchemaAsync([_book]);
        for (var i = 0; i < Rows; i++)
        {
            await _store.InsertAsync(_book, new StoredResource
            {
                Id = $"b{i}",
                Path = $"publishers/p1/books/b{i}",
                CreateTime = "2024-01-01T00:00:00Z",
                UpdateTime = "2024-01-01T00:00:00Z",
                Fields = new Dictionary<string, object?> { ["author"] = $"author{i % 50}", ["price"] = (long)i },
            }, P1);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_store is IAsyncDisposable a) a.DisposeAsync().AsTask().GetAwaiter().GetResult();
        else if (_store is IDisposable d) d.Dispose();
    }

    [Benchmark]
    public Task<StoredResource?> Get() => _store.GetAsync(_book, "publishers/p1/books/b500");

    [Benchmark]
    public Task<ListResult> ListFirstPage() =>
        _store.ListAsync(_book, P1, new ListOptions { PageSize = 50 });

    [Benchmark]
    public Task<ListResult> ListFilteredByAuthor() =>
        _store.ListAsync(_book, P1, new ListOptions { PageSize = 50, Filter = _authorFilter });
}
