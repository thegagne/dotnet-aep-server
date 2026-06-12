using Amazon.DynamoDBv2.Model;
using Aep.Storage.Abstractions.Filtering;
using Aep.Storage.Abstractions.Model;

namespace Aep.Storage.DynamoDb;

/// <summary>
/// Translates a parsed list filter (AEP-160) into a DynamoDB <c>FilterExpression</c> with its
/// expression-attribute name/value maps, so filtering runs server-side on the Query.
/// </summary>
internal sealed class DynamoDbFilterTranslator(ResourceDefinition resource, IReadOnlySet<string> allowedFields)
{
    private readonly Dictionary<string, string> _names = new();
    private readonly Dictionary<string, AttributeValue> _values = new();
    private int _counter;

    public IReadOnlyDictionary<string, string> Names => _names;
    public IReadOnlyDictionary<string, AttributeValue> Values => _values;

    public string Translate(FilterExpression expr) => expr switch
    {
        ComparisonExpression c => TranslateComparison(c),
        LogicalExpression l => $"({Translate(l.Left)} {(l.Operator == LogicalOperator.And ? "AND" : "OR")} {Translate(l.Right)})",
        _ => throw new FilterParseException("unsupported filter expression"),
    };

    private string TranslateComparison(ComparisonExpression c)
    {
        if (!allowedFields.Contains(c.Field))
            throw new FilterParseException($"unknown filter field '{c.Field}'");

        var op = c.Operator switch
        {
            FilterOperator.Equal => "=",
            FilterOperator.NotEqual => "<>",
            FilterOperator.LessThan => "<",
            FilterOperator.LessThanOrEqual => "<=",
            FilterOperator.GreaterThan => ">",
            FilterOperator.GreaterThanOrEqual => ">=",
            _ => throw new FilterParseException("unsupported operator"),
        };

        var nameKey = $"#f{_counter}";
        var valueKey = $":f{_counter}";
        _counter++;

        _names[nameKey] = c.Field;
        _values[valueKey] = ToAttribute(c.Field, c.Value);
        return $"{nameKey} {op} {valueKey}";
    }

    private AttributeValue ToAttribute(string field, object? value)
    {
        // Standard fields (id, path, *_time) aren't in the schema; treat them as strings.
        if (resource.Schema.Properties.TryGetValue(field, out var prop))
            return DynamoDbSchema.ToAttribute(value, prop) ?? new AttributeValue { NULL = true };
        return new AttributeValue { S = value?.ToString() ?? "" };
    }
}
