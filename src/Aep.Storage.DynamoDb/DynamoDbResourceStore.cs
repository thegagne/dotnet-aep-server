using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Aep.Storage.Abstractions.Filtering;
using Aep.Storage.Abstractions.Model;
using Aep.Storage.Abstractions.Storage;
using Microsoft.Extensions.Options;

namespace Aep.Storage.DynamoDb;

/// <summary>
/// DynamoDB-backed <see cref="IResourceStore"/>. One table per resource keyed by the AEP
/// <c>path</c>; a <c>by_parent</c> GSI gives scoped, id-ordered List with keyset pagination.
/// Filtering is evaluated in-process (<see cref="FilterEvaluator"/>) over a parent partition,
/// mirroring the in-memory store. Registered as a singleton.
/// </summary>
public sealed class DynamoDbResourceStore : IResourceStore, IDisposable
{
    private static readonly string[] StandardFields = ["id", "path", "create_time", "update_time"];

    private readonly IAmazonDynamoDB _client;
    private readonly string _prefix;
    private readonly BillingMode _billingMode;
    private readonly ProvisionedThroughput? _throughput;

    public DynamoDbResourceStore(IOptions<DynamoDbStorageOptions> options)
    {
        var o = options.Value;
        _prefix = o.TablePrefix;
        _billingMode = o.BillingMode == DynamoDbBillingMode.Provisioned ? BillingMode.PROVISIONED : BillingMode.PAY_PER_REQUEST;
        _throughput = o.BillingMode == DynamoDbBillingMode.Provisioned
            ? new ProvisionedThroughput(o.ReadCapacityUnits, o.WriteCapacityUnits)
            : null;

        var config = new AmazonDynamoDBConfig();
        if (!string.IsNullOrEmpty(o.ServiceUrl))
            config.ServiceURL = o.ServiceUrl;
        else
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(o.Region);
        if (o.MaxErrorRetry is { } retries)
            config.MaxErrorRetry = retries;
        if (!string.IsNullOrEmpty(o.RetryMode))
            config.RetryMode = Enum.Parse<RequestRetryMode>(o.RetryMode, ignoreCase: true);

        _client = ResolveCredentials(o) is { } creds
            ? new AmazonDynamoDBClient(creds, config)
            : new AmazonDynamoDBClient(config); // Ambient: let the SDK's default chain resolve
    }

    private static AWSCredentials? ResolveCredentials(DynamoDbStorageOptions o) => o.CredentialsSource switch
    {
        DynamoDbCredentialsSource.Ambient => null,
        DynamoDbCredentialsSource.Profile =>
            new CredentialProfileStoreChain().TryGetAWSCredentials(o.Profile, out var c) ? c
                : throw new InvalidOperationException($"AWS profile '{o.Profile}' was not found."),
        _ => new BasicAWSCredentials(o.AccessKey, o.SecretKey),
    };

