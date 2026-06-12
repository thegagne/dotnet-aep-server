using Aep.Server.Backend;
using Aep.Server.Configuration;
using Aep.Server.Controllers;
using Aep.Server.Http;
using Aep.Storage.Abstractions.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the AEP server's core services. Call exactly one storage provider
/// extension afterwards (e.g. <c>AddAepSqliteStore</c> or <c>AddAepInMemoryStore</c>),
/// then <c>app.MapAepServerAsync()</c>.
/// </summary>
public static class AepServerServiceCollectionExtensions
{
    /// <summary>Adds the AEP server, loading resources from the file named by
    /// configuration <c>Resources:File</c> (default <c>resources.yaml</c>).</summary>
    public static IServiceCollection AddAepServer(this IServiceCollection services, IConfiguration configuration)
    {
        var file = configuration["Resources:File"] ?? "resources.yaml";
        services.Configure<PageTokenOptions>(configuration.GetSection("PageToken"));
        return services.AddAepServer(ServiceDefinitionLoader.LoadFromFile(file));
    }

    /// <summary>Adds the AEP server for an already-loaded service definition.</summary>
    public static IServiceCollection AddAepServer(this IServiceCollection services, ServiceDefinition service)
    {
        var registry = new ResourceRegistry(service);
        services.AddSingleton<IResourceRegistry>(registry);

        // Opaque, unforgeable page tokens (AES-GCM). Key from PageToken:Key, else per-process random.
        services.AddSingleton<PageTokenProtector>();

        // RFC 9457 Problem Details (AEP-193): the framework's IProblemDetailsService handles
        // serialization, the application/problem+json content type, and exposes a
        // CustomizeProblemDetails hook so consumers can enrich errors (traceId, etc.).
        services.AddProblemDetails();

        // The operation backend: per-(resource, method) interceptors wrap the built-in default.
        // Add cross-cutting wrappers with DecorateResourceBackend(...); per-method logic with OnCreate/OnGet/...
        services.AddScoped<DefaultResourceBackend>();
        services.AddScoped<IResourceBackend>(sp => new InterceptingResourceBackend(
            sp.GetRequiredService<DefaultResourceBackend>(),
            sp.GetRequiredService<IOptions<ResourceInterceptorOptions>>()));

        services.AddControllers()
            // The generic ResourceController lives in this library assembly, so it must
            // be registered as an application part to be discovered by the host's MVC.
            .AddApplicationPart(typeof(ResourceController).Assembly)
            .AddJsonOptions(o =>
            {
                // Preserve AEP snake_case field names (no camelCasing).
                o.JsonSerializerOptions.PropertyNamingPolicy = null;
                o.JsonSerializerOptions.DictionaryKeyPolicy = null;
            });

        return services;
    }
}
