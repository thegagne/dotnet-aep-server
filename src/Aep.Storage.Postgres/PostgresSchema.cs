using Aep.Storage.Abstractions.Model;

namespace Aep.Storage.Postgres;

/// <summary>
/// Maps the AEP resource model onto a per-resource PostgreSQL table — the SQLite layout,
/// but using native column types (boolean / bigint / double precision). Objects and arrays
/// are stored as JSON text.
/// </summary>
internal static class PostgresSchema
{
    internal static readonly HashSet<string> StandardFields =
        new(StringComparer.Ordinal) { "id", "uid", "path", "create_time", "update_time" };

    internal static string TableName(ResourceDefinition r) => Sanitize(r.Plural);

    internal static string Sanitize(string name) => name.Replace('-', '_');

    internal static string Quote(string identifier)
    {
        if (identifier.Contains('"'))
            throw new ArgumentException($"invalid identifier: {identifier}");
        return $"\"{identifier}\"";
    }

    internal static string PgType(SchemaProperty prop) => prop.Type switch
    {
        "integer" => "bigint",
        "number" => "double precision",
        "boolean" => "boolean",
        // string, object, array and anything else are text (objects/arrays as JSON).
        _ => "text",
    };

    internal static IReadOnlyList<string> UserPropertyNames(ResourceDefinition r) =>
        r.Schema.Properties.Keys
            .Where(name => !StandardFields.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    internal static IEnumerable<string> CreateStatements(ResourceDefinition r)
    {
        var table = TableName(r);
        var cols = new List<string>
        {
            $"{Quote("id")} text PRIMARY KEY",
            // uid is nullable so the column can be added to pre-existing tables (legacy rows: NULL).
            $"{Quote("uid")} text",
            $"{Quote("path")} text NOT NULL UNIQUE",
            $"{Quote("create_time")} text NOT NULL",
            $"{Quote("update_time")} text NOT NULL",
        };

        var parentCols = new List<string>();
        foreach (var parent in r.Parents)
        {
            var col = Sanitize(parent) + "_id";
            cols.Add($"{Quote(col)} text NOT NULL");
            parentCols.Add(col);
        }

        foreach (var name in UserPropertyNames(r))
            cols.Add($"{Quote(name)} {PgType(r.Schema.Properties[name])}");

        yield return $"CREATE TABLE IF NOT EXISTS {Quote(table)} (\n  {string.Join(",\n  ", cols)}\n)";

        foreach (var col in parentCols)
            yield return $"CREATE INDEX IF NOT EXISTS {Quote($"idx_{table}_{col}")} ON {Quote(table)} ({Quote(col)})";

        foreach (var index in r.Indexes)
        {
            var name = Sanitize($"idx_{table}_{string.Join("_", index.Fields)}");
            var indexCols = string.Join(", ", index.Fields.Select(Quote));
            yield return $"CREATE INDEX IF NOT EXISTS {Quote(name)} ON {Quote(table)} ({indexCols})";
        }
    }
}
