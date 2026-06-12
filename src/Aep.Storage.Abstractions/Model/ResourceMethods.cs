namespace Aep.Storage.Abstractions.Model;

/// <summary>
/// Which AEP standard methods a resource exposes, and their options. Mirrors
/// aep-lib-go's <c>Methods</c> struct. Only the subset relevant to v1 is honored
/// (custom methods, long-running operations, and singletons are out of scope).
/// </summary>
public sealed class ResourceMethods
{
    public bool Get { get; init; } = true;
    public bool List { get; init; } = true;
    public bool Create { get; init; } = true;
    public bool Update { get; init; } = true;
    public bool Delete { get; init; } = true;
    public bool Apply { get; init; } = true;

    /// <summary>AEP-133: allow clients to set the resource id on create (<c>?id=</c>).</summary>
    public bool SupportsUserSettableCreate { get; init; } = true;

    /// <summary>AEP-160: allow the <c>filter</c> query parameter on list.</summary>
    public bool SupportsFilter { get; init; } = true;

    /// <summary>AEP-158: allow the <c>skip</c> query parameter on list.</summary>
    public bool SupportsSkip { get; init; } = true;
}
