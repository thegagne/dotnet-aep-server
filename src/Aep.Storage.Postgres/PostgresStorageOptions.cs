using Npgsql;

namespace Aep.Storage.Postgres;

/// <summary>Configuration for the PostgreSQL storage provider, bound from <c>Storage:Postgres</c>.</summary>
public sealed class PostgresStorageOptions
{
    /// <summary>Base Npgsql connection string. Discrete options below are layered on top of it.</summary>
    public string ConnectionString { get; set; } = "Host=localhost;Database=aep;Username=postgres;Password=postgres";

    /// <summary>Maximum connections in the pool (Npgsql <c>Maximum Pool Size</c>).</summary>
    public int? MaxPoolSize { get; set; }

    /// <summary>Minimum connections kept in the pool (Npgsql <c>Minimum Pool Size</c>).</summary>
    public int? MinPoolSize { get; set; }

    /// <summary>Per-command timeout in seconds (Npgsql <c>Command Timeout</c>).</summary>
    public int? CommandTimeoutSeconds { get; set; }

    /// <summary>TLS mode: Disable, Allow, Prefer, Require, VerifyCA, or VerifyFull.</summary>
    public string? SslMode { get; set; }

    /// <summary>Schema search path (Npgsql <c>Search Path</c>).</summary>
    public string? SearchPath { get; set; }

    /// <summary>Application name reported to the server (Npgsql <c>Application Name</c>).</summary>
    public string? ApplicationName { get; set; }

    /// <summary>True if <see cref="SslMode"/> is unset or names a valid <see cref="Npgsql.SslMode"/>.</summary>
    public bool IsSslModeValid => string.IsNullOrEmpty(SslMode) || Enum.TryParse<SslMode>(SslMode, ignoreCase: true, out _);

    /// <summary>The effective connection string with the discrete options applied over the base string.</summary>
    public string BuildConnectionString()
    {
        var b = new NpgsqlConnectionStringBuilder(ConnectionString);
        if (MaxPoolSize is { } max) b.MaxPoolSize = max;
        if (MinPoolSize is { } min) b.MinPoolSize = min;
        if (CommandTimeoutSeconds is { } timeout) b.CommandTimeout = timeout;
        if (!string.IsNullOrEmpty(SslMode)) b.SslMode = Enum.Parse<SslMode>(SslMode, ignoreCase: true);
        if (!string.IsNullOrEmpty(SearchPath)) b.SearchPath = SearchPath;
        if (!string.IsNullOrEmpty(ApplicationName)) b.ApplicationName = ApplicationName;
        return b.ConnectionString;
    }
}
