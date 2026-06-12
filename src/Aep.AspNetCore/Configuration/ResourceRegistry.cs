using Aep.Storage.Abstractions.Model;

namespace Aep.Server.Configuration;

/// <summary>Read-only lookup of the loaded resources, with computed URL patterns.</summary>
public interface IResourceRegistry
{
    ServiceDefinition Service { get; }
    IReadOnlyCollection<ResourceDefinition> All { get; }
    ResourceDefinition Get(string singular);
    bool TryGet(string singular, out ResourceDefinition resource);
}

/// <summary>
/// Builds the registry from a <see cref="ServiceDefinition"/>: validates parent
/// references and fills in each resource's <see cref="ResourceDefinition.PatternElems"/>
/// by walking the (first-)parent chain, mirroring aep-lib-go's pattern generation.
/// </summary>
public sealed class ResourceRegistry : IResourceRegistry
{
    private readonly Dictionary<string, ResourceDefinition> _bySingular;

    public ResourceRegistry(ServiceDefinition service)
    {
        Service = service;
        _bySingular = new Dictionary<string, ResourceDefinition>(service.Resources, StringComparer.Ordinal);

        foreach (var r in _bySingular.Values)
            foreach (var parent in r.Parents)
                if (!_bySingular.ContainsKey(parent))
                    throw new InvalidOperationException(
                        $"resource '{r.Singular}' references unknown parent '{parent}'");

        foreach (var r in _bySingular.Values)
            r.PatternElems = ComputePatternElems(r, []);
    }

    public ServiceDefinition Service { get; }

    public IReadOnlyCollection<ResourceDefinition> All => _bySingular.Values;

    public ResourceDefinition Get(string singular) => _bySingular[singular];

    public bool TryGet(string singular, out ResourceDefinition resource) =>
        _bySingular.TryGetValue(singular, out resource!);

    private IReadOnlyList<string> ComputePatternElems(ResourceDefinition r, HashSet<string> visiting)
    {
        if (!visiting.Add(r.Singular))
            throw new InvalidOperationException($"cyclic parent relationship involving '{r.Singular}'");

        var elems = new List<string>();
        if (r.Parents.Count > 0)
            elems.AddRange(ComputePatternElems(_bySingular[r.Parents[0]], visiting));

        elems.Add(r.Plural);
        elems.Add("{" + ResourceDefinition.ParamNameFor(r.Singular) + "}");

        visiting.Remove(r.Singular);
        return elems;
    }
}
