using Aep.Server.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Aep.Server.Tests;

/// <summary>Verifies AddResourceIndex(...) attaches code-declared indexes to the resource at startup.</summary>
public sealed class ResourceIndexTests : IDisposable
{
    private sealed class IndexedFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(AppContext.BaseDirectory);
            builder.UseSetting("Resources:File", Path.Combine(AppContext.BaseDirectory, "resources.yaml"));
            builder.UseSetting("Storage:Provider", "inmemory");
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(s => s.AddResourceIndex("book", "author"));
        }
    }

    private readonly IndexedFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void Declared_index_is_attached_to_the_resource()
    {
        _ = _factory.CreateClient(); // triggers startup -> MapAepServerAsync -> ApplyIndexes

        var book = _factory.Services.GetRequiredService<IResourceRegistry>().Get("book");
        Assert.Contains(book.Indexes, i => i.Fields.SequenceEqual(["author"]));
    }

    [Fact]
    public void Unknown_index_field_fails_fast()
    {
        using var bad = new BadFactory();
        Assert.ThrowsAny<Exception>(() => bad.CreateClient());
    }

    private sealed class BadFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(AppContext.BaseDirectory);
            builder.UseSetting("Resources:File", Path.Combine(AppContext.BaseDirectory, "resources.yaml"));
            builder.UseSetting("Storage:Provider", "inmemory");
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(s => s.AddResourceIndex("book", "nonexistent_field"));
        }
    }
}
