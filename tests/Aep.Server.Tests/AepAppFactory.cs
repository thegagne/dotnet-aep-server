using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Aep.Server.Tests;

/// <summary>
/// Hosts the real app for integration tests against an isolated in-memory SQLite
/// database. A fresh factory per test (xunit creates one test-class instance per
/// test) gives each test a clean database.
/// </summary>
public sealed class AepAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseSetting("Resources:File", Path.Combine(AppContext.BaseDirectory, "resources.yaml"));
        builder.UseSetting("Storage:Provider", "sqlite");
        builder.UseSetting("Storage:Sqlite:ConnectionString", "Data Source=:memory:");
        builder.UseEnvironment("Testing");
    }
}
