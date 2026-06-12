using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Aep.Server.Tests;

/// <summary>
/// A resource marked <c>not_implemented</c> is a routing parent only (#03): its own paths return
/// 501, but its children are fully served. Uses a temp resources file with such a hierarchy.
/// </summary>
public sealed class NotImplementedParentTests : IDisposable
{
    private readonly string _yamlPath;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public NotImplementedParentTests()
    {
        _yamlPath = Path.Combine(Path.GetTempPath(), $"aep-ni-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(_yamlPath, """
            name: "test.example.com"
            resources:
              tenant:
                singular: "tenant"
                plural: "tenants"
                not_implemented: true
                schema:
                  type: object
                  properties:
                    display_name: { type: string }
              widget:
                singular: "widget"
                plural: "widgets"
                parents: ["tenant"]
                schema:
                  type: object
                  required: ["name"]
                  properties:
                    name: { type: string }
            """);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseContentRoot(AppContext.BaseDirectory);
            b.UseSetting("Resources:File", _yamlPath);
            b.UseSetting("Storage:Provider", "sqlite");
            b.UseSetting("Storage:Sqlite:ConnectionString", "Data Source=:memory:");
            b.UseEnvironment("Testing");
        });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        if (File.Exists(_yamlPath)) File.Delete(_yamlPath);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Children_are_served_under_a_not_implemented_parent()
    {
        // The tenant was never created (it has no Create), yet its widgets work fine.
        var create = await _client.PostAsync("/tenants/acme/widgets?id=w1", Json("""{"name":"Gadget"}"""));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal("tenants/acme/widgets/w1", (await BodyAsync(create)).GetProperty("path").GetString());

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/tenants/acme/widgets/w1")).StatusCode);

        var list = await BodyAsync(await _client.GetAsync("/tenants/acme/widgets"));
        Assert.Equal(1, list.GetProperty("results").GetArrayLength());
    }

    [Theory]
    [InlineData("GET", "/tenants")]
    [InlineData("POST", "/tenants")]
    [InlineData("GET", "/tenants/acme")]
    [InlineData("PATCH", "/tenants/acme")]
    [InlineData("DELETE", "/tenants/acme")]
    public async Task Parents_own_methods_return_501(string method, string url)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PATCH") req.Content = Json("{}");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotImplemented, resp.StatusCode);
    }

    [Fact]
    public async Task OpenApi_omits_the_not_implemented_parent_but_keeps_children()
    {
        var doc = await BodyAsync(await _client.GetAsync("/openapi.json"));
        var paths = doc.GetProperty("paths");

        Assert.False(paths.TryGetProperty("/tenants", out _));
        Assert.False(paths.TryGetProperty("/tenants/{tenant_id}", out _));
        Assert.True(paths.TryGetProperty("/tenants/{tenant_id}/widgets", out _));

        var schemas = doc.GetProperty("components").GetProperty("schemas");
        Assert.False(schemas.TryGetProperty("tenant", out _));
        Assert.True(schemas.TryGetProperty("widget", out _));
    }
}
