using Microsoft.Extensions.Options;

namespace Aep.Storage.DynamoDb.Tests;

/// <summary>Unit tests for the DynamoDB options (#01). No emulator needed — construction doesn't call AWS.</summary>
public sealed class DynamoDbStorageOptionsTests
{
    [Fact]
    public void Defaults_are_backward_compatible()
    {
        var o = new DynamoDbStorageOptions();
        Assert.Equal(DynamoDbCredentialsSource.Static, o.CredentialsSource);
        Assert.Equal(DynamoDbBillingMode.PayPerRequest, o.BillingMode);
    }

    [Fact]
    public void Store_constructs_under_provisioned_billing_without_touching_aws()
    {
        // ServiceUrl is set (emulator endpoint), Static creds — no network call at construction.
        var store = new DynamoDbResourceStore(Options.Create(new DynamoDbStorageOptions
        {
            ServiceUrl = "http://localhost:4566",
            BillingMode = DynamoDbBillingMode.Provisioned,
            ReadCapacityUnits = 3,
            WriteCapacityUnits = 7,
            RetryMode = "Standard",
            MaxErrorRetry = 5,
        }));

        Assert.NotNull(store);
        store.Dispose();
    }
}
