using Aep.Storage.Abstractions.Filtering;

namespace Aep.Storage.Sqlite;

/// <summary>Translates a parsed list filter (AEP-160) into a parameterized SQLite WHERE fragment.</summary>
internal sealed class SqliteFilterTranslator(IReadOnlySet<string> allowedFields)
{
    private readonly List<KeyValuePair<string, object?>> _parameters = [];
    private int _counter;

    public IReadOnlyList<KeyValuePair<string, object?>> Parameters => _parameters;

    /// <summary>Returns a boolean SQL expression for the filter, registering bound parameters as a side effect.</summary>
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
            FilterOperator.NotEqual => "!=",
            FilterOperator.LessThan => "<",
            FilterOperator.LessThanOrEqual => "<=",
            FilterOperator.GreaterThan => ">",
            FilterOperator.GreaterThanOrEqual => ">=",
            _ => throw new FilterParseException("unsupported operator"),
        };

        var param = $"@f{_counter++}";
        // SQLite has no native boolean; store/compare as 0/1 to match the column encoding.
        var value = c.Value is bool b ? (b ? 1L : 0L) : c.Value;
        _parameters.Add(new KeyValuePair<string, object?>(param, value));
        return $"{SqliteSchema.Quote(c.Field)} {op} {param}";
    }
}
