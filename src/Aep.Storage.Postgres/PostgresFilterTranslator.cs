using Aep.Storage.Abstractions.Filtering;

namespace Aep.Storage.Postgres;

/// <summary>Translates a parsed list filter (AEP-160) into a parameterized PostgreSQL WHERE fragment.</summary>
internal sealed class PostgresFilterTranslator(IReadOnlySet<string> allowedFields)
{
    private readonly List<KeyValuePair<string, object?>> _parameters = [];
    private int _counter;

    public IReadOnlyList<KeyValuePair<string, object?>> Parameters => _parameters;

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

        var param = $"@f{_counter++}";
        // Postgres has a real boolean type, so bind the CLR value as-is.
        _parameters.Add(new KeyValuePair<string, object?>(param, c.Value));
        return $"{PostgresSchema.Quote(c.Field)} {op} {param}";
    }
}
