using Aep.Storage.DynamoDb;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI helper for the DynamoDB storage backend.</summary>
public static class DynamoDbStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the DynamoDB <see cref="Aep.Storage.Abstractions.Storage.IResourceStore"/>,
    /// binding options from the <c>Storage:DynamoDb</c> configuration section. Set
    /// <c>Storage:DynamoDb:ServiceUrl</c> to target a local emulator (e.g. floci at
    /// <c>http://localhost:4566</c>).
    /// </summary>
    public static IServiceCollection AddAepDynamoDbStore(this IServiceCollection services, IConfiguration configuration)
    {
        new DynamoDbStorageProvider().Register(services, configuration);
        return services;
    }
}
