using System.Text.Json;
using Aep.Storage.Abstractions.Model;

namespace Aep.Server.Http;

/// <summary>
/// Validates request bodies against a resource schema and extracts the
/// user-defined field values. Ported from aepbase's <c>resource/validate.go</c>:
/// standard and read-only fields are stripped, required fields are enforced
/// (create/apply only), and provided values are type- and enum-checked.
/// </summary>
public static class SchemaValidator
{
    private static readonly HashSet<string> StandardFields =
        new(StringComparer.Ordinal) { "id", "uid", "path", "create_time", "update_time" };

    /// <summary>Validates and extracts fields for a full-resource write (Create / Apply).</summary>
    public static Dictionary<string, object?> ValidateForWrite(ResourceDefinition resource, JsonElement body)
        => Validate(resource, body, enforceRequired: true, rejectImmutable: false);

    /// <summary>
    /// Validates and extracts fields for a partial update (PATCH); required fields are not
    /// enforced, and immutable fields may not be changed.
    /// </summary>
    public static Dictionary<string, object?> ValidateForPatch(ResourceDefinition resource, JsonElement body)
        => Validate(resource, body, enforceRequired: false, rejectImmutable: true);

    private static Dictionary<string, object?> Validate(
        ResourceDefinition resource, JsonElement body, bool enforceRequired, bool rejectImmutable)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (body.ValueKind is JsonValueKind.Undefined)
            return EnforceRequired(resource, fields, enforceRequired);
        if (body.ValueKind != JsonValueKind.Object)
            throw new ResourceValidationException("request body must be a JSON object");

        foreach (var member in body.EnumerateObject())
        {
            var name = member.Name;
            if (StandardFields.Contains(name))
                continue; // server-managed; silently ignored
            if (!resource.Schema.Properties.TryGetValue(name, out var prop))
                continue; // unknown field; ignored (only declared columns are stored)
            if (prop.ReadOnly)
                continue; // output-only; clients may not set it
            if (prop.Immutable && rejectImmutable)
                throw new ResourceValidationException($"field \"{name}\" is immutable and cannot be changed");

            ValidateValue(name, prop, member.Value);
            fields[name] = member.Value;
        }

        return EnforceRequired(resource, fields, enforceRequired);
    }

    private static Dictionary<string, object?> EnforceRequired(
        ResourceDefinition resource, Dictionary<string, object?> fields, bool enforceRequired)
    {
        if (!enforceRequired)
            return fields;

        foreach (var required in resource.Schema.Required)
        {
            if (StandardFields.Contains(required))
                continue;
            if (!fields.TryGetValue(required, out var value) || value is JsonElement { ValueKind: JsonValueKind.Null })
                throw new ResourceValidationException($"missing required field \"{required}\"");
        }
        return fields;
    }

    private static void ValidateValue(string name, SchemaProperty prop, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return; // null clears a field; type is not checked

        var ok = prop.Type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && FitsInteger(prop.Format, value),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            _ => true,
        };
        if (!ok)
            throw new ResourceValidationException(
                $"field \"{name}\" must be of type {prop.Type}{FormatSuffix(prop.Format)}");

        if (prop.Enum is { Count: > 0 } allowed && value.ValueKind == JsonValueKind.String)
        {
            var s = value.GetString();
            if (s is null || !allowed.Contains(s))
                throw new ResourceValidationException(
                    $"field \"{name}\" must be one of: {string.Join(", ", allowed)}");
        }

        // Validate each array element against the declared item schema (type, format, enum).
        if (prop is { Type: "array", Items: { } items } && value.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var element in value.EnumerateArray())
                ValidateValue($"{name}[{i++}]", items, element);
        }
    }

    /// <summary>Integers respect their OpenAPI format: <c>int32</c> must fit Int32; otherwise Int64.</summary>
    private static bool FitsInteger(string? format, JsonElement value) => format switch
    {
        "int32" => value.TryGetInt32(out _),
        _ => value.TryGetInt64(out _),
    };

    private static string FormatSuffix(string? format) =>
        string.IsNullOrEmpty(format) ? "" : $" ({format})";
}
