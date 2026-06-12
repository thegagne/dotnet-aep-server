using Aep.Storage.Abstractions.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aep.Storage.DynamoDb;

/// <summary>The DynamoDB storage provider. Selected via <c>Storage:Provider=dynamodb</c>.</summary>
public sealed class DynamoDbStorageProvider : IStorageProvider
{
    public string Name => "dynamodb";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DynamoDbStorageOptions>()
            .Bind(configuration.GetSection("Storage:DynamoDb"))
            .Validate(
                o => o.CredentialsSource != DynamoDbCredentialsSource.Profile || !string.IsNullOrEmpty(o.Profile),
                "Storage:DynamoDb:Profile is required when CredentialsSource is Profile.")
            .Validate(
                o => o.BillingMode != DynamoDbBillingMode.Provisioned || (o.ReadCapacityUnits > 0 && o.WriteCapacityUnits > 0),
                "Storage:DynamoDb:Read/WriteCapacityUnits must be > 0 under Provisioned billing.")
            .Validate(o => o.MaxErrorRetry is null or >= 0, "Storage:DynamoDb:MaxErrorRetry must be >= 0.")
            .Validate(
                o => string.IsNullOrEmpty(o.RetryMode) || Enum.TryParse<Amazon.Runtime.RequestRetryMode>(o.RetryMode, ignoreCase: true, out _),
                "Storage:DynamoDb:RetryMode must be Legacy, Standard, or Adaptive.")
            .ValidateOnStart();

        services.TryAddSingleton<IResourceStore, DynamoDbResourceStore>();
    }
}
