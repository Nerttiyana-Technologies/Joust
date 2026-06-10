using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Joust.Interop.InMemory;

/// <summary>
/// Compiles the subset of LanceDB filter syntax emitted by
/// <see cref="Joust.Linq.FilterTranslator"/> into a predicate over a row dictionary. This lets the
/// in-memory backend honour the exact same filter string the native engine would receive, keeping
/// the <see cref="INativeEngine"/> contract identical across backends.
/// </summary>
public static class FilterEvaluator
{
    /// <summary>A compiled row predicate.</summary>
    public delegate bool RowPredicate(IReadOnlyDictionary<string, object?> row);

    /// <summary>Compiles a filter string into a row predicate.</summary>
    public static RowPredicate Compile(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return static _ => true;

        var tokens = Tokenize(filter);
        int pos = 0;
        var pred = ParseOr(tokens, ref pos);
        if (pos != tokens.Count)
            throw new JoustException($"Unexpected trailing tokens in filter: '{filter}'.");
        return pred;
    }

    // -- parser -----------------------------------------------------------

    private static RowPredicate ParseOr(List<Token> t, ref int p)
    {
        var left = ParseAnd(t, ref p);
        while (PeekKeyword(t, p, "OR"))
        {
            p++;
            var right = ParseAnd(t, ref p);
            var l = left; var r = right;
            left = row => l(row) || r(row);
        }
        return left;
    }

    private static RowPredicate ParseAnd(List<Token> t, ref int p)
    {
        var left = ParseNot(t, ref p);
        while (PeekKeyword(t, p, "AND"))
        {
            p++;
            var right = ParseNot(t, ref p);
            var l = left; var r = right;
            left = row => l(row) && r(row);
        }
        return left;
    }

    private static RowPredicate ParseNot(List<Token> t, ref int p)
    {
        if (PeekKeyword(t, p, "NOT"))
        {
            p++;
            var inner = ParsePrimary(t, ref p);
            return row => !inner(row);
        }
        return ParsePrimary(t, ref p);
    }

    private static RowPredicate ParsePrimary(List<Token> t, ref int p)
    {
        if (Peek(t, p).Kind == TokenKind.LParen)
        {
            p++;
            var e = ParseOr(t, ref p);
            Expect(t, ref p, TokenKind.RParen);
            return e;
        }
        return ParseComparison(t, ref p);
    }

    private static RowPredicate ParseComparison(List<Token> t, ref int p)
    {
        var tok = Peek(t, p);

        if (tok.Kind == TokenKind.Keyword && tok.Text is "TRUE" or "FALSE")
        {
            p++;
            bool v = tok.Text == "TRUE";
            return _ => v;
        }

        if (tok.Kind != TokenKind.Ident)
            throw new JoustException($"Expected a column name in filter, found '{tok.Text}'.");

        string column = tok.Text;
        p++;
        var next = Peek(t, p);

        switch (next.Kind)
        {
            case TokenKind.Op:
                p++;
                var literal = ParseLiteral(t, ref p);
                return BuildComparison(column, next.Text, literal);

            case TokenKind.Keyword when next.Text == "IS":
                p++;
                bool negated = PeekKeyword(t, p, "NOT");
                if (negated) p++;
                ExpectKeyword(t, ref p, "NULL");
                return row => negated ? GetValue(row, column) is not null : GetValue(row, column) is null;

            case TokenKind.Keyword when next.Text == "IN":
                p++;
                Expect(t, ref p, TokenKind.LParen);
                var items = new List<object?>();
                if (Peek(t, p).Kind != TokenKind.RParen)
                {
                    items.Add(ParseLiteral(t, ref p));
                    while (Peek(t, p).Kind == TokenKind.Comma)
                    {
                        p++;
                        items.Add(ParseLiteral(t, ref p));
                    }
                }
                Expect(t, ref p, TokenKind.RParen);
                return row =>
                {
                    var v = GetValue(row, column);
                    foreach (var it in items)
                        if (ValuesEqual(v, it)) return true;
                    return false;
                };

            case TokenKind.Keyword when next.Text == "LIKE":
                p++;
                var pat = ParseLiteral(t, ref p) as string ?? "";
                var regex = LikeToRegex(pat);
                return row => GetValue(row, column) is { } v && regex.IsMatch(v.ToString() ?? "");

            default:
                // Bare boolean column.
                return row => GetValue(row, column) is bool b && b;
        }
    }

    private static RowPredicate BuildComparison(string column, string op, object? literal)
    {
        return row =>
        {
            var v = GetValue(row, column);
            switch (op)
            {
                case "=":
                    return ValuesEqual(v, literal);
                case "<>":
                case "!=":
                    return !ValuesEqual(v, literal);
                default:
                    var cmp = CompareOrder(v, literal);
                    if (cmp is null) return false;
                    return op switch
                    {
                        ">" => cmp > 0,
                        ">=" => cmp >= 0,
                        "<" => cmp < 0,
                        "<=" => cmp <= 0,
                        _ => false,
                    };
            }
        };
    }

    // -- value semantics --------------------------------------------------

    private static object? GetValue(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var v) ? v : null;

