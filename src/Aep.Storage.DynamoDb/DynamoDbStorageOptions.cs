namespace Aep.Storage.DynamoDb;

/// <summary>How the DynamoDB client obtains AWS credentials.</summary>
public enum DynamoDbCredentialsSource
{
    /// <summary>Use <see cref="DynamoDbStorageOptions.AccessKey"/> / <see cref="DynamoDbStorageOptions.SecretKey"/> (good for emulators).</summary>
    Static,

    /// <summary>Resolve from the ambient AWS chain — env vars, ECS/EC2/Lambda role, shared config. Use this on AWS.</summary>
    Ambient,

    /// <summary>Use a named profile from the shared credentials/config files (<see cref="DynamoDbStorageOptions.Profile"/>).</summary>
    Profile,
}

/// <summary>How table capacity is provisioned.</summary>
public enum DynamoDbBillingMode
{
    /// <summary>On-demand capacity (no throughput to manage).</summary>
    PayPerRequest,

    /// <summary>Provisioned capacity with fixed read/write units.</summary>
    Provisioned,
}

/// <summary>Configuration for the DynamoDB storage provider, bound from <c>Storage:DynamoDb</c>.</summary>
public sealed class DynamoDbStorageOptions
{
    /// <summary>
    /// Override the DynamoDB endpoint — set this to point at a local emulator such as
    /// <see href="https://github.com/hectorvent/floci">floci</see> (<c>http://localhost:4566</c>)
    /// or DynamoDB Local. Leave null to use the real AWS endpoint for <see cref="Region"/>.
    /// </summary>
    public string? ServiceUrl { get; set; }

    public string Region { get; set; } = "us-east-1";

    /// <summary>How to obtain credentials. Use <c>Ambient</c> on AWS (task/instance role).</summary>
    public DynamoDbCredentialsSource CredentialsSource { get; set; } = DynamoDbCredentialsSource.Static;

    /// <summary>Access key for <see cref="DynamoDbCredentialsSource.Static"/>. Any non-empty value works against emulators.</summary>
    public string AccessKey { get; set; } = "local";

    /// <summary>Secret key for <see cref="DynamoDbCredentialsSource.Static"/>.</summary>
    public string SecretKey { get; set; } = "local";

    /// <summary>Named profile for <see cref="DynamoDbCredentialsSource.Profile"/>.</summary>
    public string? Profile { get; set; }

    /// <summary>Optional prefix applied to every table name (e.g. <c>aep_</c>).</summary>
    public string TablePrefix { get; set; } = "";

    /// <summary>Capacity mode for tables this server creates.</summary>
    public DynamoDbBillingMode BillingMode { get; set; } = DynamoDbBillingMode.PayPerRequest;

    /// <summary>Provisioned read capacity units (used only when <see cref="BillingMode"/> is Provisioned).</summary>
    public long ReadCapacityUnits { get; set; } = 5;

    /// <summary>Provisioned write capacity units (used only when <see cref="BillingMode"/> is Provisioned).</summary>
    public long WriteCapacityUnits { get; set; } = 5;

    /// <summary>Maximum SDK retry attempts on throttling/transient errors; null leaves the SDK default.</summary>
    public int? MaxErrorRetry { get; set; }

    /// <summary>Retry strategy: Legacy, Standard, or Adaptive; null leaves the SDK default.</summary>
    public string? RetryMode { get; set; }
}
