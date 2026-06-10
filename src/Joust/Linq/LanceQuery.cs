using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Joust.Interop;
using Joust.Mapping;
using Joust.Schema;

namespace Joust.Linq;

/// <summary>
/// A composable, strongly-typed query over a table. It presents the familiar LINQ-style surface
/// (<c>Where</c>/<c>OrderBy</c>/<c>Take</c>/<c>Skip</c>) and executes asynchronously against the
/// configured backend. Filters are compiled by <see cref="FilterTranslator"/>; vector ordering is
/// pushed down to the engine's nearest-neighbour search.
/// </summary>
public sealed class LanceQuery<T> where T : class
{
    private readonly INativeEngine _engine;
    private readonly TableSchema _schema;
    private readonly PocoMapper<T> _mapper;

    private Expression<Func<T, bool>>? _filter;
    private float[]? _vectorQuery;
    private string? _vectorColumn;
    private DistanceMetric _metric = DistanceMetric.Cosine;
    private string? _orderByColumn;
    private bool _orderDescending;
    private int? _take;
    private int? _skip;

    internal LanceQuery(INativeEngine engine, TableSchema schema, PocoMapper<T> mapper)
    {
        _engine = engine;
        _schema = schema;
        _mapper = mapper;
    }

    /// <summary>Adds a filter predicate. Multiple calls are combined with logical AND.</summary>
    public LanceQuery<T> Where(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _filter = _filter is null ? predicate : Combine(_filter, predicate);
        return this;
    }

    /// <summary>
    /// Orders by nearest vector distance, e.g. <c>OrderBy(x =&gt; x.Embedding.CosineDistance(query))</c>.
    /// Results are returned closest-first.
    /// </summary>
    public LanceQuery<T> OrderBy(Expression<Func<T, double>> distanceSelector)
    {
        ArgumentNullException.ThrowIfNull(distanceSelector);
        ExtractVectorSearch(distanceSelector);
        return this;
    }

    /// <summary>
    /// Performs a nearest-neighbour search against the table's single <c>[Vector]</c> column using its
    /// declared metric. Equivalent to <c>OrderBy(x =&gt; x.Vector.CosineDistance(q))</c> but driven by a
    /// raw query vector (used by the AI adapters).
    /// </summary>
    public LanceQuery<T> NearestTo(ReadOnlyMemory<float> vector)
    {
        var vcol = _schema.VectorColumn
            ?? throw new NotSupportedExpressionException(
                $"Table '{_schema.Name}' has no [Vector] column to search.");
        _vectorColumn = vcol.ColumnName;
        _metric = vcol.Metric;
        _vectorQuery = vector.ToArray();
        return this;
    }

