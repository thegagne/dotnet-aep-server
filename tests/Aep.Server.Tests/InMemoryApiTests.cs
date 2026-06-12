using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Aep.Server.Tests;

/// <summary>
/// Exercises the full API served by the <c>inmemory</c> storage provider, confirming
/// the host works end-to-end without SQLite.
/// </summary>
public sealed class InMemoryApiTests : IDisposable
{
    private sealed class InMemoryFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(AppContext.BaseDirectory);
            builder.UseSetting("Resources:File", Path.Combine(AppContext.BaseDirectory, "resources.yaml"));
            builder.UseSetting("Storage:Provider", "inmemory");
            builder.UseEnvironment("Testing");
        }
    }

    private readonly InMemoryFactory _factory = new();
    private readonly HttpClient _client;

    public InMemoryApiTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Body(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Crud_filter_and_pagination_over_in_memory()
    {
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}"""))).StatusCode);

        var create = await _client.PostAsync("/publishers/acme/books?id=b1",
            Json("""{"title":"1984","author":"Orwell","price":1200,"published":true}"""));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal("publishers/acme/books/b1", (await Body(create)).GetProperty("path").GetString());

        await _client.PostAsync("/publishers/acme/books?id=b2", Json("""{"title":"BNW","author":"Huxley","price":1500}"""));

        // Get + patch.
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/publishers/acme/books/b1")).StatusCode);
        var patched = await Body(await _client.PatchAsync("/publishers/acme/books/b1", Json("""{"author":"George Orwell"}""")));
        Assert.Equal("George Orwell", patched.GetProperty("author").GetString());

        // CEL filter.
        var filtered = await Body(await _client.GetAsync(
            "/publishers/acme/books?filter=" + Uri.EscapeDataString("author == \"George Orwell\" || price > 1400")));
        Assert.Equal(2, filtered.GetProperty("results").GetArrayLength());

        // Pagination.
        var page1 = await Body(await _client.GetAsync("/publishers/acme/books?max_page_size=1"));
        Assert.Equal(1, page1.GetProperty("results").GetArrayLength());
        Assert.False(string.IsNullOrEmpty(page1.GetProperty("next_page_token").GetString()));

        // Delete.
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/publishers/acme/books/b1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/publishers/acme/books/b1")).StatusCode);
    }

    [Fact]
    public async Task Invalid_filter_returns_400_over_in_memory()
    {
        await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}"""));
        var resp = await _client.GetAsync("/publishers/acme/books?filter=" + Uri.EscapeDataString("bogus == 1"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
