using System.Text;
using System.Text.Json;
using Aep.Storage.Abstractions.Model;
using Aep.Storage.Abstractions.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aep.Storage.Postgres;

/// <summary>
/// PostgreSQL-backed <see cref="IResourceStore"/> using a pooled <see cref="NpgsqlDataSource"/>.
/// Mirrors the SQLite store (table per resource, keyset pagination, filter -> WHERE) but with
/// native column types. Registered as a singleton.
/// </summary>
public sealed class PostgresResourceStore : IResourceStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresResourceStore(IOptions<PostgresStorageOptions> options)
        => _dataSource = NpgsqlDataSource.Create(options.Value.BuildConnectionString());

    public async Task EnsureSchemaAsync(IEnumerable<ResourceDefinition> resources, CancellationToken ct = default)
    {
        foreach (var r in resources)
        {
            foreach (var stmt in PostgresSchema.CreateStatements(r))
            {
                await using var cmd = _dataSource.CreateCommand(stmt);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            // Migrate pre-uid tables; no-op when the column already exists.
            var table = PostgresSchema.Quote(PostgresSchema.TableName(r));
            await using var alter = _dataSource.CreateCommand(
                $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS {PostgresSchema.Quote("uid")} text");
            await alter.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<StoredResource?> GetAsync(ResourceDefinition resource, string path, CancellationToken ct = default)
    {
        var props = PostgresSchema.UserPropertyNames(resource);
        var sql = $"SELECT {StandardSelect(props)} FROM {Table(resource)} WHERE {PostgresSchema.Quote("path")} = @path";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@path", path);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRow(reader, resource, props) : null;
    }

    public async Task<ListResult> ListAsync(
        ResourceDefinition resource,
        IReadOnlyDictionary<string, string> parentIds,
        ListOptions options,
        CancellationToken ct = default)
    {
        var props = PostgresSchema.UserPropertyNames(resource);
        var where = new List<string>();
        var cmdParams = new List<KeyValuePair<string, object?>>();

        foreach (var (param, value) in parentIds)
        {
            if (value == ResourceDefinition.WildcardCollectionId)
                continue; // AEP-159: list across all values of this parent
            where.Add($"{PostgresSchema.Quote(PostgresSchema.Sanitize(param))} = @p_{param}");
            cmdParams.Add(new($"@p_{param}", value));
        }

        if (!string.IsNullOrEmpty(options.PageToken))
        {
            // PageToken is the raw cursor (last id); the API layer handles opacity.
            where.Add($"{PostgresSchema.Quote("id")} > @after");
            cmdParams.Add(new("@after", options.PageToken));
        }

        if (options.Filter is not null)
        {
            var allowed = new HashSet<string>(props, StringComparer.Ordinal);
            allowed.UnionWith(PostgresSchema.StandardFields);
            var translator = new PostgresFilterTranslator(allowed);
            where.Add(translator.Translate(options.Filter));
            cmdParams.AddRange(translator.Parameters);
        }

        var pageSize = Math.Clamp(options.PageSize, 1, ListOptions.MaxPageSize);
        var skip = Math.Max(0, options.Skip);
        var fetch = skip + pageSize + 1; // one extra row signals there is a next page

        var sql = new StringBuilder($"SELECT {StandardSelect(props)} FROM {Table(resource)}");
        if (where.Count > 0)
            sql.Append(" WHERE ").Append(string.Join(" AND ", where));
        sql.Append($" ORDER BY {PostgresSchema.Quote("id")} LIMIT @limit");

        List<StoredResource> rows = [];
        await using (var cmd = _dataSource.CreateCommand(sql.ToString()))
        {
            foreach (var (name, value) in cmdParams)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@limit", fetch);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add(ReadRow(reader, resource, props));
        }

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
        var props = PostgresSchema.UserPropertyNames(resource);

        var colNames = new List<string> { "id", "uid", "path", "create_time", "update_time" };
        var values = new List<object?> { stored.Id, stored.Uid, stored.Path, stored.CreateTime, stored.UpdateTime };

        foreach (var (param, value) in directParentIds)
        {
            colNames.Add(PostgresSchema.Sanitize(param));
            values.Add(value);
        }
        foreach (var name in props)
        {
            colNames.Add(name);
            values.Add(BindValue(stored.Fields.GetValueOrDefault(name), resource.Schema.Properties[name]));
        }

        var quoted = string.Join(", ", colNames.Select(PostgresSchema.Quote));
        var placeholders = string.Join(", ", colNames.Select((_, i) => $"@v{i}"));
        var sql = $"INSERT INTO {Table(resource)} ({quoted}) VALUES ({placeholders})";

        await using var cmd = _dataSource.CreateCommand(sql);
        for (var i = 0; i < values.Count; i++)
            cmd.Parameters.AddWithValue($"@v{i}", values[i] ?? DBNull.Value);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateResourceException(stored.Path);
        }
    }

    public async Task<bool> UpdateAsync(
        ResourceDefinition resource,
        string path,
        IReadOnlyDictionary<string, object?> fields,
        string updateTime,
        string? expectedUpdateTime = null,
        CancellationToken ct = default)
    {
        var props = new HashSet<string>(PostgresSchema.UserPropertyNames(resource), StringComparer.Ordinal);

        var sets = new List<string> { $"{PostgresSchema.Quote("update_time")} = @ut" };
        var values = new List<object?> { updateTime };
        foreach (var (name, value) in fields)
        {
            if (!props.Contains(name)) continue;
            sets.Add($"{PostgresSchema.Quote(name)} = @s{values.Count}");
            values.Add(BindValue(value, resource.Schema.Properties[name]));
        }

        var sql = $"UPDATE {Table(resource)} SET {string.Join(", ", sets)} WHERE {PostgresSchema.Quote("path")} = @path";
        if (expectedUpdateTime is not null) // atomic optimistic-concurrency guard (AEP-154)
            sql += $" AND {PostgresSchema.Quote("update_time")} = @expected";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@ut", values[0]!);
        for (var i = 1; i < values.Count; i++)
            cmd.Parameters.AddWithValue($"@s{i}", values[i] ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@path", path);
        if (expectedUpdateTime is not null)
            cmd.Parameters.AddWithValue("@expected", expectedUpdateTime);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteAsync(
        ResourceDefinition resource, string path, string? expectedUpdateTime = null, CancellationToken ct = default)
    {
        var sql = $"DELETE FROM {Table(resource)} WHERE {PostgresSchema.Quote("path")} = @path";
        if (expectedUpdateTime is not null)
            sql += $" AND {PostgresSchema.Quote("update_time")} = @expected";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@path", path);
        if (expectedUpdateTime is not null)
            cmd.Parameters.AddWithValue("@expected", expectedUpdateTime);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask DisposeAsync() => await _dataSource.DisposeAsync();

    // ---- helpers ----

    private static string Table(ResourceDefinition r) => PostgresSchema.Quote(PostgresSchema.TableName(r));

    private static string StandardSelect(IReadOnlyList<string> props)
    {
        var cols = new List<string> { "id", "uid", "path", "create_time", "update_time" };
        cols.AddRange(props);
        return string.Join(", ", cols.Select(PostgresSchema.Quote));
    }

    private static StoredResource ReadRow(NpgsqlDataReader reader, ResourceDefinition resource, IReadOnlyList<string> props)
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
                return ToBool(value);
            case "integer":
                return value is JsonElement ie ? ie.GetInt64() : Convert.ToInt64(value);
            case "number":
                return value is JsonElement ne ? ne.GetDouble() : Convert.ToDouble(value);
            default:
                return value is JsonElement el ? (el.GetString() ?? el.GetRawText()) : value;
        }
    }

    private static object? ReadValue(NpgsqlDataReader reader, int ordinal, SchemaProperty prop)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var raw = reader.GetValue(ordinal);

        switch (prop.Type)
        {
            case "boolean":
                return Convert.ToBoolean(raw);
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

}
