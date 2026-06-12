using System.Net;
using System.Text;

namespace Aep.Server.Tests;

/// <summary>
/// ETag + If-Match optimistic concurrency (AEP-154 / #12), exercised through the full HTTP stack.
/// </summary>
public sealed class EtagConcurrencyTests : IDisposable
{
    private readonly AepAppFactory _factory = new();
    private readonly HttpClient _client;

    public EtagConcurrencyTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<HttpResponseMessage> Send(
        HttpMethod method, string url, string? json = null, string? ifMatch = null, string? ifNoneMatch = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (ifNoneMatch is not null) req.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        return await _client.SendAsync(req);
    }

    private async Task<string> CreateAcme()
    {
        var create = await Send(HttpMethod.Post, "/publishers?id=acme", """{"display_name":"Acme"}""");
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var etag = create.Headers.ETag?.ToString();
        Assert.False(string.IsNullOrEmpty(etag));
        return etag!;
    }

    [Fact]
    public async Task Etag_is_returned_and_changes_on_modification()
    {
        var e1 = await CreateAcme();
        var get = await Send(HttpMethod.Get, "/publishers/acme");
        Assert.Equal(e1, get.Headers.ETag?.ToString()); // stable while unchanged

        var patch = await Send(HttpMethod.Patch, "/publishers/acme", """{"display_name":"Acme 2"}""");
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.NotEqual(e1, patch.Headers.ETag?.ToString()); // changes on update
    }

    [Fact]
    public async Task IfMatch_allows_current_and_rejects_stale()
    {
        var e1 = await CreateAcme();

        // A matching If-Match succeeds and yields a fresh etag.
        var ok = await Send(HttpMethod.Patch, "/publishers/acme", """{"display_name":"v2"}""", ifMatch: e1);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var e2 = ok.Headers.ETag!.ToString();

        // Reusing the now-stale etag fails the precondition.
        var stale = await Send(HttpMethod.Patch, "/publishers/acme", """{"display_name":"v3"}""", ifMatch: e1);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        // The current etag still works.
        var fresh = await Send(HttpMethod.Patch, "/publishers/acme", """{"display_name":"v3"}""", ifMatch: e2);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Fact]
    public async Task IfMatch_wildcard_matches_any_existing()
    {
        await CreateAcme();
        var ok = await Send(HttpMethod.Patch, "/publishers/acme", """{"display_name":"w"}""", ifMatch: "*");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task IfMatch_on_missing_resource_is_412()
    {
        var resp = await Send(HttpMethod.Delete, "/publishers/ghost", ifMatch: "\"anything\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_honors_ifmatch()
    {
        var e1 = await CreateAcme();
        var e2 = (await Send(HttpMethod.Patch, "/publishers/acme", """{"display_name":"v2"}""", ifMatch: e1))
            .Headers.ETag!.ToString();

        Assert.Equal(HttpStatusCode.PreconditionFailed,
            (await Send(HttpMethod.Delete, "/publishers/acme", ifMatch: e1)).StatusCode); // stale
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(HttpMethod.Delete, "/publishers/acme", ifMatch: e2)).StatusCode); // current
    }

    [Fact]
    public async Task Get_with_matching_ifmatch_succeeds_mismatch_412()
    {
        var e1 = await CreateAcme();
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/publishers/acme", ifMatch: e1)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed,
            (await Send(HttpMethod.Get, "/publishers/acme", ifMatch: "\"stale\"")).StatusCode);
    }

    [Fact]
    public async Task IfNoneMatch_is_rejected_as_unsupported()
    {
        await CreateAcme();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(HttpMethod.Get, "/publishers/acme", ifNoneMatch: "\"x\"")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(HttpMethod.Patch, "/publishers/acme", """{"display_name":"x"}""", ifNoneMatch: "*")).StatusCode);
    }
}