    /// <summary>Orders by a scalar column ascending. Ignored if a vector search is also specified.</summary>
    public LanceQuery<T> OrderByColumn<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        SetScalarOrder(keySelector, descending: false);
        return this;
    }

    /// <summary>Orders by a scalar column descending. Ignored if a vector search is also specified.</summary>
    public LanceQuery<T> OrderByColumnDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        SetScalarOrder(keySelector, descending: true);
        return this;
    }

    /// <summary>Projects each result row. The projection runs as rows stream from the backend.</summary>
    public LanceProjection<TResult> Select<TResult>(Expression<Func<T, TResult>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var map = selector.Compile();
        return new LanceProjection<TResult>(ct => Project(map, ct));
    }

    /// <summary>Returns true if any row matches the current filter.</summary>
    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var _ in AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
            return true;
        return false;
    }

    /// <summary>Limits the number of rows returned.</summary>
    public LanceQuery<T> Take(int count)
    {
        _take = count < 0 ? 0 : count;
        return this;
    }

    /// <summary>Skips a number of rows.</summary>
    public LanceQuery<T> Skip(int count)
    {
        _skip = count < 0 ? 0 : count;
        return this;
    }

    /// <summary>Executes the query and returns all matching rows as a list.</summary>
    public async Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<T>();
        await foreach (var item in AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
            list.Add(item);
        return list;
    }

    /// <summary>Executes the query and returns the first matching row, or null.</summary>
    public async Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var prior = _take;
        _take = 1;
        try
        {
            await foreach (var item in AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
                return item;
            return null;
        }
        finally
        {
            _take = prior;
        }
    }

    /// <summary>Counts rows matching the current filter (ignores ordering/paging).</summary>
    public Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        string? filter = _filter is null ? null : FilterTranslator.Translate(_filter, _schema);
        return _engine.CountAsync(_schema.Name, filter, cancellationToken);
    }

    /// <summary>Streams matching rows as they are produced by the engine.</summary>
    public async IAsyncEnumerable<T> AsAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = new NativeQuery
        {
            Filter = _filter is null ? null : FilterTranslator.Translate(_filter, _schema),
            VectorQuery = _vectorQuery,
            VectorColumn = _vectorColumn,
            Metric = _metric,
            OrderByColumn = _orderByColumn,
            OrderDescending = _orderDescending,
            Limit = _take,
            Offset = _skip,
        };

        await foreach (var row in _engine.QueryAsync(_schema.Name, query, cancellationToken).ConfigureAwait(false))
            yield return _mapper.FromRow(row);
    }

    // -- helpers ----------------------------------------------------------

    private async IAsyncEnumerable<TResult> Project<TResult>(
        Func<T, TResult> map, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
            yield return map(item);
    }

    private void SetScalarOrder<TKey>(Expression<Func<T, TKey>> keySelector, bool descending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        _orderByColumn = ResolveColumn(keySelector.Body, keySelector.Parameters[0])
            ?? throw new NotSupportedExpressionException(
                $"OrderByColumn expects a single mapped column, e.g. x => x.Price. Got: '{keySelector.Body}'.");
        _orderDescending = descending;
    }

    private static Expression<Func<T, bool>> Combine(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b)
    {
        var p = a.Parameters[0];
        var rebound = new ReplaceParameterVisitor(b.Parameters[0], p).Visit(b.Body)!;
        var body = Expression.AndAlso(a.Body, rebound);
        return Expression.Lambda<Func<T, bool>>(body, p);
    }

    private void ExtractVectorSearch(Expression<Func<T, double>> selector)
    {
        var body = selector.Body;
        while (body is UnaryExpression u && u.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
            body = u.Operand;

        if (body is not MethodCallExpression call ||
            call.Method.DeclaringType != typeof(VectorSearchExtensions))
        {
            throw new NotSupportedExpressionException(
                "OrderBy must select a vector distance, e.g. x => x.Embedding.CosineDistance(query). " +
                $"Got: '{selector.Body}'.");
        }

        _metric = call.Method.Name switch
        {
            nameof(VectorSearchExtensions.CosineDistance) => DistanceMetric.Cosine,
            nameof(VectorSearchExtensions.EuclideanDistance) => DistanceMetric.Euclidean,
            nameof(VectorSearchExtensions.DotDistance) => DistanceMetric.Dot,
            _ => throw new NotSupportedExpressionException($"Unknown distance function '{call.Method.Name}'."),
        };

        // Extension method: arg[0] is the column member, arg[1] is the query vector.
        var memberExpr = call.Arguments[0];
        var queryExpr = call.Arguments[1];

        _vectorColumn = ResolveColumn(memberExpr, selector.Parameters[0])
            ?? throw new NotSupportedExpressionException(
                $"The distance must be applied to a mapped vector column. Got: '{memberExpr}'.");

        var value = Expression.Lambda(queryExpr).Compile().DynamicInvoke();
        _vectorQuery = value switch
        {
            float[] fa => fa,
            IEnumerable<float> ef => ef.ToArray(),
            IEnumerable<double> ed => ed.Select(d => (float)d).ToArray(),
            _ => throw new NotSupportedExpressionException("The query vector must be a float[] or float sequence."),
        };
    }

    private string? ResolveColumn(Expression e, ParameterExpression p)
    {
        while (e is UnaryExpression u && u.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
            e = u.Operand;

        if (e is MemberExpression m && m.Member is PropertyInfo prop && m.Expression == p)
            return _schema.ByPropertyName(prop.Name)?.ColumnName;

        return null;
    }

    private sealed class ReplaceParameterVisitor(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
