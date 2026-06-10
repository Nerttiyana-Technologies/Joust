using System.Linq.Expressions;
using System.Reflection;
using Joust.Interop;
using Joust.Linq;
using Joust.Mapping;
using Joust.Schema;

namespace Joust;

/// <summary>
/// A typed handle to a table. Provides data operations and is the root of the LINQ-style query
/// surface. Obtain instances from <see cref="LanceDbContext"/>.
/// </summary>
public sealed class LanceTable<T> where T : class
{
    private readonly INativeEngine _engine;
    private readonly TableSchema _schema;
    private readonly PocoMapper<T> _mapper;

    internal LanceTable(INativeEngine engine, TableSchema schema)
    {
        _engine = engine;
        _schema = schema;
        _mapper = new PocoMapper<T>(schema);
    }

    /// <summary>The table name.</summary>
    public string Name => _schema.Name;

    /// <summary>The compiled schema for this table.</summary>
    public TableSchema Schema => _schema;

    /// <summary>Appends a batch of rows.</summary>
    public Task AddAsync(IEnumerable<T> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var rows = items.Select(i => (IReadOnlyDictionary<string, object?>)_mapper.ToRow(i)).ToList();
        return _engine.AddAsync(_schema.Name, _schema, rows, cancellationToken);
    }

    /// <summary>Appends a single row.</summary>
    public Task AddAsync(T item, CancellationToken cancellationToken = default) =>
        AddAsync(new[] { item }, cancellationToken);

    /// <summary>Starts a merge-insert (upsert) keyed by the selected column.</summary>
    public MergeInsertBuilder<T> MergeInsert(Expression<Func<T, object?>> keySelector)
    {
        var key = ResolveColumn(keySelector)
            ?? throw new NotSupportedExpressionException(
                $"MergeInsert expects a single mapped key column, e.g. x => x.Id. Got: '{keySelector.Body}'.");
        return new MergeInsertBuilder<T>(_engine, _schema, key, _mapper);
    }

    /// <summary>Starts a query with a filter predicate.</summary>
    public LanceQuery<T> Where(Expression<Func<T, bool>> predicate) => NewQuery().Where(predicate);

    /// <summary>Starts a query ordered by nearest vector distance.</summary>
    public LanceQuery<T> OrderBy(Expression<Func<T, double>> distanceSelector) => NewQuery().OrderBy(distanceSelector);

    /// <summary>Starts a nearest-neighbour search against the table's <c>[Vector]</c> column.</summary>
    public LanceQuery<T> VectorSearch(ReadOnlyMemory<float> queryVector) => NewQuery().NearestTo(queryVector);

    /// <summary>Starts a full-text (BM25) search over the given columns (or all indexed text columns).</summary>
    public LanceQuery<T> NearestToText(string text, params string[] columns) => NewQuery().NearestToText(text, columns);

    /// <summary>Creates a full-text search index over the selected text column. Returns the index name.</summary>
    public Task<string> CreateFullTextIndexAsync(Expression<Func<T, object?>> columnSelector, CancellationToken cancellationToken = default)
    {
        var column = ResolveColumn(columnSelector)
            ?? throw new NotSupportedExpressionException(
                $"CreateFullTextIndexAsync expects a single mapped column, e.g. x => x.Text. Got: '{columnSelector.Body}'.");
        return _engine.CreateIndexAsync(_schema.Name, column, new IndexSpec { Kind = "FTS" }, cancellationToken);
    }

    /// <summary>Starts a query ordered by a scalar column ascending.</summary>
    public LanceQuery<T> OrderByColumn<TKey>(Expression<Func<T, TKey>> keySelector) => NewQuery().OrderByColumn(keySelector);

    /// <summary>Starts a query ordered by a scalar column descending.</summary>
    public LanceQuery<T> OrderByColumnDescending<TKey>(Expression<Func<T, TKey>> keySelector) => NewQuery().OrderByColumnDescending(keySelector);

    /// <summary>Starts an unfiltered query (full scan).</summary>
    public LanceQuery<T> Query() => NewQuery();

    /// <summary>Streams all rows.</summary>
    public IAsyncEnumerable<T> AsAsyncEnumerable(CancellationToken cancellationToken = default) =>
        NewQuery().AsAsyncEnumerable(cancellationToken);

    /// <summary>Returns all rows as a list.</summary>
    public Task<List<T>> ToListAsync(CancellationToken cancellationToken = default) =>
        NewQuery().ToListAsync(cancellationToken);

    /// <summary>Deletes rows matching a predicate. Returns the number removed.</summary>
    public Task<int> DeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var filter = FilterTranslator.Translate(predicate, _schema);
        return _engine.DeleteAsync(_schema.Name, filter, cancellationToken);
    }

    /// <summary>Counts rows, optionally constrained by a predicate.</summary>
    public Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        string? filter = predicate is null ? null : FilterTranslator.Translate(predicate, _schema);
        return _engine.CountAsync(_schema.Name, filter, cancellationToken);
    }

    /// <summary>Builds an index over the column selected by <paramref name="columnSelector"/>. Returns the index name.</summary>
    public Task<string> CreateIndexAsync(Expression<Func<T, object?>> columnSelector, IndexSpec spec, CancellationToken cancellationToken = default)
    {
        var column = ResolveColumn(columnSelector)
            ?? throw new NotSupportedExpressionException(
                $"CreateIndexAsync expects a single mapped column, e.g. x => x.Embedding. Got: '{columnSelector.Body}'.");
        return _engine.CreateIndexAsync(_schema.Name, column, spec, cancellationToken);
    }

    /// <summary>Lists the names of indexes on this table.</summary>
    public Task<IReadOnlyList<string>> ListIndexesAsync(CancellationToken cancellationToken = default) =>
        _engine.ListIndexesAsync(_schema.Name, cancellationToken);

    /// <summary>Drops an index by name.</summary>
    public Task DropIndexAsync(string indexName, CancellationToken cancellationToken = default) =>
        _engine.DropIndexAsync(_schema.Name, indexName, cancellationToken);

    private LanceQuery<T> NewQuery() => new(_engine, _schema, _mapper);

    private string? ResolveColumn(Expression<Func<T, object?>> selector)
    {
        Expression e = selector.Body;
        while (e is UnaryExpression u && u.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
            e = u.Operand;

        if (e is MemberExpression m && m.Member is PropertyInfo prop && m.Expression == selector.Parameters[0])
            return _schema.ByPropertyName(prop.Name)?.ColumnName;

        return null;
    }
}
