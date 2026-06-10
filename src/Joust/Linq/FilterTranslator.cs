using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Joust.Schema;

namespace Joust.Linq;

/// <summary>
/// Translates a strongly-typed predicate expression into a LanceDB filter string. Property
/// references resolve through the compiled <see cref="TableSchema"/>, so a property rename is a
/// compile-time/refactor concern rather than a runtime string bug. Unsupported expressions throw
/// <see cref="NotSupportedExpressionException"/> — the query fails loudly instead of silently
/// degrading to a full table scan.
/// </summary>
public static class FilterTranslator
{
    /// <summary>Translates a predicate into a LanceDB filter string.</summary>
    public static string Translate<T>(Expression<Func<T, bool>> predicate, TableSchema schema)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(schema);
        var parameter = predicate.Parameters[0];
        return Visit(predicate.Body, parameter, schema);
    }

    private static string Visit(Expression expr, ParameterExpression p, TableSchema schema)
    {
        expr = Unwrap(expr);

        switch (expr)
        {
            case BinaryExpression b when b.NodeType is ExpressionType.AndAlso:
                return $"({Visit(b.Left, p, schema)} AND {Visit(b.Right, p, schema)})";

            case BinaryExpression b when b.NodeType is ExpressionType.OrElse:
                return $"({Visit(b.Left, p, schema)} OR {Visit(b.Right, p, schema)})";

            case BinaryExpression b:
                return VisitComparison(b, p, schema);

            case UnaryExpression u when u.NodeType is ExpressionType.Not:
                if (TryGetColumn(u.Operand, p, schema, out var notCol))
                    return $"{notCol} = false";
                return $"NOT ({Visit(u.Operand, p, schema)})";

            case MethodCallExpression m:
                return VisitMethodCall(m, p, schema);

            // A bare boolean column, e.g. `x.IsActive`.
            case MemberExpression when TryGetColumn(expr, p, schema, out var boolCol):
                return $"{boolCol} = true";

            case ConstantExpression c when c.Value is bool cb:
                return cb ? "true" : "false";
        }

        throw Unsupported(expr);
    }

    private static string VisitComparison(BinaryExpression b, ParameterExpression p, TableSchema schema)
    {
        var op = b.NodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "<>",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            _ => throw Unsupported(b),
        };

        // Identify which side is the column and which is the value.
        if (TryGetColumn(b.Left, p, schema, out var leftCol))
        {
            var value = EvaluateOrThrow(b.Right);
            return RenderComparison(leftCol, op, value, b);
        }

        if (TryGetColumn(b.Right, p, schema, out var rightCol))
        {
            var value = EvaluateOrThrow(b.Left);
            return RenderComparison(rightCol, Flip(op), value, b);
        }

        throw new NotSupportedExpressionException(
            $"Comparison '{b}' does not reference exactly one mapped column. " +
            "Both sides of a filter comparison cannot be column-independent.");
    }

    private static string RenderComparison(string column, string op, object? value, Expression source)
    {
        if (value is null)
        {
            return op switch
            {
                "=" => $"{column} IS NULL",
                "<>" => $"{column} IS NOT NULL",
                _ => throw new NotSupportedExpressionException(
                        $"Operator '{op}' cannot be used with null in '{source}'."),
            };
        }
        return $"{column} {op} {RenderLiteral(value)}";
    }

    private static string VisitMethodCall(MethodCallExpression m, ParameterExpression p, TableSchema schema)
    {
        // Collection.Contains(column)  ->  column IN (...)
        if (m.Method.Name == "Contains")
        {
            // Instance form: list.Contains(x.Id)
            if (m.Object is not null && m.Arguments.Count == 1 &&
                TryGetColumn(m.Arguments[0], p, schema, out var inCol) &&
                IsEnumerableValue(m.Object))
            {
                return RenderIn(inCol, EvaluateOrThrow(StripConversions(m.Object)));
            }

            // Static form: Enumerable.Contains(source, x.Id) — or the span-based
            // MemoryExtensions.Contains, where source[0] is an implicit array→ReadOnlySpan
            // conversion. The last argument is always the searched value.
            if (m.Object is null && m.Arguments.Count >= 2 &&
                TryGetColumn(m.Arguments[^1], p, schema, out var inCol2))
            {
                return RenderIn(inCol2, EvaluateOrThrow(StripConversions(m.Arguments[0])));
            }

            // String.Contains(value)  ->  column LIKE '%value%'
            if (m.Object is not null && m.Arguments.Count == 1 &&
                TryGetColumn(m.Object, p, schema, out var likeCol) &&
                m.Object.Type == typeof(string))
            {
                return $"{likeCol} LIKE {RenderLikeLiteral(EvaluateOrThrow(m.Arguments[0]), prefix: true, suffix: true)}";
            }
        }

        if (m.Object is not null && m.Object.Type == typeof(string) && m.Arguments.Count == 1 &&
            TryGetColumn(m.Object, p, schema, out var sCol))
        {
            if (m.Method.Name == "StartsWith")
                return $"{sCol} LIKE {RenderLikeLiteral(EvaluateOrThrow(m.Arguments[0]), prefix: false, suffix: true)}";
            if (m.Method.Name == "EndsWith")
                return $"{sCol} LIKE {RenderLikeLiteral(EvaluateOrThrow(m.Arguments[0]), prefix: true, suffix: false)}";
        }

        throw Unsupported(m);
    }

    private static string RenderIn(string column, object? collection)
    {
        if (collection is not IEnumerable e)
            throw new NotSupportedExpressionException($"Expected a collection for IN over '{column}'.");

        var parts = new List<string>();
        foreach (var item in e)
            parts.Add(RenderLiteral(item));

        if (parts.Count == 0)
            return "1 = 0"; // empty IN matches nothing

        return $"{column} IN ({string.Join(", ", parts)})";
    }

    // -- helpers ----------------------------------------------------------

    private static Expression Unwrap(Expression e)
    {
        while (e is UnaryExpression u && (u.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.Quote))
            e = u.Operand;
        return e;
    }

    /// <summary>
    /// Strips conversion wrappers — both <c>Convert</c> unary nodes and user-defined
    /// <c>op_Implicit</c>/<c>op_Explicit</c> calls (e.g. the <c>T[]</c> → <c>ReadOnlySpan&lt;T&gt;</c>
    /// conversion the compiler inserts for span-based <c>Contains</c>) — to recover the original
    /// collection expression so it can be evaluated to a real array/list.
    /// </summary>
    private static Expression StripConversions(Expression e)
    {
        while (true)
        {
            e = Unwrap(e);
            if (e is MethodCallExpression mc && mc.Arguments.Count == 1 &&
                (mc.Method.Name == "op_Implicit" || mc.Method.Name == "op_Explicit"))
            {
                e = mc.Arguments[0];
                continue;
            }
            return e;
        }
    }

    private static bool TryGetColumn(Expression e, ParameterExpression p, TableSchema schema, out string columnName)
    {
        columnName = "";
        e = Unwrap(e);
        if (e is MemberExpression m && m.Member is PropertyInfo prop && Unwrap(m.Expression!) == p)
        {
            var col = schema.ByPropertyName(prop.Name);
            if (col is null)
                throw new NotSupportedExpressionException(
                    $"Property '{prop.Name}' is not a mapped column on table '{schema.Name}'.");
            columnName = col.ColumnName;
            return true;
        }
        return false;
    }

    private static object? EvaluateOrThrow(Expression e)
    {
        e = Unwrap(e);
        if (e is ConstantExpression c)
            return c.Value;
        try
        {
            return Expression.Lambda(e).Compile().DynamicInvoke();
        }
        catch (Exception ex)
        {
            throw new NotSupportedExpressionException($"Could not evaluate '{e}' to a constant value: {ex.Message}");
        }
    }

    private static bool IsEnumerableValue(Expression e)
    {
        var t = e.Type;
        return t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t);
    }

    private static string Flip(string op) => op switch
    {
        ">" => "<",
        ">=" => "<=",
        "<" => ">",
        "<=" => ">=",
        _ => op, // = and <> are symmetric
    };

    private static string RenderLiteral(object? value)
    {
        switch (value)
        {
            case null:
                return "NULL";
            case bool b:
                return b ? "true" : "false";
            case string s:
                return Quote(s);
            case char ch:
                return Quote(ch.ToString());
            case Guid g:
                return Quote(g.ToString());
            case DateTime dt:
                return Quote(dt.ToString("o", CultureInfo.InvariantCulture));
            case DateTimeOffset dto:
                return Quote(dto.ToString("o", CultureInfo.InvariantCulture));
            case Enum en:
                return Convert.ToInt64(en, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            case float f:
                return f.ToString("R", CultureInfo.InvariantCulture);
            case double d:
                return d.ToString("R", CultureInfo.InvariantCulture);
            case decimal dec:
                return dec.ToString(CultureInfo.InvariantCulture);
            case IFormattable fmt:
                return fmt.ToString(null, CultureInfo.InvariantCulture);
            default:
                return Quote(value.ToString() ?? "");
        }
    }

    private static string RenderLikeLiteral(object? value, bool prefix, bool suffix)
    {
        var s = value?.ToString() ?? "";
        var sb = new StringBuilder();
        if (prefix) sb.Append('%');
        sb.Append(s);
        if (suffix) sb.Append('%');
        return Quote(sb.ToString());
    }

    private static string Quote(string s) => "'" + s.Replace("'", "''") + "'";

    private static NotSupportedExpressionException Unsupported(Expression e) =>
        new($"Expression '{e}' (node type {e.NodeType}) cannot be translated to a LanceDB filter. " +
            "Supported: comparisons, AND/OR/NOT, boolean columns, IN via Contains, and string LIKE " +
            "via Contains/StartsWith/EndsWith. For anything else, pass a raw filter string.");
}