    private static bool ValuesEqual(object? rowVal, object? literal)
    {
        if (rowVal is null || literal is null)
            return rowVal is null && literal is null;

        if (literal is bool lb)
            return rowVal is bool rb && rb == lb;

        if (literal is double ld)
            return TryToDouble(rowVal, out var rd) && rd == ld;

        // literal is string
        return string.Equals(RowToString(rowVal), literal.ToString(), StringComparison.Ordinal);
    }

    private static int? CompareOrder(object? rowVal, object? literal)
    {
        if (rowVal is null || literal is null)
            return null;

        if (literal is double ld && TryToDouble(rowVal, out var rd))
            return rd.CompareTo(ld);

        if (literal is string ls)
            return string.CompareOrdinal(RowToString(rowVal), ls);

        return null;
    }

    private static bool TryToDouble(object value, out double result)
    {
        try
        {
            result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }

    private static string RowToString(object v) => v switch
    {
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
        Guid g => g.ToString(),
        Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    private static Regex LikeToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (var c in pattern)
        {
            sb.Append(c switch
            {
                '%' => ".*",
                '_' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.Singleline);
    }

    // -- tokenizer --------------------------------------------------------

    private enum TokenKind { Ident, Number, String, Op, Keyword, LParen, RParen, Comma }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static readonly HashSet<string> Keywords =
        new(StringComparer.OrdinalIgnoreCase) { "AND", "OR", "NOT", "IN", "IS", "NULL", "LIKE", "TRUE", "FALSE" };

    private static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            switch (c)
            {
                case '(': tokens.Add(new Token(TokenKind.LParen, "(")); i++; continue;
                case ')': tokens.Add(new Token(TokenKind.RParen, ")")); i++; continue;
                case ',': tokens.Add(new Token(TokenKind.Comma, ",")); i++; continue;
                case '\'':
                {
                    var sb = new StringBuilder();
                    i++;
                    while (i < s.Length)
                    {
                        if (s[i] == '\'')
                        {
                            if (i + 1 < s.Length && s[i + 1] == '\'') { sb.Append('\''); i += 2; continue; }
                            i++; break;
                        }
                        sb.Append(s[i]); i++;
                    }
                    tokens.Add(new Token(TokenKind.String, sb.ToString()));
                    continue;
                }
            }

            if (c is '=' or '<' or '>' or '!')
            {
                if (c == '<' && i + 1 < s.Length && s[i + 1] == '>') { tokens.Add(new Token(TokenKind.Op, "<>")); i += 2; continue; }
                if (c == '!' && i + 1 < s.Length && s[i + 1] == '=') { tokens.Add(new Token(TokenKind.Op, "!=")); i += 2; continue; }
                if ((c == '<' || c == '>') && i + 1 < s.Length && s[i + 1] == '=') { tokens.Add(new Token(TokenKind.Op, c + "=")); i += 2; continue; }
                tokens.Add(new Token(TokenKind.Op, c.ToString())); i++; continue;
            }

            // Number (optionally signed, with decimal/exponent).
            if (char.IsDigit(c) ||
                (c == '-' && i + 1 < s.Length && char.IsDigit(s[i + 1])) ||
                (c == '+' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
            {
                int start = i;
                i++;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] is '.' or 'e' or 'E' or '+' or '-'))
                {
                    // Allow +/- only immediately after e/E (exponent sign).
                    if ((s[i] is '+' or '-') && !(s[i - 1] is 'e' or 'E')) break;
                    i++;
                }
                tokens.Add(new Token(TokenKind.Number, s.Substring(start, i - start)));
                continue;
            }

            // Identifier or keyword.
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                var text = s.Substring(start, i - start);
                tokens.Add(Keywords.Contains(text)
                    ? new Token(TokenKind.Keyword, text.ToUpperInvariant())
                    : new Token(TokenKind.Ident, text));
                continue;
            }

            throw new JoustException($"Unexpected character '{c}' in filter at position {i}.");
        }
        return tokens;
    }

    private static object? ParseLiteral(List<Token> t, ref int p)
    {
        var tok = Peek(t, p);
        switch (tok.Kind)
        {
            case TokenKind.Number:
                p++;
                return double.Parse(tok.Text, CultureInfo.InvariantCulture);
            case TokenKind.String:
                p++;
                return tok.Text;
            case TokenKind.Keyword when tok.Text == "TRUE":
                p++; return true;
            case TokenKind.Keyword when tok.Text == "FALSE":
                p++; return false;
            case TokenKind.Keyword when tok.Text == "NULL":
                p++; return null;
            default:
                throw new JoustException($"Expected a literal in filter, found '{tok.Text}'.");
        }
    }

    private static Token Peek(List<Token> t, int p) =>
        p < t.Count ? t[p] : new Token(TokenKind.Comma, "<end>");

    private static bool PeekKeyword(List<Token> t, int p, string kw) =>
        p < t.Count && t[p].Kind == TokenKind.Keyword && t[p].Text == kw;

    private static void Expect(List<Token> t, ref int p, TokenKind kind)
    {
        if (p >= t.Count || t[p].Kind != kind)
            throw new JoustException($"Expected {kind} in filter.");
        p++;
    }

    private static void ExpectKeyword(List<Token> t, ref int p, string kw)
    {
        if (!PeekKeyword(t, p, kw))
            throw new JoustException($"Expected keyword '{kw}' in filter.");
        p++;
    }
}
