using Aep.Storage.Abstractions.Filtering;
using Aep.Storage.Abstractions.Model;
using Aep.Storage.Abstractions.Storage;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;

namespace Aep.Storage.DynamoDb.Tests;

/// <summary>Runs floci (a local AWS emulator) once for the test class; DynamoDB is at :4566.</summary>
public sealed class FlociFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("hectorvent/floci:latest")
        .WithPortBinding(4566, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(4566).ForPath("/")))
        .Build();

    public string ServiceUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(4566)}";

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public sealed class DynamoDbResourceStoreTests(FlociFixture fixture)
    : IClassFixture<FlociFixture>, IAsyncLifetime
{
    private DynamoDbResourceStore _store = null!;

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

    private static readonly ResourceDefinition Book = new()
    {
        Singular = "book",
        Plural = "books",
        Parents = ["publisher"],
        Schema = new ResourceSchema
        {
            Properties = new Dictionary<string, SchemaProperty>
            {
                ["title"] = new() { Type = "string" },
                ["author"] = new() { Type = "string" },
                ["price"] = new() { Type = "integer" },
                ["published"] = new() { Type = "boolean" },
                ["tags"] = new() { Type = "array", Items = new SchemaProperty { Type = "string" } },
            },
        },
    };

    private static readonly IReadOnlyDictionary<string, string> P1 =
        new Dictionary<string, string> { ["publisher_id"] = "p1" };

    private static StoredResource NewBook(string id, string author, long price, bool published = false) => new()
    {
        Id = id,
        Path = $"publishers/p1/books/{id}",
        CreateTime = "2024-01-01T00:00:00Z",
        UpdateTime = "2024-01-01T00:00:00Z",
        Fields = new Dictionary<string, object?> { ["title"] = $"Book {id}", ["author"] = author, ["price"] = price, ["published"] = published },
    };

    [Fact]
    public async Task Insert_then_get_round_trips_typed_fields()
    {
        await _store.InsertAsync(Book, NewBook("b1", "Orwell", 10, published: true), P1);
        var got = await _store.GetAsync(Book, "publishers/p1/books/b1");
        Assert.NotNull(got);
        Assert.Equal("Orwell", got!.Fields["author"]);
        Assert.Equal(10L, got.Fields["price"]);
        Assert.Equal(true, got.Fields["published"]);
    }

    [Fact]
    public async Task Get_missing_returns_null() =>
        Assert.Null(await _store.GetAsync(Book, "publishers/p1/books/none"));

    [Fact]
    public async Task Insert_duplicate_throws()
    {
        await _store.InsertAsync(Book, NewBook("b1", "A", 1), P1);
        await Assert.ThrowsAsync<DuplicateResourceException>(() =>
            _store.InsertAsync(Book, NewBook("b1", "B", 2), P1));
    }

    [Fact]
    public async Task Update_merges_only_supplied_fields()
    {
        await _store.InsertAsync(Book, NewBook("b1", "Orwell", 10), P1);
        Assert.True(await _store.UpdateAsync(Book, "publishers/p1/books/b1",
            new Dictionary<string, object?> { ["author"] = "George Orwell" }, "2024-06-01T00:00:00Z"));

        var got = await _store.GetAsync(Book, "publishers/p1/books/b1");
        Assert.Equal("George Orwell", got!.Fields["author"]);
        Assert.Equal("Book b1", got.Fields["title"]);
        Assert.Equal("2024-06-01T00:00:00Z", got.UpdateTime);
    }

    [Fact]
    public async Task Update_missing_returns_false() =>
        Assert.False(await _store.UpdateAsync(Book, "publishers/p1/books/ghost",
            new Dictionary<string, object?> { ["author"] = "x" }, "2024-06-01T00:00:00Z"));

    [Fact]
    public async Task Delete_removes_and_reports()
    {
        await _store.InsertAsync(Book, NewBook("b1", "A", 1), P1);
        Assert.True(await _store.DeleteAsync(Book, "publishers/p1/books/b1"));
        Assert.False(await _store.DeleteAsync(Book, "publishers/p1/books/b1"));
    }

    [Fact]
    public async Task List_paginates_with_token()
    {
        for (var i = 0; i < 5; i++)
            await _store.InsertAsync(Book, NewBook($"b{i}", "A", i), P1);

        var p1 = await _store.ListAsync(Book, P1, new ListOptions { PageSize = 2 });
        Assert.Equal(2, p1.Items.Count);
        var p2 = await _store.ListAsync(Book, P1, new ListOptions { PageSize = 2, PageToken = p1.NextPageToken });
        var p3 = await _store.ListAsync(Book, P1, new ListOptions { PageSize = 2, PageToken = p2.NextPageToken });
        Assert.Single(p3.Items);
        Assert.True(string.IsNullOrEmpty(p3.NextPageToken));
        Assert.Equal(5, p1.Items.Concat(p2.Items).Concat(p3.Items).Select(b => b.Id).Distinct().Count());
    }

    [Fact]
    public async Task List_applies_cel_filter()
    {
        await _store.InsertAsync(Book, NewBook("b1", "Orwell", 10), P1);
        await _store.InsertAsync(Book, NewBook("b2", "Huxley", 20), P1);
        await _store.InsertAsync(Book, NewBook("b3", "Orwell", 30), P1);

        var result = await _store.ListAsync(Book, P1, new ListOptions
        {
            Filter = FilterParser.Parse("author == \"Orwell\" && price > 15"),
        });
        Assert.Single(result.Items);
        Assert.Equal("b3", result.Items[0].Id);
    }

    private static readonly ResourceDefinition IndexedBook = new()
    {
        Singular = "book", Plural = "books", Parents = ["publisher"], Schema = Book.Schema,
        Indexes = [new ResourceIndex { Fields = ["author"] }],
    };

    private DynamoDbResourceStore NewIndexedStore() => new(Options.Create(new DynamoDbStorageOptions
    {
        ServiceUrl = fixture.ServiceUrl,
        TablePrefix = $"t{Guid.NewGuid():N}_",
    }));

    [Fact]
    public async Task Indexed_equality_is_parent_scoped_and_filters_residual()
    {
        var store = NewIndexedStore();
        await store.EnsureSchemaAsync([IndexedBook]);

        await store.InsertAsync(IndexedBook, NewBook("b1", "Orwell", 10), P1);
        await store.InsertAsync(IndexedBook, NewBook("b2", "Huxley", 20), P1);
        await store.InsertAsync(IndexedBook, NewBook("b3", "Orwell", 30), P1);
        // Same author under a different publisher must not leak in (parent scope is in the GSI key).
        await store.InsertAsync(IndexedBook, new StoredResource
        {
            Id = "bx", Path = "publishers/p2/books/bx",
            CreateTime = "2024-01-01T00:00:00Z", UpdateTime = "2024-01-01T00:00:00Z",
            Fields = new Dictionary<string, object?> { ["author"] = "Orwell", ["price"] = 99L },
        }, new Dictionary<string, string> { ["publisher_id"] = "p2" });

        // Equality on the indexed field -> GSI; residual (price > 15) -> FilterExpression.
        var hit = await store.ListAsync(IndexedBook, P1, new ListOptions
        {
            Filter = FilterParser.Parse("author == \"Orwell\" && price > 15"),
        });
        Assert.Equal(["b3"], hit.Items.Select(i => i.Id));

        var both = await store.ListAsync(IndexedBook, P1, new ListOptions { Filter = FilterParser.Parse("author == \"Orwell\"") });
        Assert.Equal(["b1", "b3"], both.Items.Select(i => i.Id).OrderBy(x => x));

        store.Dispose();
    }

    [Fact]
    public async Task Updating_indexed_field_moves_it_in_the_index()
    {
        var store = NewIndexedStore();
        await store.EnsureSchemaAsync([IndexedBook]);
        await store.InsertAsync(IndexedBook, NewBook("b1", "Orwell", 10), P1);

        await store.UpdateAsync(IndexedBook, "publishers/p1/books/b1",
            new Dictionary<string, object?> { ["author"] = "Huxley" }, "2024-06-01T00:00:00Z");

        var orwell = await store.ListAsync(IndexedBook, P1, new ListOptions { Filter = FilterParser.Parse("author == \"Orwell\"") });
        Assert.Empty(orwell.Items);
        var huxley = await store.ListAsync(IndexedBook, P1, new ListOptions { Filter = FilterParser.Parse("author == \"Huxley\"") });
        Assert.Equal(["b1"], huxley.Items.Select(i => i.Id));

        store.Dispose();
    }

    [Fact]
    public async Task List_scopes_to_parent()
    {
        await _store.InsertAsync(Book, NewBook("b1", "A", 1), P1);
        await _store.InsertAsync(Book, new StoredResource
        {
            Id = "b2", Path = "publishers/p2/books/b2",
            CreateTime = "2024-01-01T00:00:00Z", UpdateTime = "2024-01-01T00:00:00Z",
            Fields = new Dictionary<string, object?> { ["author"] = "A" },
        }, new Dictionary<string, string> { ["publisher_id"] = "p2" });

        var p1 = await _store.ListAsync(Book, P1, new ListOptions());
        Assert.Single(p1.Items);
        Assert.Equal("b1", p1.Items[0].Id);
    }

    [Fact]
    public async Task List_across_collections_with_wildcard_parent()
    {
        // Exercises the Scan fallback (a "-" parent can't form the GSI partition key).
        await _store.InsertAsync(Book, NewBook("b1", "A", 1), P1);
        await _store.InsertAsync(Book, new StoredResource
        {
            Id = "b2", Path = "publishers/p2/books/b2",
            CreateTime = "2024-01-01T00:00:00Z", UpdateTime = "2024-01-01T00:00:00Z",
            Fields = new Dictionary<string, object?> { ["author"] = "A" },
        }, new Dictionary<string, string> { ["publisher_id"] = "p2" });

        var wildcard = new Dictionary<string, string> { ["publisher_id"] = ResourceDefinition.WildcardCollectionId };
        var all = await _store.ListAsync(Book, wildcard, new ListOptions());
        Assert.Equal(new[] { "b1", "b2" }, all.Items.Select(i => i.Id).OrderBy(x => x).ToArray());
    }
}
