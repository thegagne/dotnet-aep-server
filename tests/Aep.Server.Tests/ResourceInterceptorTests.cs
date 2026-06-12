using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Aep.Server.Tests;

/// <summary>Verifies per-(resource, method) interceptors fire only for their exact resource + method.</summary>
public sealed class ResourceInterceptorTests : IDisposable
{
    // Stand-in for an event bus; records what was published.
    private sealed class RecordingBus
    {
        public List<string> Events { get; } = [];
    }

    private sealed class InterceptedFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(AppContext.BaseDirectory);
            builder.UseSetting("Resources:File", Path.Combine(AppContext.BaseDirectory, "resources.yaml"));
            builder.UseSetting("Storage:Provider", "inmemory");
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(s =>
            {
                s.AddSingleton<RecordingBus>();
                // Only book Create publishes an event.
                s.OnCreate("book", async (request, next) =>
                {
                    var response = await next(request);
                    request.Services.GetRequiredService<RecordingBus>().Events.Add($"book.created:{response.Resource.Id}");
                    return response;
                });
            });
        }
    }

    private readonly InterceptedFactory _factory = new();
    private readonly HttpClient _client;

    public ResourceInterceptorTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private RecordingBus Bus => _factory.Services.GetRequiredService<RecordingBus>();

    [Fact]
    public async Task Interceptor_runs_only_for_its_resource_and_method()
    {
        // Publisher Create — different resource, must NOT trigger the book interceptor.
        await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}"""));
        Assert.Empty(Bus.Events);

        // Book Create — triggers exactly once.
        var create = await _client.PostAsync("/publishers/acme/books?id=b1", Json("""{"title":"1984"}"""));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal(["book.created:b1"], Bus.Events);

        // Book Get and List — same resource, different method, must NOT trigger it.
        await _client.GetAsync("/publishers/acme/books/b1");
        await _client.GetAsync("/publishers/acme/books");
        Assert.Equal(["book.created:b1"], Bus.Events);

        // Another book Create — fires again.
        await _client.PostAsync("/publishers/acme/books?id=b2", Json("""{"title":"BNW"}"""));
        Assert.Equal(["book.created:b1", "book.created:b2"], Bus.Events);
    }

    [Fact]
    public async Task Interceptor_wraps_the_built_in_operation()
    {
        await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}"""));

        // The book was actually persisted by the wrapped (built-in) create the interceptor called.
        await _client.PostAsync("/publishers/acme/books?id=b1", Json("""{"title":"1984"}"""));
        var get = await _client.GetAsync("/publishers/acme/books/b1");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Single(Bus.Events);
    }
}
