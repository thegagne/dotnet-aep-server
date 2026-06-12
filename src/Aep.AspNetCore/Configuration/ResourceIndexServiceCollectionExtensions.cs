namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Holds index declarations until they are applied to the resources at startup.</summary>
public sealed class ResourceIndexOptions
{
    internal List<(string Singular, IReadOnlyList<string> Fields)> Declarations { get; } = [];
}

/// <summary>Declares secondary indexes on resources from code.</summary>
public static class ResourceIndexServiceCollectionExtensions
{
    /// <summary>
    /// Declares an index over one or more fields of a resource. Relational backends create a
    /// btree index to speed up filtering/ordering on those fields. Call after <c>AddAepServer</c>.
    /// </summary>
    public static IServiceCollection AddResourceIndex(this IServiceCollection services, string singular, params string[] fields)
    {
        if (fields.Length == 0)
            throw new ArgumentException("an index needs at least one field", nameof(fields));
        return services.Configure<ResourceIndexOptions>(o => o.Declarations.Add((singular, fields)));
    }
}
