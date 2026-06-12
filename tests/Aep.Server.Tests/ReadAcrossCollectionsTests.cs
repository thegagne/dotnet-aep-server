using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Aep.Server.Tests;

/// <summary>
/// Reading across collections with the AEP-159 <c>-</c> wildcard parent (#04), end to end.
/// </summary>
public sealed class ReadAcrossCollectionsTests : IDisposable
{
    private readonly AepAppFactory _factory = new();
    private readonly HttpClient _client;

    public ReadAcrossCollectionsTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task Seed()
    {
        await _client.PostAsync("/publishers?id=p1", Json("""{"display_name":"P1"}"""));
        await _client.PostAsync("/publishers?id=p2", Json("""{"display_name":"P2"}"""));
        await _client.PostAsync("/publishers/p1/books?id=bk1", Json("""{"title":"A","author":"X"}"""));
        await _client.PostAsync("/publishers/p2/books?id=bk2", Json("""{"title":"B","author":"X"}"""));
        await _client.PostAsync("/publishers/p2/books?id=bk3", Json("""{"title":"C","author":"Y"}"""));
    }

    private static string[] Paths(JsonElement body) =>
        body.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("path").GetString()!).OrderBy(p => p).ToArray();

    [Fact]
    public async Task Wildcard_lists_books_across_all_publishers_with_full_paths()
    {
        await Seed();

        var all = await BodyAsync(await _client.GetAsync("/publishers/-/books"));
        Assert.Equal(
            new[] { "publishers/p1/books/bk1", "publishers/p2/books/bk2", "publishers/p2/books/bk3" },
            Paths(all)); // every item carries its real parent in the path

        // A concrete parent still scopes normally.
        var scoped = await BodyAsync(await _client.GetAsync("/publishers/p2/books"));
        Assert.Equal(2, scoped.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task Wildcard_list_honors_filter()
    {
        await Seed();
        var filtered = await BodyAsync(
            await _client.GetAsync("/publishers/-/books?filter=" + Uri.EscapeDataString("author == \"X\"")));
        Assert.Equal(
            new[] { "publishers/p1/books/bk1", "publishers/p2/books/bk2" },
            Paths(filtered));
    }

    [Fact]
    public async Task Wildcard_list_paginates_across_collections()
    {
        await Seed(); // bk1, bk2, bk3 across p1/p2

        var page1 = await BodyAsync(await _client.GetAsync("/publishers/-/books?max_page_size=2"));
        Assert.Equal(2, page1.GetProperty("results").GetArrayLength());
        var token = page1.GetProperty("next_page_token").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        var page2 = await BodyAsync(
            await _client.GetAsync($"/publishers/-/books?max_page_size=2&page_token={Uri.EscapeDataString(token!)}"));
        Assert.Single(page2.GetProperty("results").EnumerateArray());

        // The two pages together cover all three, no overlap.
        var seen = Paths(page1).Concat(Paths(page2)).ToHashSet();
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public async Task Wildcard_works_for_a_grandchild_collection()
    {
        await _client.PostAsync("/publishers?id=p1", Json("""{"display_name":"P1"}"""));
        await _client.PostAsync("/publishers/p1/books?id=b1", Json("""{"title":"One"}"""));
        await _client.PostAsync("/publishers/p1/books?id=b2", Json("""{"title":"Two"}"""));
        await _client.PostAsync("/publishers/p1/books/b1/chapters?id=c1", Json("""{"title":"Ch1"}"""));
        await _client.PostAsync("/publishers/p1/books/b2/chapters?id=c2", Json("""{"title":"Ch2"}"""));

        // Wildcard on the book collection lists chapters across both books.
        var acrossBooks = await BodyAsync(await _client.GetAsync("/publishers/p1/books/-/chapters"));
        Assert.Equal(
            new[] { "publishers/p1/books/b1/chapters/c1", "publishers/p1/books/b2/chapters/c2" },
            Paths(acrossBooks));
    }
}
