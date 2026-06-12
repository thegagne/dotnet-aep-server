namespace Aep.Storage.Abstractions.Model;

/// <summary>The full service description loaded from <c>resources.yaml</c>.</summary>
public sealed class ServiceDefinition
{
    public required string Name { get; init; }
    public string? ServerUrl { get; init; }
    public Contact? Contact { get; init; }

    /// <summary>Resources keyed by singular name.</summary>
    public required IReadOnlyDictionary<string, ResourceDefinition> Resources { get; init; }
}

public sealed class Contact
{
    public string? Name { get; init; }
    public string? Email { get; init; }
    public string? Url { get; init; }
}
