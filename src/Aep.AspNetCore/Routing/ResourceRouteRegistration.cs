using Aep.Server.Configuration;
using Aep.Storage.Abstractions.Model;

namespace Aep.Server.Routing;

/// <summary>
/// Registers conventional MVC routes for every resource at startup. Each route
/// targets the single <c>ResourceController</c> action for one AEP method, is
/// constrained to one HTTP verb, and carries the resource singular as the
/// <c>resourceKey</c> data token so the controller can resolve its definition.
/// </summary>
public static class ResourceRouteRegistration
{
    public static void MapAepResources(this IEndpointRouteBuilder endpoints, IResourceRegistry registry)
    {
        foreach (var r in registry.All)
        {
            if (r.NotImplemented)
            {
                // Routing parent only: its own paths answer 501 (any verb); children are unaffected.
                MapAny(endpoints, r, r.CollectionPattern);
                MapAny(endpoints, r, r.ItemPattern);
                continue;
            }

            var m = r.Methods;
            if (m.Create) Map(endpoints, r, "Create", r.CollectionPattern, HttpMethods.Post);
            if (m.List) Map(endpoints, r, "List", r.CollectionPattern, HttpMethods.Get);
            if (m.Get) Map(endpoints, r, "Get", r.ItemPattern, HttpMethods.Get);
            if (m.Update) Map(endpoints, r, "Update", r.ItemPattern, HttpMethods.Patch);
            if (m.Apply) Map(endpoints, r, "Apply", r.ItemPattern, HttpMethods.Put);
            if (m.Delete) Map(endpoints, r, "Delete", r.ItemPattern, HttpMethods.Delete);
        }
    }

    /// <summary>Maps a pattern (any HTTP verb) to the controller's 501 action for a not-implemented resource.</summary>
    private static void MapAny(IEndpointRouteBuilder endpoints, ResourceDefinition resource, string pattern) =>
        endpoints.MapControllerRoute(
            name: $"{resource.Singular}.NotImplemented.{pattern}",
            pattern: pattern,
            defaults: new { controller = "Resource", action = "NotImplemented" },
            dataTokens: new { resourceKey = resource.Singular });

    private static void Map(
        IEndpointRouteBuilder endpoints, ResourceDefinition resource,
        string action, string pattern, string httpMethod)
    {
        // The HTTP verb is enforced by the action's [HttpGet]/[HttpPost]/... attribute;
        // the route just pins controller + action and carries the resource key.
        endpoints.MapControllerRoute(
            name: $"{resource.Singular}.{action}.{httpMethod}",
            pattern: pattern,
            defaults: new { controller = "Resource", action },
            dataTokens: new { resourceKey = resource.Singular });
    }
}
