namespace Aep.Storage.Abstractions.Filtering;

/// <summary>Comparison operators supported in list filters.</summary>
public enum FilterOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
}

/// <summary>Boolean connectives joining sub-expressions.</summary>
public enum LogicalOperator
{
    And,
    Or,
}

/// <summary>
/// A parsed list-filter expression (AEP-160), as a small AST. Storage providers
/// translate this into their native query form (e.g. a SQL WHERE clause), so the
/// parser stays provider-agnostic.
/// </summary>
public abstract class FilterExpression
{
}

/// <summary><c>field op value</c>, e.g. <c>author = "Orwell"</c> or <c>price &gt;= 10</c>.</summary>
public sealed class ComparisonExpression : FilterExpression
{
    public required string Field { get; init; }
    public required FilterOperator Operator { get; init; }

    /// <summary>Literal value: string, long, double, or bool.</summary>
    public required object? Value { get; init; }
}

/// <summary><c>left AND right</c> / <c>left OR right</c>.</summary>
public sealed class LogicalExpression : FilterExpression
{
    public required LogicalOperator Operator { get; init; }
    public required FilterExpression Left { get; init; }
    public required FilterExpression Right { get; init; }
}
