using Aep.Storage.Abstractions.Model;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Aep.Storage.Sqlite.Tests;

public sealed class SqliteIndexTests
{
    [Fact]
    public async Task Declared_index_is_created()
    {
        var file = Path.Combine(Path.GetTempPath(), $"aep-idx-{Guid.NewGuid():N}.db");
        try
        {
            var resource = new ResourceDefinition
            {
                Singular = "book",
                Plural = "books",
                Schema = new ResourceSchema
                {
                    Properties = new Dictionary<string, SchemaProperty>
                    {
                        ["author"] = new() { Type = "string" },
                        ["price"] = new() { Type = "integer" },
                    },
                },
                Indexes = [new ResourceIndex { Fields = ["author", "price"] }],
            };

            var store = new SqliteResourceStore(Options.Create(new SqliteStorageOptions { ConnectionString = $"Data Source={file}" }));
            await store.EnsureSchemaAsync([resource]);
            await store.DisposeAsync();

            await using var conn = new SqliteConnection($"Data Source={file}");
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'books'";
            var names = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));

            Assert.Contains("idx_books_author_price", names);
        }
        finally
        {
            foreach (var f in new[] { file, file + "-wal", file + "-shm" })
                if (File.Exists(f)) File.Delete(f);
        }
    }
}
