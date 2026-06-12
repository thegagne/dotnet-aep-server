using Microsoft.Data.Sqlite;

namespace Aep.Storage.Sqlite;

/// <summary>Configuration for the SQLite storage provider, bound from <c>Storage:Sqlite</c>.</summary>
public sealed class SqliteStorageOptions
{
    /// <summary>ADO.NET connection string. Defaults to a local file.</summary>
    public string ConnectionString { get; set; } = "Data Source=aep.db";

    /// <summary>Journal mode applied via <c>PRAGMA journal_mode</c> (default WAL).</summary>
    public string JournalMode { get; set; } = "WAL";

    /// <summary>Lock-wait timeout in milliseconds, applied via <c>PRAGMA busy_timeout</c>.</summary>
    public int BusyTimeoutMs { get; set; } = 5000;

    /// <summary>Override connection pooling; null leaves the connection string / driver default.</summary>
    public bool? Pooling { get; set; }

    /// <summary>The PRAGMA journal modes SQLite accepts (used both to apply and to validate).</summary>
    internal static readonly string[] JournalModes = ["DELETE", "TRUNCATE", "PERSIST", "MEMORY", "WAL", "OFF"];

    /// <summary>The effective connection string with discrete options layered on top.</summary>
    public string BuildConnectionString()
    {
        if (Pooling is not { } pooling)
            return ConnectionString;
        return new SqliteConnectionStringBuilder(ConnectionString) { Pooling = pooling }.ConnectionString;
    }
}
