using Aep.Storage.Abstractions.Filtering;
using Aep.Storage.Abstractions.Model;
using Aep.Storage.Abstractions.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Aep.Storage.Postgres.Tests;

/// <summary>Starts a throwaway PostgreSQL container once for the test class.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        // Guard against the initdb restart race: wait until Postgres truly accepts connections.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var conn = new NpgsqlConnection(ConnectionString);
                await conn.OpenAsync();
                return;
            }
            catch (NpgsqlException) when (attempt < 50)
            {
                await Task.Delay(200);
            }
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public sealed class PostgresResourceStoreTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private PostgresResourceStore _store = null!;

    public async Task InitializeAsync()
    {
        // Fresh slate per test.
        await using (var conn = new NpgsqlConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE IF EXISTS books CASCADE";
            await cmd.ExecuteNonQueryAsync();
        }
        _store = new PostgresResourceStore(Options.Create(new PostgresStorageOptions { ConnectionString = fixture.ConnectionString }));
        await _store.EnsureSchemaAsync([Book]);
    }

    public async Task DisposeAsync() => await _store.DisposeAsync();

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

    [Fact]
    public async Task Declared_index_is_created()
    {
        var indexed = new ResourceDefinition
        {
            Singular = "book", Plural = "books", Parents = ["publisher"], Schema = Book.Schema,
            Indexes = [new ResourceIndex { Fields = ["author"] }],
        };
        await _store.EnsureSchemaAsync([indexed]);

        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT indexdef FROM pg_indexes WHERE tablename = 'books'";
        var defs = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            defs.Add(reader.GetString(0));

        Assert.Contains(defs, d => d.Contains("idx_books_author") && d.Contains("author"));
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
