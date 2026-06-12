using System.Globalization;

namespace Aep.Storage.Abstractions.Filtering;

/// <summary>Thrown when a list filter expression cannot be parsed.</summary>
public sealed class FilterParseException(string message) : Exception(message);

/// <summary>
/// A dependency-free recursive-descent parser for a documented subset of
/// <see href="https://github.com/google/cel-spec">CEL</see>, used for AEP-160 list
/// filters. It deliberately accepts only a strict subset of real CEL, so every
/// expression it accepts is also valid under a full CEL engine (forward-compatible).
///
/// Supported grammar:
/// <code>
///   filter      := orExpr
///   orExpr      := andExpr ( "||" andExpr )*
///   andExpr     := primary ( "&amp;&amp;" primary )*
///   primary     := "(" orExpr ")" | comparison
///   comparison  := field compOp literal | literal compOp field
///   compOp      := "==" | "!=" | "&lt;" | "&lt;=" | "&gt;" | "&gt;="
///   field       := IDENT ( "." IDENT )*
///   literal     := STRING | NUMBER | "true" | "false"
/// </code>
///
/// Not supported (raise <see cref="FilterParseException"/> → HTTP 400 / INVALID_ARGUMENT):
/// functions/macros, the <c>in</c> operator, arithmetic, unary <c>!</c>, and
/// field-to-field or literal-to-literal comparisons. Note <c>=</c> and the keywords
/// <c>AND</c>/<c>OR</c> are not CEL and are rejected; use <c>==</c>, <c>&amp;&amp;</c>, <c>||</c>.
/// </summary>
public static class FilterParser
{
    /// <summary>Parses <paramref name="filter"/>, or returns null when it is null/whitespace.</summary>
    /// <exception cref="FilterParseException">The expression is not a valid CEL-subset filter.</exception>
    public static FilterExpression? Parse(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        var tokens = Tokenize(filter);
        var pos = 0;
        var expr = ParseOr(tokens, ref pos);
        if (tokens[pos].Kind != TokenKind.Eof)
            throw new FilterParseException($"unexpected token '{tokens[pos].Text}' in filter");
        return expr;
    }

    private static FilterExpression ParseOr(List<Token> tokens, ref int pos)
    {
        var left = ParseAnd(tokens, ref pos);
        while (tokens[pos].Kind == TokenKind.Or)
        {
            pos++;
            var right = ParseAnd(tokens, ref pos);
            left = new LogicalExpression { Operator = LogicalOperator.Or, Left = left, Right = right };
        }
        return left;
    }

    private static FilterExpression ParseAnd(List<Token> tokens, ref int pos)
    {
        var left = ParsePrimary(tokens, ref pos);
        while (tokens[pos].Kind == TokenKind.And)
        {
            pos++;
            var right = ParsePrimary(tokens, ref pos);
            left = new LogicalExpression { Operator = LogicalOperator.And, Left = left, Right = right };
        }
        return left;
    }

    private static FilterExpression ParsePrimary(List<Token> tokens, ref int pos)
    {
        if (tokens[pos].Kind == TokenKind.LParen)
        {
            pos++;
            var inner = ParseOr(tokens, ref pos);
            if (tokens[pos].Kind != TokenKind.RParen)
                throw new FilterParseException("expected ')' in filter");
            pos++;
            return inner;
        }
        return ParseComparison(tokens, ref pos);
    }

    private static FilterExpression ParseComparison(List<Token> tokens, ref int pos)
    {
        var left = ParseOperand(tokens, ref pos);

        var opTok = tokens[pos];
        if (opTok.Kind != TokenKind.Operator)
            throw new FilterParseException($"expected a comparison operator but found '{opTok.Text}'");
        pos++;

        var right = ParseOperand(tokens, ref pos);

        // Exactly one side must be a field and the other a literal (our column model).
        if (left.IsField && !right.IsField)
            return new ComparisonExpression { Field = left.Field!, Operator = MapOperator(opTok.Text), Value = right.Value };
        if (right.IsField && !left.IsField)
            return new ComparisonExpression { Field = right.Field!, Operator = Flip(MapOperator(opTok.Text)), Value = left.Value };

        throw new FilterParseException("each comparison must be between a field and a literal value");
    }

    private readonly record struct Operand(bool IsField, string? Field, object? Value);

    private static Operand ParseOperand(List<Token> tokens, ref int pos)
    {
        var tok = tokens[pos];
        switch (tok.Kind)
        {
            case TokenKind.Identifier:
                pos++;
                return new Operand(true, tok.Text, null);
            case TokenKind.String:
                pos++;
                return new Operand(false, null, tok.Text);
            case TokenKind.Number:
                pos++;
                return new Operand(false, null, ParseNumber(tok.Text));
            case TokenKind.Bool:
                pos++;
                return new Operand(false, null, bool.Parse(tok.Text));
            default:
                throw new FilterParseException($"expected a field or literal but found '{tok.Text}'");
        }
    }

