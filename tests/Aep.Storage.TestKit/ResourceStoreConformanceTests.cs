using Aep.Storage.Abstractions.Filtering;
using Aep.Storage.Abstractions.Model;
using Aep.Storage.Abstractions.Storage;

namespace Aep.Storage.TestKit;

/// <summary>
/// The behavioral contract every <see cref="IResourceStore"/> must satisfy, run identically
/// against each backend (#06). A backend test project provides a concrete subclass that exposes
/// a ready store (schema ensured for <see cref="Book"/>); xUnit then runs all of these against it.
/// Backend-specific behavior (index DDL, GSI planning, options) stays in the backend's own tests.
/// </summary>
public abstract class ResourceStoreConformanceTests
{
    /// <summary>An isolated, schema-ready store for the resource(s) below. Fresh per test.</summary>
    protected abstract IResourceStore Store { get; }

    protected static readonly ResourceDefinition Book = new()
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

    private static IReadOnlyDictionary<string, string> Parent(string id) =>
        new Dictionary<string, string> { ["publisher_id"] = id };

    private static readonly IReadOnlyDictionary<string, string> P1 = Parent("p1");

    private static StoredResource NewBook(
        string id, string author, long price, bool published = false, string parent = "p1", string? uid = null) => new()
    {
        Id = id,
        Uid = uid,
        Path = $"publishers/{parent}/books/{id}",
        CreateTime = "2024-01-01T00:00:00Z",
        UpdateTime = "2024-01-01T00:00:00Z",
        Fields = new Dictionary<string, object?>
        {
            ["title"] = $"Book {id}",
            ["author"] = author,
            ["price"] = price,
            ["published"] = published,
        },
    };

    // ---- round-trip / single-item contract ----

    [Fact]
    public async Task Insert_then_get_round_trips_fields()
    {
        await Store.InsertAsync(Book, NewBook("b1", "Orwell", 10, published: true), P1);

        var got = await Store.GetAsync(Book, "publishers/p1/books/b1");
        Assert.NotNull(got);
        Assert.Equal("b1", got!.Id);
        Assert.Equal("Orwell", got.Fields["author"]);
        Assert.Equal(10L, Convert.ToInt64(got.Fields["price"]));
        Assert.Equal(true, got.Fields["published"]);
    }

    [Fact]
    public async Task Get_missing_returns_null() =>
        Assert.Null(await Store.GetAsync(Book, "publishers/p1/books/none"));

    [Fact]
    public async Task Insert_duplicate_path_throws()
    {
        await Store.InsertAsync(Book, NewBook("b1", "A", 1), P1);
        await Assert.ThrowsAsync<DuplicateResourceException>(() =>
            Store.InsertAsync(Book, NewBook("b1", "B", 2), P1));
    }

    [Fact]
    public async Task Uid_round_trips()
    {
        await Store.InsertAsync(Book, NewBook("b1", "A", 1, uid: "uid-123"), P1);
        var got = await Store.GetAsync(Book, "publishers/p1/books/b1");
        Assert.Equal("uid-123", got!.Uid);
    }

    // ---- update / delete contract ----

    [Fact]
    public async Task Update_merges_only_supplied_fields()
    {
        await Store.InsertAsync(Book, NewBook("b1", "Orwell", 10), P1);
        var ok = await Store.UpdateAsync(Book, "publishers/p1/books/b1",
            new Dictionary<string, object?> { ["author"] = "Huxley" }, "2024-02-02T00:00:00Z");
        Assert.True(ok);

        var got = await Store.GetAsync(Book, "publishers/p1/books/b1");
        Assert.Equal("Huxley", got!.Fields["author"]);
        Assert.Equal(10L, Convert.ToInt64(got.Fields["price"])); // untouched
        Assert.Equal("2024-02-02T00:00:00Z", got.UpdateTime);
    }

    [Fact]
    public async Task Update_missing_returns_false() =>
        Assert.False(await Store.UpdateAsync(Book, "publishers/p1/books/none",
            new Dictionary<string, object?> { ["author"] = "X" }, "2024-02-02T00:00:00Z"));

    [Fact]
    public async Task Delete_removes_and_reports()
    {
        await Store.InsertAsync(Book, NewBook("b1", "A", 1), P1);
        Assert.True(await Store.DeleteAsync(Book, "publishers/p1/books/b1"));
        Assert.Null(await Store.GetAsync(Book, "publishers/p1/books/b1"));
        Assert.False(await Store.DeleteAsync(Book, "publishers/p1/books/b1")); // already gone
    }

    // ---- optimistic concurrency (AEP-154 / #12), proven on every backend ----