    public async Task EnsureSchemaAsync(IEnumerable<ResourceDefinition> resources, CancellationToken ct = default)
    {
        foreach (var r in resources)
        {
            var table = DynamoDbSchema.TableName(_prefix, r);
            var indexFields = DynamoDbSchema.SingleFieldIndexes(r);

            DescribeTableResponse? described = null;
            try { described = await _client.DescribeTableAsync(table, ct); }
            catch (ResourceNotFoundException) { /* create below */ }

            if (described is null)
            {
                var attributes = new List<AttributeDefinition>
                {
                    new() { AttributeName = DynamoDbSchema.PathAttr, AttributeType = ScalarAttributeType.S },
                    new() { AttributeName = DynamoDbSchema.ParentAttr, AttributeType = ScalarAttributeType.S },
                    new() { AttributeName = DynamoDbSchema.IdAttr, AttributeType = ScalarAttributeType.S },
                };
                var gsis = new List<GlobalSecondaryIndex> { Gsi(DynamoDbSchema.ParentIndex, DynamoDbSchema.ParentAttr) };
                foreach (var field in indexFields)
                {
                    attributes.Add(new() { AttributeName = DynamoDbSchema.IndexAttr(field), AttributeType = ScalarAttributeType.S });
                    gsis.Add(Gsi(DynamoDbSchema.IndexName(field), DynamoDbSchema.IndexAttr(field)));
                }

                try
                {
                    await _client.CreateTableAsync(new CreateTableRequest
                    {
                        TableName = table,
                        BillingMode = _billingMode,
                        ProvisionedThroughput = _throughput,
                        AttributeDefinitions = attributes,
                        KeySchema = [new() { AttributeName = DynamoDbSchema.PathAttr, KeyType = KeyType.HASH }],
                        GlobalSecondaryIndexes = gsis,
                    }, ct);
                }
                catch (ResourceInUseException) { /* created concurrently */ }

                await WaitUntilActiveAsync(table, ct);
            }
            else
            {
                // Add any index GSIs that the existing table is missing (one UpdateTable each).
                var existing = described.Table.GlobalSecondaryIndexes.Select(g => g.IndexName).ToHashSet(StringComparer.Ordinal);
                foreach (var field in indexFields.Where(f => !existing.Contains(DynamoDbSchema.IndexName(f))))
                {
                    await _client.UpdateTableAsync(new UpdateTableRequest
                    {
                        TableName = table,
                        AttributeDefinitions =
                        [
                            new() { AttributeName = DynamoDbSchema.IndexAttr(field), AttributeType = ScalarAttributeType.S },
                            new() { AttributeName = DynamoDbSchema.IdAttr, AttributeType = ScalarAttributeType.S },
                        ],
                        GlobalSecondaryIndexUpdates =
                        [
                            new() { Create = ToCreateGsi(DynamoDbSchema.IndexName(field), DynamoDbSchema.IndexAttr(field)) },
                        ],
                    }, ct);
                    await WaitUntilActiveAsync(table, ct);
                }
            }
        }
    }

    private GlobalSecondaryIndex Gsi(string indexName, string partitionKey) => new()
    {
        IndexName = indexName,
        KeySchema =
        [
            new() { AttributeName = partitionKey, KeyType = KeyType.HASH },
            new() { AttributeName = DynamoDbSchema.IdAttr, KeyType = KeyType.RANGE },
        ],
        Projection = new Projection { ProjectionType = ProjectionType.ALL },
        ProvisionedThroughput = _throughput, // null under on-demand billing
    };

    private CreateGlobalSecondaryIndexAction ToCreateGsi(string indexName, string partitionKey) => new()
    {
        IndexName = indexName,
        KeySchema =
        [
            new() { AttributeName = partitionKey, KeyType = KeyType.HASH },
            new() { AttributeName = DynamoDbSchema.IdAttr, KeyType = KeyType.RANGE },
        ],
        Projection = new Projection { ProjectionType = ProjectionType.ALL },
        ProvisionedThroughput = _throughput,
    };

    public async Task<StoredResource?> GetAsync(ResourceDefinition resource, string path, CancellationToken ct = default)
    {
        var response = await _client.GetItemAsync(new GetItemRequest
        {
            TableName = DynamoDbSchema.TableName(_prefix, resource),
            Key = new() { [DynamoDbSchema.PathAttr] = new AttributeValue { S = path } },
        }, ct);

        return response.Item is { Count: > 0 } ? DynamoDbSchema.FromItem(resource, response.Item) : null;
    }

