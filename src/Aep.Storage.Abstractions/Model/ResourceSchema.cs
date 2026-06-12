namespace Aep.Storage.Abstractions.Model;

/// <summary>
/// A minimal OpenAPI-subset schema for a resource. Mirrors the shape used by
/// aep-lib-go / aepc YAML: an object with named properties. AEP extensions such
/// as <c>x-aep-field</c>/<c>field_number</c> are ignored.
/// </summary>
public sealed class ResourceSchema
{
    /// <summary>JSON schema type. For resources this is always "object".</summary>
    public string Type { get; init; } = "object";

    /// <summary>Property name -> property schema.</summary>
    public IReadOnlyDictionary<string, SchemaProperty> Properties { get; init; }
        = new Dictionary<string, SchemaProperty>();

    /// <summary>Names of required properties (AEP-203 field behavior).</summary>
    public IReadOnlyList<string> Required { get; init; } = [];
}

/// <summary>Schema for a single resource property.</summary>
public sealed class SchemaProperty
{
    /// <summary>One of: string, integer, number, boolean, object, array.</summary>
    public string Type { get; init; } = "string";

    /// <summary>Optional OpenAPI format hint (e.g. int32, double, date-time).</summary>
    public string? Format { get; init; }

    /// <summary>Output-only fields are server-managed and rejected on writes (AEP-203).</summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// Immutable fields may be set on create but not changed by an update (AEP-203).
    /// Providing one in a PATCH is rejected with INVALID_ARGUMENT.
    /// </summary>
    public bool Immutable { get; init; }

    /// <summary>
    /// Input-only fields are accepted on writes but never returned in responses (AEP-203).
    /// </summary>
    public bool InputOnly { get; init; }

    /// <summary>Human-readable description, surfaced in the OpenAPI spec.</summary>
    public string? Description { get; init; }

    /// <summary>Allowed string values, when the property is an enum (AEP-126).</summary>
    public IReadOnlyList<string>? Enum { get; init; }

    /// <summary>Item schema when <see cref="Type"/> is "array".</summary>
    public SchemaProperty? Items { get; init; }
}