    private static object ParseNumber(string text)
    {
        // Separate statements (not a ?:) so an integer doesn't widen to double.
        if (text.Contains('.') || text.Contains('e') || text.Contains('E'))
            return double.Parse(text, CultureInfo.InvariantCulture);
        return long.Parse(text, CultureInfo.InvariantCulture);
    }

    private static FilterOperator MapOperator(string op) => op switch
    {
        "==" => FilterOperator.Equal,
        "!=" => FilterOperator.NotEqual,
        "<" => FilterOperator.LessThan,
        "<=" => FilterOperator.LessThanOrEqual,
        ">" => FilterOperator.GreaterThan,
        ">=" => FilterOperator.GreaterThanOrEqual,
        _ => throw new FilterParseException($"unknown operator '{op}'"),
    };

    private static FilterOperator Flip(FilterOperator op) => op switch
    {
        FilterOperator.LessThan => FilterOperator.GreaterThan,
        FilterOperator.LessThanOrEqual => FilterOperator.GreaterThanOrEqual,
        FilterOperator.GreaterThan => FilterOperator.LessThan,
        FilterOperator.GreaterThanOrEqual => FilterOperator.LessThanOrEqual,
        _ => op, // == and != are symmetric
    };

    private enum TokenKind { Identifier, String, Number, Bool, Operator, And, Or, LParen, RParen, Eof }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            switch (c)
            {
                case '(': tokens.Add(new Token(TokenKind.LParen, "(")); i++; continue;
                case ')': tokens.Add(new Token(TokenKind.RParen, ")")); i++; continue;
                case '"' or '\'':
                    tokens.Add(new Token(TokenKind.String, ReadString(input, ref i, c)));
                    continue;
                case '&':
                    Require(input, i, '&', "expected '&&'"); i += 2;
                    tokens.Add(new Token(TokenKind.And, "&&")); continue;
                case '|':
                    Require(input, i, '|', "expected '||'"); i += 2;
                    tokens.Add(new Token(TokenKind.Or, "||")); continue;
            }

            if (c is '=' or '!' or '<' or '>')
            {
                tokens.Add(new Token(TokenKind.Operator, ReadOperator(input, ref i)));
                continue;
            }

            if (char.IsDigit(c) || (c == '-' && i + 1 < input.Length && char.IsDigit(input[i + 1])))
            {
                tokens.Add(new Token(TokenKind.Number, ReadNumber(input, ref i)));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var ident = ReadIdentifier(input, ref i);
                tokens.Add(ident is "true" or "false"
                    ? new Token(TokenKind.Bool, ident)
                    : new Token(TokenKind.Identifier, ident));
                continue;
            }

            throw new FilterParseException($"unexpected character '{c}' in filter");
        }
        tokens.Add(new Token(TokenKind.Eof, ""));
        return tokens;
    }

    private static void Require(string input, int i, char next, string message)
    {
        if (i + 1 >= input.Length || input[i + 1] != next)
            throw new FilterParseException(message);
    }

    private static string ReadString(string input, ref int i, char quote)
    {
        i++; // opening quote
        var start = i;
        while (i < input.Length && input[i] != quote)
            i++;
        if (i >= input.Length)
            throw new FilterParseException("unterminated string literal in filter");
        var s = input[start..i];
        i++; // closing quote
        return s;
    }

    // CEL comparison operators only. A lone '=' or '!' is not valid CEL and errors.
    private static string ReadOperator(string input, ref int i)
    {
        var c = input[i];
        var next = i + 1 < input.Length ? input[i + 1] : '\0';
        switch (c)
        {
            case '=' when next == '=': i += 2; return "==";
            case '!' when next == '=': i += 2; return "!=";
            case '<' when next == '=': i += 2; return "<=";
            case '>' when next == '=': i += 2; return ">=";
            case '<': i++; return "<";
            case '>': i++; return ">";
            case '=': throw new FilterParseException("'=' is not a CEL operator; use '=='");
            default: throw new FilterParseException("'!' must be part of '!='");
        }
    }

    private static string ReadNumber(string input, ref int i)
    {
        var start = i;
        if (input[i] == '-') i++;
        while (i < input.Length && (char.IsDigit(input[i]) || input[i] is '.' or 'e' or 'E' or '+' or '-'))
            i++;
        return input[start..i];
    }

    private static string ReadIdentifier(string input, ref int i)
    {
        var start = i;
        while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] is '_' or '.'))
            i++;
        return input[start..i];
    }
}
