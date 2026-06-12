using System.Text.Json;
using Aep.Storage.Abstractions.Filtering;
using Aep.Storage.Abstractions.Model;
using Aep.Storage.Abstractions.Storage;
using Microsoft.Extensions.Options;

namespace Aep.Storage.Sqlite.Tests;

public sealed class SqliteResourceStoreTests : IAsyncLifetime
{
    private SqliteResourceStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new SqliteResourceStore(Options.Create(new SqliteStorageOptions
        {
            ConnectionString = "Data Source=:memory:",
        }));
        await _store.EnsureSchemaAsync([TestResources.Book]);
    }

    public async Task DisposeAsync() => await _store.DisposeAsync();

    [Fact]
    public async Task Insert_then_get_round_trips_fields()
    {
        var book = TestResources.NewBook("b1", "Orwell", 10, published: true);
        book.Fields["tags"] = JsonDocument.Parse("[\"dystopia\",\"classic\"]").RootElement;

        await _store.InsertAsync(TestResources.Book, book, TestResources.P1);
        var got = await _store.GetAsync(TestResources.Book, book.Path);

        Assert.NotNull(got);
        Assert.Equal("Orwell", got!.Fields["author"]);
        Assert.Equal(10L, got.Fields["price"]);
        Assert.Equal(true, got.Fields["published"]);
        var tags = Assert.IsType<JsonElement>(got.Fields["tags"]);
        Assert.Equal(JsonValueKind.Array, tags.ValueKind);
        Assert.Equal(2, tags.GetArrayLength());
    }

    [Fact]
    public async Task Get_missing_returns_null()
    {
        Assert.Null(await _store.GetAsync(TestResources.Book, "publishers/p1/books/nope"));
    }

    [Fact]
    public async Task Insert_duplicate_path_throws()
    {
        var book = TestResources.NewBook("b1", "Orwell", 10);
        await _store.InsertAsync(TestResources.Book, book, TestResources.P1);
        await Assert.ThrowsAsync<DuplicateResourceException>(() =>
            _store.InsertAsync(TestResources.Book, TestResources.NewBook("b1", "Other", 5), TestResources.P1));
    }

    [Fact]
    public async Task Update_merges_only_supplied_fields()
    {
        var book = TestResources.NewBook("b1", "Orwell", 10, title: "1984");
        await _store.InsertAsync(TestResources.Book, book, TestResources.P1);

        var updated = await _store.UpdateAsync(
            TestResources.Book, book.Path,
            new Dictionary<string, object?> { ["author"] = "George Orwell" },
            "2024-06-01T00:00:00Z");

        Assert.True(updated);
        var got = await _store.GetAsync(TestResources.Book, book.Path);
        Assert.Equal("George Orwell", got!.Fields["author"]);
        Assert.Equal("1984", got.Fields["title"]); // untouched
        Assert.Equal("2024-06-01T00:00:00Z", got.UpdateTime);
    }

    [Fact]
    public async Task Update_missing_returns_false()
    {
        var ok = await _store.UpdateAsync(
            TestResources.Book, "publishers/p1/books/ghost",
            new Dictionary<string, object?> { ["author"] = "x" }, "2024-06-01T00:00:00Z");
        Assert.False(ok);
    }

    [Fact]
    public async Task Delete_removes_and_reports()
    {
        var book = TestResources.NewBook("b1", "Orwell", 10);
        await _store.InsertAsync(TestResources.Book, book, TestResources.P1);

        Assert.True(await _store.DeleteAsync(TestResources.Book, book.Path));
        Assert.False(await _store.DeleteAsync(TestResources.Book, book.Path));
        Assert.Null(await _store.GetAsync(TestResources.Book, book.Path));
    }

    [Fact]
    public async Task List_paginates_with_page_token()
    {
        for (var i = 0; i < 5; i++)
            await _store.InsertAsync(TestResources.Book, TestResources.NewBook($"b{i}", "A", i), TestResources.P1);

        var page1 = await _store.ListAsync(TestResources.Book, TestResources.P1, new ListOptions { PageSize = 2 });
        Assert.Equal(2, page1.Items.Count);
        Assert.False(string.IsNullOrEmpty(page1.NextPageToken));

        var page2 = await _store.ListAsync(TestResources.Book, TestResources.P1,
            new ListOptions { PageSize = 2, PageToken = page1.NextPageToken });
        Assert.Equal(2, page2.Items.Count);

        var page3 = await _store.ListAsync(TestResources.Book, TestResources.P1,
            new ListOptions { PageSize = 2, PageToken = page2.NextPageToken });
        Assert.Single(page3.Items);
        Assert.True(string.IsNullOrEmpty(page3.NextPageToken));

        // No overlap across pages.
        var ids = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(b => b.Id).ToList();
        Assert.Equal(5, ids.Distinct().Count());
    }

    [Fact]
    public async Task List_applies_filter()
    {
        await _store.InsertAsync(TestResources.Book, TestResources.NewBook("b1", "Orwell", 10), TestResources.P1);
        await _store.InsertAsync(TestResources.Book, TestResources.NewBook("b2", "Huxley", 20), TestResources.P1);
        await _store.InsertAsync(TestResources.Book, TestResources.NewBook("b3", "Orwell", 30), TestResources.P1);

        var result = await _store.ListAsync(TestResources.Book, TestResources.P1, new ListOptions
        {
            Filter = FilterParser.Parse("author == \"Orwell\" && price > 15"),
        });

        Assert.Single(result.Items);
        Assert.Equal("b3", result.Items[0].Id);
    }

    [Fact]
    public async Task List_scopes_to_parent()
    {
        await _store.InsertAsync(TestResources.Book, TestResources.NewBook("b1", "A", 1), TestResources.P1);
        var p2Book = TestResources.NewBook("b2", "A", 1);
        var p2BookScoped = new StoredResource
        {
            Id = p2Book.Id, Path = "publishers/p2/books/b2",
            CreateTime = p2Book.CreateTime, UpdateTime = p2Book.UpdateTime, Fields = p2Book.Fields,
        };
        await _store.InsertAsync(TestResources.Book, p2BookScoped,
            new Dictionary<string, string> { ["publisher_id"] = "p2" });

        var p1 = await _store.ListAsync(TestResources.Book, TestResources.P1, new ListOptions());
        Assert.Single(p1.Items);
        Assert.Equal("b1", p1.Items[0].Id);
    }
}
