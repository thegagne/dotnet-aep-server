using Npgsql;

namespace Aep.Storage.Postgres.Tests;

/// <summary>Unit tests for the Postgres options → connection-string layering (#01). No database needed.</summary>
public sealed class PostgresStorageOptionsTests
{
    [Fact]
    public void BuildConnectionString_layers_discrete_options_over_the_base()
    {
        var o = new PostgresStorageOptions
        {
            ConnectionString = "Host=db;Database=aep;Username=u;Password=p",
            MaxPoolSize = 20,
            MinPoolSize = 2,
            CommandTimeoutSeconds = 45,
            SslMode = "Require",
            SearchPath = "tenant1",
            ApplicationName = "aep-test",
        };

        var b = new NpgsqlConnectionStringBuilder(o.BuildConnectionString());
        Assert.Equal("db", b.Host); // base string preserved
        Assert.Equal(20, b.MaxPoolSize);
        Assert.Equal(2, b.MinPoolSize);
        Assert.Equal(45, b.CommandTimeout);
        Assert.Equal(SslMode.Require, b.SslMode);
        Assert.Equal("tenant1", b.SearchPath);
        Assert.Equal("aep-test", b.ApplicationName);
    }

    [Fact]
    public void IsSslModeValid_accepts_known_modes_and_blank_rejects_garbage()
    {
        Assert.True(new PostgresStorageOptions { SslMode = "VerifyFull" }.IsSslModeValid);
        Assert.True(new PostgresStorageOptions { SslMode = null }.IsSslModeValid);
        Assert.False(new PostgresStorageOptions { SslMode = "Nope" }.IsSslModeValid);
    }
}
