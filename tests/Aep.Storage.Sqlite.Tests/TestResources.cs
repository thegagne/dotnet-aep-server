using Aep.Storage.Abstractions.Model;

namespace Aep.Storage.Sqlite.Tests;

/// <summary>Shared resource definitions for store tests (a publisher -> book hierarchy).</summary>
internal static class TestResources
{
    public static ResourceDefinition Book { get; } = new()
    {
        Singular = "book",
        Plural = "books",
        Parents = ["publisher"],
        Schema = new ResourceSchema
        {
            Properties = new Dictionary<string, SchemaProperty>
            {
                ["title"] = new() { Type = "string" },
                ["author"] = new() { Type = "string" },
                ["price"] = new() { Type = "integer" },
                ["published"] = new() { Type = "boolean" },
                ["tags"] = new() { Type = "array", Items = new SchemaProperty { Type = "string" } },
            },
        },
    };

    public static StoredResource NewBook(
        string id, string author, long price, bool published = false, string? title = null) => new()
    {
        Id = id,
        Path = $"publishers/p1/books/{id}",
        CreateTime = "2024-01-01T00:00:00Z",
        UpdateTime = "2024-01-01T00:00:00Z",
        Fields = new Dictionary<string, object?>
        {
            ["title"] = title ?? $"Book {id}",
            ["author"] = author,
            ["price"] = price,
            ["published"] = published,
        },
    };

    public static readonly IReadOnlyDictionary<string, string> P1 =
        new Dictionary<string, string> { ["publisher_id"] = "p1" };
}
