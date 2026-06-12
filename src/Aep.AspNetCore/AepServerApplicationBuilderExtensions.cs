using Aep.Server.Configuration;
using Aep.Server.Http;
using Aep.Server.OpenApi;
using Aep.Server.Routing;
using Aep.Storage.Abstractions.Model;
using Aep.Storage.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Wires the AEP server into the request pipeline.</summary>
public static class AepServerApplicationBuilderExtensions
{
    /// <summary>
    /// Ensures the storage schema exists, installs AEP-193 error handling, and maps
    /// the resource routes plus <c>GET /openapi.json</c>. Call after registering a
    /// storage provider and after any of your own middleware.
    /// </summary>
    public static async Task MapAepServerAsync(this WebApplication app)
    {
        var registry = app.Services.GetRequiredService<IResourceRegistry>();
        var store = app.Services.GetRequiredService<IResourceStore>();

        ApplyIndexes(registry, app.Services.GetRequiredService<IOptions<ResourceIndexOptions>>().Value);
        await store.EnsureSchemaAsync(registry.All);

        // AEP-193 error shaping wraps everything downstream (including endpoints).
        app.UseMiddleware<AepErrorMiddleware>();

        // Buffer request bodies so the controller can re-read raw JSON even if a form
        // value-provider consumed the stream (e.g. a misset Content-Type).
        app.Use(async (context, next) =>
        {
            context.Request.EnableBuffering();
            await next();
        });

        app.MapAepResources(registry);

        // The resource set is fixed, so generate the spec once.
        var openApiJson = new OpenApiGenerator(registry).Generate().ToJsonString();
        app.MapGet("/openapi.json", () => Results.Text(openApiJson, "application/json"));
    }

    private static readonly string[] StandardFields = ["id", "uid", "path", "create_time", "update_time"];

    /// <summary>Attaches code-declared indexes to their resources, validating field names.</summary>
    private static void ApplyIndexes(IResourceRegistry registry, ResourceIndexOptions options)
    {
        foreach (var group in options.Declarations.GroupBy(d => d.Singular))
        {
            if (!registry.TryGet(group.Key, out var resource))
                throw new InvalidOperationException($"AddResourceIndex: unknown resource '{group.Key}'.");

            foreach (var (_, fields) in group)
                foreach (var field in fields)
                    if (!resource.Schema.Properties.ContainsKey(field) && !StandardFields.Contains(field))
                        throw new InvalidOperationException(
                            $"AddResourceIndex: resource '{group.Key}' has no field '{field}'.");

            resource.Indexes = group.Select(d => new ResourceIndex { Fields = d.Fields }).ToList();
        }
    }
}
