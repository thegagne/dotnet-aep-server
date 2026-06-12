using System.Text;
using System.Text.Json;
using Aep.Storage.Abstractions.Model;
using Aep.Storage.Abstractions.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Aep.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IResourceStore"/>. Holds a single shared connection
/// (WAL enabled) guarded by a semaphore, mirroring aepbase's single-connection
/// model. Registered as a singleton.
/// </summary>
public sealed class SqliteResourceStore : IResourceStore, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public SqliteResourceStore(IOptions<SqliteStorageOptions> options)
    {
        var o = options.Value;
        var connectionString = o.BuildConnectionString();
        EnsureDataDirectory(connectionString);
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        // JournalMode is validated against a fixed set at registration, so interpolation is safe.
        Execute($"PRAGMA journal_mode={o.JournalMode}");
        Execute($"PRAGMA busy_timeout={o.BusyTimeoutMs}");
    }

    /// <summary>Creates the directory for a file-backed database if it does not yet exist.</summary>
    private static void EnsureDataDirectory(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrEmpty(dataSource) ||
            dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
            dataSource.StartsWith("file::memory:", StringComparison.OrdinalIgnoreCase))
            return;

        var dir = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public async Task EnsureSchemaAsync(IEnumerable<ResourceDefinition> resources, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            foreach (var r in resources)
            {
                foreach (var stmt in SqliteSchema.CreateStatements(r))
                    await ExecuteAsync(stmt, ct);
                await EnsureColumnAsync(SqliteSchema.TableName(r), "uid", "TEXT", ct); // migrate pre-uid tables
            }
            _initialized = true;
        }
        finally { _gate.Release(); }
    }

    public async Task<StoredResource?> GetAsync(ResourceDefinition resource, string path, CancellationToken ct = default)
    {
        EnsureInitialized();
        var props = SqliteSchema.UserPropertyNames(resource);
        var cols = StandardSelect(props);
        var sql = $"SELECT {cols} FROM {Table(resource)} WHERE {SqliteSchema.Quote("path")} = @path";

        await _gate.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@path", path);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? ReadRow(reader, resource, props) : null;
        }
        finally { _gate.Release(); }
    }

    public async Task<ListResult> ListAsync(
        ResourceDefinition resource,
        IReadOnlyDictionary<string, string> parentIds,
        ListOptions options,
        CancellationToken ct = default)
    {
        EnsureInitialized();
        var props = SqliteSchema.UserPropertyNames(resource);
        var where = new List<string>();
        var cmdParams = new List<KeyValuePair<string, object?>>();

        foreach (var (param, value) in parentIds)
        {
            if (value == ResourceDefinition.WildcardCollectionId)
                continue; // AEP-159: list across all values of this parent
            where.Add($"{SqliteSchema.Quote(SqliteSchema.Sanitize(param))} = @p_{param}");
            cmdParams.Add(new($"@p_{param}", value));
        }

        if (!string.IsNullOrEmpty(options.PageToken))
        {
            // PageToken is the raw cursor (last id); the API layer handles opacity.
            where.Add($"{SqliteSchema.Quote("id")} > @after");
            cmdParams.Add(new("@after", options.PageToken));
        }

        if (options.Filter is not null)
        {
            var allowed = new HashSet<string>(props, StringComparer.Ordinal);
            allowed.UnionWith(SqliteSchema.StandardFields);
            var translator = new SqliteFilterTranslator(allowed);
            where.Add(translator.Translate(options.Filter));
            cmdParams.AddRange(translator.Parameters);
        }

        var pageSize = Math.Clamp(options.PageSize, 1, ListOptions.MaxPageSize);
        var skip = Math.Max(0, options.Skip);
        var fetch = skip + pageSize + 1; // one extra row signals there is a next page

        var sql = new StringBuilder($"SELECT {StandardSelect(props)} FROM {Table(resource)}");
        if (where.Count > 0)
            sql.Append(" WHERE ").Append(string.Join(" AND ", where));
        sql.Append($" ORDER BY {SqliteSchema.Quote("id")} LIMIT @limit");

        await _gate.WaitAsync(ct);
        List<StoredResource> rows = [];
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql.ToString();
            foreach (var (name, value) in cmdParams)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@limit", fetch);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add(ReadRow(reader, resource, props));
        }
        finally { _gate.Release(); }

        // Apply skip in-process (mirrors aepbase): drop the first N rows.
        if (skip > 0)
            rows = skip >= rows.Count ? [] : rows.GetRange(skip, rows.Count - skip);

        string? nextToken = null;
        if (rows.Count > pageSize)
        {
            nextToken = rows[pageSize - 1].Id;
            rows = rows.GetRange(0, pageSize);
        }

        return new ListResult { Items = rows, NextPageToken = nextToken };
    }

    public async Task InsertAsync(
        ResourceDefinition resource,
        StoredResource stored,
        IReadOnlyDictionary<string, string> directParentIds,
        CancellationToken ct = default)
    {
        EnsureInitialized();
        var props = SqliteSchema.UserPropertyNames(resource);

        var colNames = new List<string> { "id", "uid", "path", "create_time", "update_time" };
        var values = new List<object?> { stored.Id, stored.Uid, stored.Path, stored.CreateTime, stored.UpdateTime };

        foreach (var (param, value) in directParentIds)
        {
            colNames.Add(SqliteSchema.Sanitize(param));
            values.Add(value);
        }
        foreach (var name in props)
        {
            colNames.Add(name);
            values.Add(BindValue(stored.Fields.GetValueOrDefault(name), resource.Schema.Properties[name]));
        }

        var quoted = string.Join(", ", colNames.Select(SqliteSchema.Quote));
        var placeholders = string.Join(", ", colNames.Select((_, i) => $"@v{i}"));
        var sql = $"INSERT INTO {Table(resource)} ({quoted}) VALUES ({placeholders})";

        await _gate.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            for (var i = 0; i < values.Count; i++)
                cmd.Parameters.AddWithValue($"@v{i}", values[i] ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
        {
            throw new DuplicateResourceException(stored.Path);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> UpdateAsync(
        ResourceDefinition resource,
        string path,
        IReadOnlyDictionary<string, object?> fields,
        string updateTime,
        string? expectedUpdateTime = null,
        CancellationToken ct = default)
    {
        EnsureInitialized();
        var props = new HashSet<string>(SqliteSchema.UserPropertyNames(resource), StringComparer.Ordinal);

        var sets = new List<string> { $"{SqliteSchema.Quote("update_time")} = @ut" };
        var values = new List<object?> { updateTime };
        foreach (var (name, value) in fields)
        {
            if (!props.Contains(name)) continue;
            sets.Add($"{SqliteSchema.Quote(name)} = @s{values.Count}");
            values.Add(BindValue(value, resource.Schema.Properties[name]));
        }

        var sql = $"UPDATE {Table(resource)} SET {string.Join(", ", sets)} WHERE {SqliteSchema.Quote("path")} = @path";
        if (expectedUpdateTime is not null) // atomic optimistic-concurrency guard (AEP-154)
            sql += $" AND {SqliteSchema.Quote("update_time")} = @expected";

        await _gate.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@ut", values[0]!);
            for (var i = 1; i < values.Count; i++)
                cmd.Parameters.AddWithValue($"@s{i}", values[i] ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@path", path);
            if (expectedUpdateTime is not null)
                cmd.Parameters.AddWithValue("@expected", expectedUpdateTime);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> DeleteAsync(
        ResourceDefinition resource, string path, string? expectedUpdateTime = null, CancellationToken ct = default)
    {
        EnsureInitialized();
        var sql = $"DELETE FROM {Table(resource)} WHERE {SqliteSchema.Quote("path")} = @path";
        if (expectedUpdateTime is not null)
            sql += $" AND {SqliteSchema.Quote("update_time")} = @expected";

        await _gate.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@path", path);
            if (expectedUpdateTime is not null)
                cmd.Parameters.AddWithValue("@expected", expectedUpdateTime);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        _gate.Dispose();
    }

    // ---- helpers ----

    private static string Table(ResourceDefinition r) => SqliteSchema.Quote(SqliteSchema.TableName(r));

    private static string StandardSelect(IReadOnlyList<string> props)
    {
        var cols = new List<string> { "id", "uid", "path", "create_time", "update_time" };
        cols.AddRange(props);
        return string.Join(", ", cols.Select(SqliteSchema.Quote));
    }

    private static StoredResource ReadRow(SqliteDataReader reader, ResourceDefinition resource, IReadOnlyList<string> props)
    {
        var stored = new StoredResource
        {
            Id = reader.GetString(0),
            Uid = reader.IsDBNull(1) ? null : reader.GetString(1),
            Path = reader.GetString(2),
            CreateTime = reader.GetString(3),
            UpdateTime = reader.GetString(4),
        };
        for (var i = 0; i < props.Count; i++)
            stored.Fields[props[i]] = ReadValue(reader, 5 + i, resource.Schema.Properties[props[i]]);
        return stored;
    }

    /// <summary>Adds a column to an existing table if it isn't already present (idempotent migration).</summary>
    private async Task EnsureColumnAsync(string table, string column, string type, CancellationToken ct)
    {
        await using (var check = _connection.CreateCommand())
        {
            check.CommandText = $"SELECT 1 FROM pragma_table_info(@t) WHERE name = @c";
            check.Parameters.AddWithValue("@t", table);
            check.Parameters.AddWithValue("@c", column);
            if (await check.ExecuteScalarAsync(ct) is not null)
                return;
        }
        await ExecuteAsync(
            $"ALTER TABLE {SqliteSchema.Quote(table)} ADD COLUMN {SqliteSchema.Quote(column)} {type}", ct);
    }

    /// <summary>Converts a CLR/JSON field value into the form bound to a SQLite parameter.</summary>
    private static object? BindValue(object? value, SchemaProperty prop)
    {
        if (value is null) return null;
        if (value is JsonElement { ValueKind: JsonValueKind.Null }) return null;

        switch (prop.Type)
        {
            case "object" or "array":
                return value switch
                {
                    string s => s,
                    JsonElement je => je.GetRawText(),
                    _ => JsonSerializer.Serialize(value),
                };
            case "boolean":
                return ToBool(value) ? 1L : 0L;
            default:
                return value is JsonElement el ? JsonElementToClr(el) : value;
        }
    }

    /// <summary>Coerces a stored SQLite value back to its schema-typed CLR representation.</summary>
    private static object? ReadValue(SqliteDataReader reader, int ordinal, SchemaProperty prop)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var raw = reader.GetValue(ordinal);

        switch (prop.Type)
        {
            case "boolean":
                return Convert.ToInt64(raw) != 0;
            case "integer":
                return Convert.ToInt64(raw);
            case "number":
                return Convert.ToDouble(raw);
            case "object" or "array":
                var json = raw as string;
                if (string.IsNullOrEmpty(json)) return null;
                using (var doc = JsonDocument.Parse(json))
                    return doc.RootElement.Clone();
            default:
                return raw as string ?? raw.ToString();
        }
    }

    private static bool ToBool(object value) => value switch
    {
        bool b => b,
        long l => l != 0,
        int i => i != 0,
        JsonElement je => je.ValueKind == JsonValueKind.True,
        _ => Convert.ToBoolean(value),
    };

    private static object? JsonElementToClr(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.GetRawText(),
    };

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("EnsureSchemaAsync must be called before using the store.");
    }
}
