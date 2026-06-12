using Aep.Storage.Abstractions.Filtering;

namespace Aep.Storage.Sqlite.Tests;

public sealed class FilterParserTests
{
    [Fact]
    public void Null_or_blank_returns_null()
    {
        Assert.Null(FilterParser.Parse(null));
        Assert.Null(FilterParser.Parse("   "));
    }

    [Fact]
    public void Parses_cel_equality()
    {
        var expr = Assert.IsType<ComparisonExpression>(FilterParser.Parse("author == \"Orwell\""));
        Assert.Equal("author", expr.Field);
        Assert.Equal(FilterOperator.Equal, expr.Operator);
        Assert.Equal("Orwell", expr.Value);
    }

    [Fact]
    public void Parses_numeric_and_bool_literals()
    {
        var price = Assert.IsType<ComparisonExpression>(FilterParser.Parse("price >= 10"));
        Assert.Equal(10L, Assert.IsType<long>(price.Value));

        var published = Assert.IsType<ComparisonExpression>(FilterParser.Parse("published == true"));
        Assert.Equal(true, Assert.IsType<bool>(published.Value));

        var rating = Assert.IsType<ComparisonExpression>(FilterParser.Parse("rating < 4.5"));
        Assert.Equal(4.5d, Assert.IsType<double>(rating.Value));
    }

    [Fact]
    public void Flips_operator_when_literal_is_on_the_left()
    {
        // CEL allows `1000 < price`; lowered to `price > 1000`.
        var expr = Assert.IsType<ComparisonExpression>(FilterParser.Parse("1000 < price"));
        Assert.Equal("price", expr.Field);
        Assert.Equal(FilterOperator.GreaterThan, expr.Operator);
        Assert.Equal(1000L, expr.Value);
    }

    [Fact]
    public void Respects_cel_and_or_precedence()
    {
        // a == 1 || b == 2 && c == 3  ==>  a==1 OR (b==2 AND c==3)
        var root = Assert.IsType<LogicalExpression>(FilterParser.Parse("a == 1 || b == 2 && c == 3"));
        Assert.Equal(LogicalOperator.Or, root.Operator);
        Assert.IsType<ComparisonExpression>(root.Left);
        var right = Assert.IsType<LogicalExpression>(root.Right);
        Assert.Equal(LogicalOperator.And, right.Operator);
    }

    [Fact]
    public void Parentheses_override_precedence()
    {
        var root = Assert.IsType<LogicalExpression>(FilterParser.Parse("(a == 1 || b == 2) && c == 3"));
        Assert.Equal(LogicalOperator.And, root.Operator);
        Assert.IsType<LogicalExpression>(root.Left);
    }

    [Theory]
    [InlineData("author ==")]            // incomplete
    [InlineData("== 5")]                 // missing lhs
    [InlineData("author \"Orwell\"")]    // missing operator
    [InlineData("(author == \"x\"")]     // unbalanced paren
    public void Rejects_syntactically_invalid_cel(string filter)
    {
        Assert.Throws<FilterParseException>(() => FilterParser.Parse(filter));
    }

    [Theory]
    [InlineData("author = \"Orwell\"")]              // '=' is assignment, not valid CEL
    [InlineData("author == \"x\" AND price > 1")]    // AND/OR keywords are not CEL
    [InlineData("!published")]                        // unary not is unsupported
    [InlineData("startsWith(title, \"a\")")]         // functions/macros unsupported
    [InlineData("a == b")]                            // both sides fields
    [InlineData("1 == 2")]                            // both sides literals
    public void Rejects_non_cel_or_unsupported_constructs(string filter)
    {
        Assert.Throws<FilterParseException>(() => FilterParser.Parse(filter));
    }
}