    [Fact]
    public async Task Update_with_matching_expected_update_time_succeeds()
    {
        await Store.InsertAsync(Book, NewBook("b1", "A", 1), P1); // update_time = 2024-01-01T00:00:00Z
        var ok = await Store.UpdateAsync(Book, "publishers/p1/books/b1",
            new Dictionary<string, object?> { ["author"] = "B" }, "2024-03-03T00:00:00Z",
            expectedUpdateTime: "2024-01-01T00:00:00Z");
        Assert.True(ok);
        Assert.Equal("B", (await Store.GetAsync(Book, "publishers/p1/books/b1"))!.Fields["author"]);
    }

    [Fact]
    public async Task Update_with_stale_expected_update_time_fails()
    {
        await Store.InsertAsync(Book, NewBook("b1", "A", 1), P1);
        var ok = await Store.UpdateAsync(Book, "publishers/p1/books/b1",
            new Dictionary<string, object?> { ["author"] = "B" }, "2024-03-03T00:00:00Z",
            expectedUpdateTime: "1999-01-01T00:00:00Z");
        Assert.False(ok);
        Assert.Equal("A", (await Store.GetAsync(Book, "publishers/p1/books/b1"))!.Fields["author"]); // unchanged
    }

    [Fact]
    public async Task Delete_with_stale_expected_update_time_fails_then_matching_succeeds()
    {
        await Store.InsertAsync(Book, NewBook("b1", "A", 1), P1);
        Assert.False(await Store.DeleteAsync(Book, "publishers/p1/books/b1", expectedUpdateTime: "1999-01-01T00:00:00Z"));
        Assert.NotNull(await Store.GetAsync(Book, "publishers/p1/books/b1")); // still there
        Assert.True(await Store.DeleteAsync(Book, "publishers/p1/books/b1", expectedUpdateTime: "2024-01-01T00:00:00Z"));
    }

    // ---- list contract: scope, pagination, filter, wildcard ----

    [Fact]
    public async Task List_scopes_to_parent()
    {
        await Store.InsertAsync(Book, NewBook("b1", "A", 1), P1);
        await Store.InsertAsync(Book, NewBook("b2", "A", 2, parent: "p2"), Parent("p2"));

        var p1 = await Store.ListAsync(Book, P1, new ListOptions());
        Assert.Single(p1.Items);
        Assert.Equal("b1", p1.Items[0].Id);
    }

    [Fact]
    public async Task List_paginates_with_token()
    {
        for (var i = 1; i <= 5; i++)
            await Store.InsertAsync(Book, NewBook($"b{i}", "A", i), P1);

        var seen = new List<string>();
        string? token = null;
        do
        {
            var page = await Store.ListAsync(Book, P1, new ListOptions { PageSize = 2, PageToken = token });
            Assert.True(page.Items.Count <= 2);
            seen.AddRange(page.Items.Select(i => i.Id));
            token = page.NextPageToken;
        }
        while (token is not null);

        Assert.Equal(new[] { "b1", "b2", "b3", "b4", "b5" }, seen.OrderBy(x => x).ToArray());
        Assert.Equal(5, seen.Distinct().Count()); // no overlap
    }

    [Fact]
    public async Task List_applies_filter()
    {
        await Store.InsertAsync(Book, NewBook("b1", "Orwell", 10), P1);
        await Store.InsertAsync(Book, NewBook("b2", "Huxley", 20), P1);
        await Store.InsertAsync(Book, NewBook("b3", "Orwell", 30), P1);

        var result = await Store.ListAsync(Book, P1, new ListOptions
        {
            Filter = FilterParser.Parse("author == \"Orwell\" && price > 15"),
        });
        Assert.Single(result.Items);
        Assert.Equal("b3", result.Items[0].Id);
    }

    [Fact]
    public async Task List_rejects_unknown_filter_field()
    {
        await Store.InsertAsync(Book, NewBook("b1", "A", 1), P1);
        await Assert.ThrowsAsync<FilterParseException>(() =>
            Store.ListAsync(Book, P1, new ListOptions { Filter = FilterParser.Parse("bogus == 1") }));
    }

    [Fact]
    public async Task List_across_collections_with_wildcard_parent()
    {
        await Store.InsertAsync(Book, NewBook("b1", "A", 1), P1);
        await Store.InsertAsync(Book, NewBook("b2", "A", 2, parent: "p2"), Parent("p2"));

        var all = await Store.ListAsync(Book, Parent(ResourceDefinition.WildcardCollectionId), new ListOptions());
        Assert.Equal(new[] { "b1", "b2" }, all.Items.Select(i => i.Id).OrderBy(x => x).ToArray());
    }
}
