using System.Globalization;
using System.Text.Json;
using Amazon.DynamoDBv2.Model;
using Aep.Storage.Abstractions.Model;

namespace Aep.Storage.DynamoDb;

/// <summary>
/// Maps the AEP resource model onto DynamoDB. One table per resource, keyed by the AEP
/// resource <c>path</c>. A <c>by_parent</c> GSI (partition = parent scope, sort = id) backs
/// scoped, id-ordered List. User fields are stored as native scalar attributes; objects and
/// arrays as JSON strings.
/// </summary>
internal static class DynamoDbSchema
{
    internal const string PathAttr = "path";
    internal const string IdAttr = "id";
    internal const string UidAttr = "uid";
    internal const string ParentAttr = "_parent";
    internal const string CreateTimeAttr = "create_time";
    internal const string UpdateTimeAttr = "update_time";
    internal const string ParentIndex = "by_parent";

    internal static readonly HashSet<string> StandardFields =
        new(StringComparer.Ordinal) { "id", "uid", "path", "create_time", "update_time" };

    internal static string TableName(string prefix, ResourceDefinition r) => prefix + r.Plural.Replace('-', '_');

    internal static IReadOnlyList<string> UserPropertyNames(ResourceDefinition r) =>
        r.Schema.Properties.Keys
            .Where(name => !StandardFields.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>The GSI partition value for a resource scoped by its direct parent ids.</summary>
    internal static string ParentScope(IReadOnlyDictionary<string, string> directParentIds)
    {
        if (directParentIds.Count == 0)
            return "_root";
        return string.Join('/', directParentIds.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value));
    }

    /// <summary>The parent scope inferred from an existing resource path (used on update).</summary>
    internal static string ScopeFromPath(ResourceDefinition resource, string path)
    {
        if (resource.Parents.Count == 0)
            return "_root";
        var segments = path.Split('/');
        // pattern is [..., {parent_id}, plural, {own_id}] -> direct parent id is third from the end.
        return segments.Length >= 3 ? segments[^3] : "_root";
    }

    // ---- per-field secondary indexes (GSIs) ----

    /// <summary>Fields with a single-field declared index (those get a GSI for equality lookups).</summary>
    internal static IReadOnlyList<string> SingleFieldIndexes(ResourceDefinition r) =>
        r.Indexes.Where(i => i.Fields.Count == 1).Select(i => i.Fields[0]).Distinct(StringComparer.Ordinal).ToList();

    internal static string IndexAttr(string field) => "idx_" + field;
    internal static string IndexName(string field) => "gsi_" + field;

    /// <summary>The GSI partition value: parent scope + the field's value (so lookups stay parent-scoped).</summary>
    internal static string IndexKey(string scope, string keyPart) => $"{scope}\u001f{keyPart}";

    /// <summary>The canonical string form of a value for an index key (matches how it is stored/queried).</summary>
    internal static string? KeyPart(object? value, SchemaProperty prop)
    {
        var av = ToAttribute(value, prop);
        if (av is null) return null;
        if (av.S is not null) return av.S;
        if (av.N is not null) return av.N;
        if (av.IsBOOLSet) return (av.BOOL ?? false) ? "true" : "false";
        return null;
    }

    /// <summary>Builds the full DynamoDB item for a new resource, including any index-key attributes.</summary>
    internal static Dictionary<string, AttributeValue> ToItem(
        ResourceDefinition resource, StoredResource stored, IReadOnlyDictionary<string, string> directParentIds)
    {
        var scope = ParentScope(directParentIds);
        var item = new Dictionary<string, AttributeValue>(StringComparer.Ordinal)
        {
            [PathAttr] = new() { S = stored.Path },
            [IdAttr] = new() { S = stored.Id },
            [ParentAttr] = new() { S = scope },
            [CreateTimeAttr] = new() { S = stored.CreateTime },
            [UpdateTimeAttr] = new() { S = stored.UpdateTime },
        };
        if (stored.Uid is not null)
            item[UidAttr] = new AttributeValue { S = stored.Uid };
        foreach (var name in UserPropertyNames(resource))
        {
            var av = ToAttribute(stored.Fields.GetValueOrDefault(name), resource.Schema.Properties[name]);
            if (av is not null)
                item[name] = av;
        }
        foreach (var field in SingleFieldIndexes(resource))
        {
            var keyPart = KeyPart(stored.Fields.GetValueOrDefault(field), resource.Schema.Properties[field]);
            if (keyPart is not null)
                item[IndexAttr(field)] = new AttributeValue { S = IndexKey(scope, keyPart) };
        }
        return item;
    }

    internal static StoredResource FromItem(ResourceDefinition resource, Dictionary<string, AttributeValue> item)
    {
        var stored = new StoredResource
        {
            Id = item[IdAttr].S,
            Uid = item.TryGetValue(UidAttr, out var uid) ? uid.S : null,
            Path = item[PathAttr].S,
            CreateTime = item[CreateTimeAttr].S,
            UpdateTime = item[UpdateTimeAttr].S,
        };
        foreach (var name in UserPropertyNames(resource))
            if (item.TryGetValue(name, out var av))
                stored.Fields[name] = FromAttribute(av, resource.Schema.Properties[name]);
        return stored;
    }

    /// <summary>Converts a field value into a DynamoDB attribute, or null when it should be omitted.</summary>
    internal static AttributeValue? ToAttribute(object? value, SchemaProperty prop)
    {
        if (value is null || value is JsonElement { ValueKind: JsonValueKind.Null })
            return null;

        switch (prop.Type)
        {
            case "object" or "array":
                var json = value switch
                {
                    string s => s,
                    JsonElement je => je.GetRawText(),
                    _ => JsonSerializer.Serialize(value),
                };
                return new AttributeValue { S = json };
            case "boolean":
                return new AttributeValue { BOOL = ToBool(value) };
            case "integer":
                return new AttributeValue { N = ToLong(value).ToString(CultureInfo.InvariantCulture) };
            case "number":
                return new AttributeValue { N = ToDouble(value).ToString("R", CultureInfo.InvariantCulture) };
            default:
                return new AttributeValue { S = ToStringValue(value) };
        }
    }

    private static object? FromAttribute(AttributeValue av, SchemaProperty prop)
    {
        switch (prop.Type)
        {
            case "boolean":
                return av.BOOL ?? false;
            case "integer":
                return long.Parse(av.N, CultureInfo.InvariantCulture);
            case "number":
                return double.Parse(av.N, CultureInfo.InvariantCulture);
            case "object" or "array":
                if (string.IsNullOrEmpty(av.S)) return null;
                using (var doc = JsonDocument.Parse(av.S))
                    return doc.RootElement.Clone();
            default:
                return av.S;
        }
    }

    private static bool ToBool(object value) => value switch
    {
        bool b => b,
        JsonElement je => je.ValueKind == JsonValueKind.True,
        _ => Convert.ToBoolean(value),
    };

    private static long ToLong(object value) => value switch
    {
        JsonElement je => je.GetInt64(),
        _ => Convert.ToInt64(value),
    };

    private static double ToDouble(object value) => value switch
    {
        JsonElement je => je.GetDouble(),
        _ => Convert.ToDouble(value),
    };

    private static string ToStringValue(object value) => value switch
    {
        string s => s,
        JsonElement je => je.GetString() ?? je.GetRawText(),
        _ => value.ToString() ?? "",
    };
}
