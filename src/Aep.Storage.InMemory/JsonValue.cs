using System.Text.Json;

namespace Aep.Storage.InMemory;

/// <summary>Converts request values (often <see cref="JsonElement"/>) into plain CLR
/// objects so they store cleanly, compare correctly in filters, and re-serialize
/// as natural JSON.</summary>
internal static class JsonValue
{
    public static object? ToClr(object? value) => value switch
    {
        JsonElement el => FromElement(el),
        _ => value,
    };

    private static object? FromElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => FromElement(p.Value)),
        JsonValueKind.Array => el.EnumerateArray().Select(FromElement).ToList(),
        _ => null,
    };
}