    public async Task<ListResult> ListAsync(
        ResourceDefinition resource,
        IReadOnlyDictionary<string, string> parentIds,
        ListOptions options,
        CancellationToken ct = default)
    {
        // AEP-159: a "-" parent can't form the GSI partition key, so we scan across the table.
        if (parentIds.Values.Any(v => v == ResourceDefinition.WildcardCollectionId))
            return await ListAcrossCollectionsAsync(resource, options, ct);

        var scope = DynamoDbSchema.ParentScope(parentIds);
        var singleIndexes = DynamoDbSchema.SingleFieldIndexes(resource).ToHashSet(StringComparer.Ordinal);

        // If the filter leads with an equality on an indexed field, query that GSI (true index);
        // otherwise scan the parent partition. The remaining predicates become a FilterExpression.
        var (indexField, indexValue, residual) = PlanIndex(options.Filter, singleIndexes);

        string indexName;
        var names = new Dictionary<string, string> { ["#pp"] = "_pk" };
        var values = new Dictionary<string, AttributeValue>();
        if (indexField is not null)
        {
            indexName = DynamoDbSchema.IndexName(indexField);
            names["#pp"] = DynamoDbSchema.IndexAttr(indexField);
            var keyPart = DynamoDbSchema.KeyPart(indexValue, resource.Schema.Properties[indexField])!;
            values[":pp"] = new AttributeValue { S = DynamoDbSchema.IndexKey(scope, keyPart) };
        }
        else
        {
            indexName = DynamoDbSchema.ParentIndex;
            names["#pp"] = DynamoDbSchema.ParentAttr;
            values[":pp"] = new AttributeValue { S = scope };
        }

        var keyCondition = "#pp = :pp";

        // PageToken is the raw cursor (last id); the API layer handles opacity.
        if (!string.IsNullOrEmpty(options.PageToken))
        {
            names["#id"] = DynamoDbSchema.IdAttr;
            values[":after"] = new AttributeValue { S = options.PageToken };
            keyCondition += " AND #id > :after";
        }

        // Remaining predicates run as a server-side FilterExpression (validates field names too).
        string? filterExpression = null;
        if (residual is not null)
        {
            var translator = new DynamoDbFilterTranslator(resource, AllowedFields(resource));
            filterExpression = translator.Translate(residual);
            foreach (var (k, v) in translator.Names) names[k] = v;
            foreach (var (k, v) in translator.Values) values[k] = v;
        }

        var pageSize = Math.Clamp(options.PageSize, 1, ListOptions.MaxPageSize);
        var skip = Math.Max(0, options.Skip);

        // Query the parent partition (ascending by id), filtering server-side; follow pagination
        // only until the page is filled (Dynamo applies Limit before the filter, so we accumulate).
        var matched = new List<StoredResource>();
        Dictionary<string, AttributeValue>? startKey = null;
        do
        {
            var response = await _client.QueryAsync(new QueryRequest
            {
                TableName = DynamoDbSchema.TableName(_prefix, resource),
                IndexName = indexName,
                KeyConditionExpression = keyCondition,
                FilterExpression = filterExpression,
                ExpressionAttributeNames = names,
                ExpressionAttributeValues = values,
                ScanIndexForward = true,
                ExclusiveStartKey = startKey,
            }, ct);

            foreach (var item in response.Items)
                matched.Add(DynamoDbSchema.FromItem(resource, item));
            startKey = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        }
        while (startKey is not null && matched.Count <= skip + pageSize);

        if (skip > 0)
            matched = skip >= matched.Count ? [] : matched.GetRange(skip, matched.Count - skip);

        string? nextToken = null;
        if (matched.Count > pageSize)
        {
            nextToken = matched[pageSize - 1].Id;
            matched = matched.GetRange(0, pageSize);
        }

        return new ListResult { Items = matched, NextPageToken = nextToken };
    }

