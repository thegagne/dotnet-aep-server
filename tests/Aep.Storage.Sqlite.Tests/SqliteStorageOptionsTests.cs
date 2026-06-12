using Microsoft.Data.Sqlite;

namespace Aep.Storage.Sqlite.Tests;

/// <summary>Unit tests for the SQLite options (#01).</summary>
public sealed class SqliteStorageOptionsTests
{
    [Fact]
    public void BuildConnectionString_applies_pooling_when_set()
    {
        var off = new SqliteStorageOptions { ConnectionString = "Data Source=x.db", Pooling = false };
        Assert.False(new SqliteConnectionStringBuilder(off.BuildConnectionString()).Pooling);
    }

    [Fact]
    public void BuildConnectionString_is_unchanged_when_pooling_unset()
    {
        var o = new SqliteStorageOptions { ConnectionString = "Data Source=x.db" };
        Assert.Equal("Data Source=x.db", o.BuildConnectionString());
    }

    [Fact]
    public void Defaults_are_wal_with_a_busy_timeout()
    {
        var o = new SqliteStorageOptions();
        Assert.Equal("WAL", o.JournalMode);
        Assert.Equal(5000, o.BusyTimeoutMs);
    }
}
