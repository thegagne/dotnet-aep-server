using Aep.Storage.Abstractions.Model;

namespace Aep.Storage.Sqlite;

/// <summary>
/// Helpers for mapping the AEP resource model onto a per-resource SQLite table.
/// Mirrors aepbase's <c>pkg/db/db.go</c>: one table per resource (named after the
/// sanitized plural), the four standard columns, one <c>{parent}_id</c> column +
/// index per parent, and one column per user-defined property.
/// </summary>
internal static class SqliteSchema
{
    /// <summary>Standard, server-managed fields stored as fixed columns (not user properties).</summary>
    internal static readonly HashSet<string> StandardFields =
        new(StringComparer.Ordinal) { "id", "uid", "path", "create_time", "update_time" };

    /// <summary>SQLite table name for a resource (hyphens are not valid bare identifiers).</summary>
    internal static string TableName(ResourceDefinition r) => Sanitize(r.Plural);

    internal static string Sanitize(string name) => name.Replace('-', '_');

    /// <summary>Quotes a SQL identifier and rejects embedded quotes.</summary>
    internal static string Quote(string identifier)
    {
        if (identifier.Contains('"'))
            throw new ArgumentException($"invalid identifier: {identifier}");
        return $"\"{identifier}\"";
    }

    internal static string SqliteType(SchemaProperty prop) => prop.Type switch
    {
        "integer" => "INTEGER",
        "number" => "REAL",
        "boolean" => "INTEGER",
        // string, object, array and anything else are stored as text (objects/arrays as JSON).
        _ => "TEXT",
    };

    /// <summary>User-defined property names in declaration-independent (sorted) order.</summary>
    internal static IReadOnlyList<string> UserPropertyNames(ResourceDefinition r) =>
        r.Schema.Properties.Keys
            .Where(name => !StandardFields.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>The <c>CREATE TABLE</c> and index statements for a resource.</summary>
    internal static IEnumerable<string> CreateStatements(ResourceDefinition r)
    {
        var table = TableName(r);
        var cols = new List<string>
        {
            $"{Quote("id")} TEXT PRIMARY KEY",
            // uid is nullable so the column can be added to pre-existing tables (legacy rows: NULL).
            $"{Quote("uid")} TEXT",
            $"{Quote("path")} TEXT NOT NULL UNIQUE",
            $"{Quote("create_time")} TEXT NOT NULL",
            $"{Quote("update_time")} TEXT NOT NULL",
        };

        var parentCols = new List<string>();
        foreach (var parent in r.Parents)
        {
            var col = Sanitize(parent) + "_id";
            cols.Add($"{Quote(col)} TEXT NOT NULL");
            parentCols.Add(col);
        }

        foreach (var name in UserPropertyNames(r))
            cols.Add($"{Quote(name)} {SqliteType(r.Schema.Properties[name])}");

        yield return $"CREATE TABLE IF NOT EXISTS {Quote(table)} (\n  {string.Join(",\n  ", cols)}\n)";

        foreach (var col in parentCols)
            yield return $"CREATE INDEX IF NOT EXISTS {Quote($"idx_{table}_{col}")} ON {Quote(table)}({Quote(col)})";

        foreach (var index in r.Indexes)
        {
            var name = Sanitize($"idx_{table}_{string.Join("_", index.Fields)}");
            var indexCols = string.Join(", ", index.Fields.Select(Quote));
            yield return $"CREATE INDEX IF NOT EXISTS {Quote(name)} ON {Quote(table)}({indexCols})";
        }
    }
}