    /// <summary>
    /// AEP-159 reading across collections: with a wildcard parent there is no single partition to
    /// Query, so we Scan the whole table (filter pushed down as a FilterExpression) and order /
    /// paginate by id in-process. This reads more than a scoped Query — documented as the fallback.
    /// </summary>
    private async Task<ListResult> ListAcrossCollectionsAsync(
        ResourceDefinition resource, ListOptions options, CancellationToken ct)
    {
        var names = new Dictionary<string, string>();
        var values = new Dictionary<string, AttributeValue>();
        string? filterExpression = null;
        if (options.Filter is not null)
        {
            var translator = new DynamoDbFilterTranslator(resource, AllowedFields(resource));
            filterExpression = translator.Translate(options.Filter);
            foreach (var (k, v) in translator.Names) names[k] = v;
            foreach (var (k, v) in translator.Values) values[k] = v;
        }

        var all = new List<StoredResource>();
        Dictionary<string, AttributeValue>? startKey = null;
        do
        {
            var response = await _client.ScanAsync(new ScanRequest
            {
                TableName = DynamoDbSchema.TableName(_prefix, resource),
                FilterExpression = filterExpression,
                ExpressionAttributeNames = names.Count > 0 ? names : null,
                ExpressionAttributeValues = values.Count > 0 ? values : null,
                ExclusiveStartKey = startKey,
            }, ct);
            foreach (var item in response.Items)
                all.Add(DynamoDbSchema.FromItem(resource, item));
            startKey = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        }
        while (startKey is not null);

        all.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id)); // match the id-ordered Query path
        var rows = all.AsEnumerable();
        if (!string.IsNullOrEmpty(options.PageToken))
            rows = rows.Where(r => string.CompareOrdinal(r.Id, options.PageToken) > 0);
        if (options.Skip > 0)
            rows = rows.Skip(options.Skip);

        var pageSize = Math.Clamp(options.PageSize, 1, ListOptions.MaxPageSize);
        var page = rows.Take(pageSize + 1).ToList();
        string? nextToken = null;
        if (page.Count > pageSize)
        {
            nextToken = page[pageSize - 1].Id;
            page = page.GetRange(0, pageSize);
        }
        return new ListResult { Items = page, NextPageToken = nextToken };
    }

    public async Task InsertAsync(
        ResourceDefinition resource,
        StoredResource stored,
        IReadOnlyDictionary<string, string> directParentIds,
        CancellationToken ct = default)
    {
        try
        {
            await _client.PutItemAsync(new PutItemRequest
            {
                TableName = DynamoDbSchema.TableName(_prefix, resource),
                Item = DynamoDbSchema.ToItem(resource, stored, directParentIds),
                ConditionExpression = "attribute_not_exists(#p)",
                ExpressionAttributeNames = new() { ["#p"] = DynamoDbSchema.PathAttr },
            }, ct);
        }
        catch (ConditionalCheckFailedException)
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
        var props = new HashSet<string>(DynamoDbSchema.UserPropertyNames(resource), StringComparer.Ordinal);
        var indexed = DynamoDbSchema.SingleFieldIndexes(resource).ToHashSet(StringComparer.Ordinal);
        var scope = DynamoDbSchema.ScopeFromPath(resource, path);

        var names = new Dictionary<string, string> { ["#p"] = DynamoDbSchema.PathAttr, ["#ut"] = DynamoDbSchema.UpdateTimeAttr };
        var values = new Dictionary<string, AttributeValue> { [":ut"] = new() { S = updateTime } };
        var sets = new List<string> { "#ut = :ut" };
        var removes = new List<string>();

        var i = 0;
        foreach (var (name, value) in fields)
        {
            if (!props.Contains(name)) continue;
            var nameKey = $"#n{i}";
            names[nameKey] = name;
            var av = DynamoDbSchema.ToAttribute(value, resource.Schema.Properties[name]);
            if (av is null)
                removes.Add(nameKey); // null clears the attribute
            else
            {
                var valKey = $":v{i}";
                values[valKey] = av;
                sets.Add($"{nameKey} = {valKey}");
            }

            // Keep the field's index-key attribute in sync so its GSI stays correct.
            if (indexed.Contains(name))
            {
                var idxKey = $"#ix{i}";
                names[idxKey] = DynamoDbSchema.IndexAttr(name);
                var keyPart = DynamoDbSchema.KeyPart(value, resource.Schema.Properties[name]);
                if (keyPart is null)
                    removes.Add(idxKey);
                else
                {
                    var idxValKey = $":ix{i}";
                    values[idxValKey] = new AttributeValue { S = DynamoDbSchema.IndexKey(scope, keyPart) };
                    sets.Add($"{idxKey} = {idxValKey}");
                }
            }

            i++;
        }

        var expression = "SET " + string.Join(", ", sets);
        if (removes.Count > 0)
            expression += " REMOVE " + string.Join(", ", removes);

        // Atomic optimistic-concurrency guard (AEP-154): require the stored update_time to match.
        var condition = "attribute_exists(#p)";
        if (expectedUpdateTime is not null)
        {
            condition += " AND #ut = :expected_ut";
            values[":expected_ut"] = new AttributeValue { S = expectedUpdateTime };
        }

        try
        {
            await _client.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = DynamoDbSchema.TableName(_prefix, resource),
                Key = new() { [DynamoDbSchema.PathAttr] = new AttributeValue { S = path } },
                UpdateExpression = expression,
                ConditionExpression = condition,
                ExpressionAttributeNames = names,
                ExpressionAttributeValues = values,
            }, ct);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(
        ResourceDefinition resource, string path, string? expectedUpdateTime = null, CancellationToken ct = default)
    {
        var condition = "attribute_exists(#p)";
        var names = new Dictionary<string, string> { ["#p"] = DynamoDbSchema.PathAttr };
        var values = new Dictionary<string, AttributeValue>();
        if (expectedUpdateTime is not null) // optimistic-concurrency guard (AEP-154)
        {
            condition += " AND #ut = :expected_ut";
            names["#ut"] = DynamoDbSchema.UpdateTimeAttr;
            values[":expected_ut"] = new AttributeValue { S = expectedUpdateTime };
        }

        try
        {
            await _client.DeleteItemAsync(new DeleteItemRequest
            {
                TableName = DynamoDbSchema.TableName(_prefix, resource),
                Key = new() { [DynamoDbSchema.PathAttr] = new AttributeValue { S = path } },
                ConditionExpression = condition,
                ExpressionAttributeNames = names,
                ExpressionAttributeValues = values.Count > 0 ? values : null,
            }, ct);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public void Dispose() => _client.Dispose();

    // ---- helpers ----

    private async Task WaitUntilActiveAsync(string table, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var described = await _client.DescribeTableAsync(table, ct);
            var indexesReady = described.Table.GlobalSecondaryIndexes.All(g => g.IndexStatus == IndexStatus.ACTIVE);
            if (described.Table.TableStatus == TableStatus.ACTIVE && indexesReady)
                return;
            await Task.Delay(200, ct);
        }
        throw new TimeoutException($"DynamoDB table '{table}' did not become active.");
    }

    private static IReadOnlySet<string> AllowedFields(ResourceDefinition resource)
    {
        var set = new HashSet<string>(DynamoDbSchema.UserPropertyNames(resource), StringComparer.Ordinal);
        set.UnionWith(StandardFields);
        return set;
    }

    /// <summary>
    /// Finds a leading equality on an indexed field to push to a GSI. Returns that field/value
    /// plus the remaining predicates (to run as a FilterExpression), or (null, null, filter) to
    /// scan the parent partition.
    /// </summary>
    private static (string? Field, object? Value, FilterExpression? Residual) PlanIndex(
        FilterExpression? filter, IReadOnlySet<string> indexedFields)
    {
        if (filter is null)
            return (null, null, null);

        var conjuncts = Flatten(filter).ToList();
        for (var i = 0; i < conjuncts.Count; i++)
        {
            if (conjuncts[i] is ComparisonExpression { Operator: FilterOperator.Equal } cmp && indexedFields.Contains(cmp.Field))
            {
                var rest = conjuncts.Where((_, j) => j != i).ToList();
                FilterExpression? residual = rest.Count == 0
                    ? null
                    : rest.Aggregate((a, b) => new LogicalExpression { Operator = LogicalOperator.And, Left = a, Right = b });
                return (cmp.Field, cmp.Value, residual);
            }
        }
        return (null, null, filter);
    }

    /// <summary>Splits a top-level AND chain into its conjuncts (OR/comparison nodes stay whole).</summary>
    private static IEnumerable<FilterExpression> Flatten(FilterExpression expr) =>
        expr is LogicalExpression { Operator: LogicalOperator.And } l
            ? Flatten(l.Left).Concat(Flatten(l.Right))
            : [expr];
}
