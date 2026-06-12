using System.Net;
using System.Text;
using System.Text.Json;
using Aep.Server.Backend;
using Aep.Storage.Abstractions.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Aep.Server.Tests;

/// <summary>Verifies the IResourceBackend decorator can transform, short-circuit, and wrap operations.</summary>
public sealed class BackendDecoratorTests : IDisposable
{
    // Wraps the built-in backend; overrides only what it needs (the rest delegates to Backend).
    // A decorator wraps every resource, so it scopes its logic to "book" and passes the rest through.
    private sealed class BookBackend(IResourceBackend backend) : ResourceBackendDecorator(backend)
    {
        public override Task<CreateResponse> CreateAsync(CreateRequest request)
        {
            if (request.Resource.Singular != "book")
                return Backend.CreateAsync(request);

            // Pre-hook: transform the request before it reaches the built-in backend.
            request.Fields["author"] = "by-decorator";
            return Backend.CreateAsync(request);
        }

        public override Task<GetResponse> GetAsync(GetRequest request)
        {
            // Short-circuit: serve a synthetic resource without ever calling the store.
            if (request.Resource.Singular == "book" && request.Path.EndsWith("/ghost"))
            {
                var synthetic = new StoredResource
                {
                    Id = "ghost",
                    Path = request.Path,
                    CreateTime = "2026-01-01T00:00:00Z",
                    UpdateTime = "2026-01-01T00:00:00Z",
                    Fields = new Dictionary<string, object?> { ["title"] = "synthetic" },
                };
                return Task.FromResult(new GetResponse { Resource = synthetic });
            }
            return Backend.GetAsync(request);
        }
    }

    private sealed class DecoratedFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(AppContext.BaseDirectory);
            builder.UseSetting("Resources:File", Path.Combine(AppContext.BaseDirectory, "resources.yaml"));
            builder.UseSetting("Storage:Provider", "inmemory");
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(s => s.DecorateResourceBackend<BookBackend>());
        }
    }

    private readonly DecoratedFactory _factory = new();
    private readonly HttpClient _client;

    public BackendDecoratorTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Body(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Decorator_transforms_request_before_built_in_backend()
    {
        await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}"""));

        var create = await _client.PostAsync("/publishers/acme/books?id=b1",
            Json("""{"title":"1984","author":"original"}"""));
        Assert.Equal("by-decorator", (await Body(create)).GetProperty("author").GetString());

        // The transformed value was persisted by the inner backend.
        Assert.Equal("by-decorator",
            (await Body(await _client.GetAsync("/publishers/acme/books/b1"))).GetProperty("author").GetString());
    }

    [Fact]
    public async Task Decorator_can_short_circuit_an_operation()
    {
        // "ghost" was never created, but the decorator serves it without touching the store.
        var resp = await _client.GetAsync("/publishers/acme/books/ghost");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("synthetic", (await Body(resp)).GetProperty("title").GetString());
    }
}
