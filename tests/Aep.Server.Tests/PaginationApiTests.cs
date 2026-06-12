using System.Net;
using System.Text;
using System.Text.Json;

namespace Aep.Server.Tests;

/// <summary>Verifies AEP-158 page-token behavior: opaque, unforgeable, and validated inputs.</summary>
public sealed class PaginationApiTests : IDisposable
{
    private readonly AepAppFactory _factory = new();
    private readonly HttpClient _client;

    public PaginationApiTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Body(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task SeedAsync(int count)
    {
        await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}"""));
        for (var i = 0; i < count; i++)
            await _client.PostAsync($"/publishers/acme/books?id=b{i}", Json($$"""{"title":"T{{i}}"}"""));
    }

    [Fact]
    public async Task Token_is_opaque_and_does_not_leak_the_cursor_id()
    {
        await SeedAsync(3);
        var page1 = await Body(await _client.GetAsync("/publishers/acme/books?max_page_size=1"));
        var lastId = page1.GetProperty("results")[0].GetProperty("id").GetString()!;
        var token = page1.GetProperty("next_page_token").GetString()!;

        // URL-safe alphabet only.
        Assert.Matches("^[A-Za-z0-9_-]+$", token);

        // The decoded token bytes must not contain the cursor id (it's encrypted, not base64(id)).
        var decoded = Encoding.UTF8.GetString(Base64UrlDecode(token));
        Assert.DoesNotContain(lastId, decoded);
    }

    [Fact]
    public async Task Pagination_round_trips_through_the_api()
    {
        await SeedAsync(5);
        var ids = new List<string>();
        string? token = null;
        do
        {
            var url = "/publishers/acme/books?max_page_size=2" + (token is null ? "" : $"&page_token={Uri.EscapeDataString(token)}");
            var page = await Body(await _client.GetAsync(url));
            ids.AddRange(page.GetProperty("results").EnumerateArray().Select(b => b.GetProperty("id").GetString()!));
            token = page.TryGetProperty("next_page_token", out var t) ? t.GetString() : null;
        }
        while (token is not null);

        Assert.Equal(5, ids.Distinct().Count());
    }

    [Theory]
    [InlineData("not-a-real-token")]      // forged / garbage
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA")] // well-formed base64 but fails authentication
    public async Task Forged_or_tampered_token_returns_400(string token)
    {
        await SeedAsync(1);
        var resp = await _client.GetAsync($"/publishers/acme/books?page_token={Uri.EscapeDataString(token)}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Tampering_with_a_real_token_returns_400()
    {
        await SeedAsync(3);
        var page1 = await Body(await _client.GetAsync("/publishers/acme/books?max_page_size=1"));
        var token = page1.GetProperty("next_page_token").GetString()!;
        var tampered = (token[0] == 'A' ? 'B' : 'A') + token[1..]; // flip the first char

        var resp = await _client.GetAsync($"/publishers/acme/books?page_token={Uri.EscapeDataString(tampered)}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Negative_max_page_size_returns_400()
    {
        await SeedAsync(1);
        var resp = await _client.GetAsync("/publishers/acme/books?max_page_size=-5");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal(400, (await Body(resp)).GetProperty("status").GetInt32());
    }

    private static byte[] Base64UrlDecode(string token)
    {
        var base64 = token.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(base64);
    }
}
