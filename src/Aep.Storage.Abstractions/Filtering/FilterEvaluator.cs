using System.Globalization;

namespace Aep.Storage.Abstractions.Filtering;

/// <summary>
/// Evaluates a parsed <see cref="FilterExpression"/> in-process against a resource's
/// fields. Used by storage backends that cannot push the filter down to a query
/// engine (e.g. the in-memory store). Field names are validated against an allowed
/// set so an unknown field fails the same way the SQL translator does.
/// </summary>
public static class FilterEvaluator
{
    /// <summary>Returns true when <paramref name="filter"/> is null or the fields satisfy it.</summary>
    /// <exception cref="FilterParseException">The filter references a field not in <paramref name="allowedFields"/>.</exception>
    public static bool Matches(
        FilterExpression? filter,
        IReadOnlyDictionary<string, object?> fields,
        IReadOnlySet<string> allowedFields)
    {
        return filter is null || Eval(filter, fields, allowedFields);
    }

    /// <summary>
    /// Validates that every field referenced by the filter is allowed, independent of
    /// whether any rows exist — so an unknown field fails the same way (and as eagerly)
    /// as the SQL translator's column check.
    /// </summary>
    /// <exception cref="FilterParseException">A referenced field is not in <paramref name="allowedFields"/>.</exception>
    public static void Validate(FilterExpression? filter, IReadOnlySet<string> allowedFields)
    {
        switch (filter)
        {
            case null:
                return;
            case LogicalExpression l:
                Validate(l.Left, allowedFields);
                Validate(l.Right, allowedFields);
                break;
            case ComparisonExpression c when !allowedFields.Contains(c.Field):
                throw new FilterParseException($"unknown filter field '{c.Field}'");
        }
    }

    private static bool Eval(FilterExpression expr, IReadOnlyDictionary<string, object?> fields, IReadOnlySet<string> allowed) => expr switch
    {
        LogicalExpression l => l.Operator == LogicalOperator.And
            ? Eval(l.Left, fields, allowed) && Eval(l.Right, fields, allowed)
            : Eval(l.Left, fields, allowed) || Eval(l.Right, fields, allowed),
        ComparisonExpression c => EvalComparison(c, fields, allowed),
        _ => throw new FilterParseException("unsupported filter expression"),
    };

    private static bool EvalComparison(ComparisonExpression c, IReadOnlyDictionary<string, object?> fields, IReadOnlySet<string> allowed)
    {
        if (!allowed.Contains(c.Field))
            throw new FilterParseException($"unknown filter field '{c.Field}'");

        fields.TryGetValue(c.Field, out var fieldValue);
        int? cmp = Compare(fieldValue, c.Value);

        // Lifted nullable comparisons yield false when cmp is null (incomparable),
        // matching SQL three-valued logic — a null/incomparable field never matches,
        // including for "!=" (hence the explicit not-null guard there).
        return c.Operator switch
        {
            FilterOperator.Equal => cmp == 0,
            FilterOperator.NotEqual => cmp is not null && cmp != 0,
            FilterOperator.LessThan => cmp < 0,
            FilterOperator.LessThanOrEqual => cmp <= 0,
            FilterOperator.GreaterThan => cmp > 0,
            FilterOperator.GreaterThanOrEqual => cmp >= 0,
            _ => false,
        };
    }

    /// <summary>
    /// Three-way compare of a stored field value against a filter literal. Returns
    /// null when the values are not comparable (e.g. null field, or object/array vs
    /// scalar), which equality treats as "not equal" and ordering as "no match".
    /// </summary>
    private static int? Compare(object? fieldValue, object? literal)
    {
        if (fieldValue is null || literal is null)
            return fieldValue is null && literal is null ? 0 : null;

        switch (literal)
        {
            case bool b:
                return fieldValue is bool fb ? (fb == b ? 0 : 1) : null;
            case string s:
                return fieldValue is string fs ? Math.Sign(string.CompareOrdinal(fs, s)) : null;
            case long or double or int:
                return TryToDouble(fieldValue, out var fd) && TryToDouble(literal, out var ld)
                    ? fd.CompareTo(ld)
                    : null;
            default:
                return null;
        }
    }

    private static bool TryToDouble(object? value, out double result)
    {
        switch (value)
        {
            case long l: result = l; return true;
            case int i: result = i; return true;
            case double d: result = d; return true;
            case float f: result = f; return true;
            case string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                result = parsed; return true;
            default:
                result = 0; return false;
        }
    }
}
