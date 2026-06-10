using System.Collections;
using System.Runtime.CompilerServices;
using Joust.Schema;

namespace Joust.Interop.InMemory;

/// <summary>
/// A pure-managed <see cref="INativeEngine"/> used for development and tests. It stores rows in
/// memory and honours the same filter strings the native engine would, so application code and the
/// LINQ engine behave identically against it. It is not a persistence engine and is not optimized
/// for large datasets.
/// </summary>
public sealed class InMemoryEngine : INativeEngine
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Table> _tables = new(StringComparer.Ordinal);

    private sealed class Table
    {
        public required TableSchema Schema { get; init; }
        public List<Dictionary<string, object?>> Rows { get; } = new();
        public HashSet<string> Indexes { get; } = new(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public Task CreateTableAsync(TableSchema schema, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_tables.ContainsKey(schema.Name))
                _tables[schema.Name] = new Table { Schema = schema };
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> TableExistsAsync(string table, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult(_tables.ContainsKey(table));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<string>>(
                _tables.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
    }

    /// <inheritdoc />
    public Task DropTableAsync(string table, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _tables.Remove(table);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AddAsync(string table, TableSchema schema, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var t = Require(table);
            foreach (var row in rows)
                t.Rows.Add(new Dictionary<string, object?>(row, StringComparer.Ordinal));
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> DeleteAsync(string table, string filter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predicate = FilterEvaluator.Compile(filter);
        lock (_gate)
        {
            var t = Require(table);
            int removed = t.Rows.RemoveAll(r => predicate(r));
            return Task.FromResult(removed);
        }
    }

    /// <inheritdoc />
    public Task<long> CountAsync(string table, string? filter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predicate = FilterEvaluator.Compile(filter);
        lock (_gate)
        {
            var t = Require(table);
            long count = t.Rows.Count(r => predicate(r));
            return Task.FromResult(count);
        }
    }

    /// <inheritdoc />
    public Task<MergeInsertResult> MergeInsertAsync(string table, TableSchema schema, string keyColumn,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, MergeInsertOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int inserted = 0, updated = 0, deleted = 0;

        lock (_gate)
        {
            var t = Require(table);

            // Index existing rows by key value.
            var byKey = new Dictionary<object, int>();
            for (int i = 0; i < t.Rows.Count; i++)
            {
                var k = t.Rows[i].TryGetValue(keyColumn, out var kv) ? kv : null;
                if (k is not null)
                    byKey[k] = i;
            }

            var sourceKeys = new HashSet<object>();
            foreach (var src in rows)
            {
                var key = src.TryGetValue(keyColumn, out var kv) ? kv : null;
                if (key is null)
                    throw new JoustException($"Merge-insert source row is missing key column '{keyColumn}'.");
                sourceKeys.Add(key);

                if (byKey.TryGetValue(key, out var idx))
                {
                    if (options.WhenMatchedUpdateAll)
                    {
                        t.Rows[idx] = new Dictionary<string, object?>(src, StringComparer.Ordinal);
                        updated++;
                    }
                }
                else if (options.WhenNotMatchedInsertAll)
                {
                    t.Rows.Add(new Dictionary<string, object?>(src, StringComparer.Ordinal));
                    inserted++;
                }
            }

            if (options.WhenNotMatchedBySourceDelete)
            {
                deleted = t.Rows.RemoveAll(r =>
                {
                    var k = r.TryGetValue(keyColumn, out var kv) ? kv : null;
                    return k is not null && !sourceKeys.Contains(k);
                });
            }
        }

        return Task.FromResult(new MergeInsertResult(inserted, updated, deleted));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> QueryAsync(
        string table, NativeQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<Dictionary<string, object?>> snapshot;
        lock (_gate)
        {
            var t = Require(table);
            snapshot = t.Rows.Select(r => new Dictionary<string, object?>(r, StringComparer.Ordinal)).ToList();
        }

        IEnumerable<Dictionary<string, object?>> result = snapshot;

        if (!string.IsNullOrWhiteSpace(query.Filter))
        {
            var predicate = FilterEvaluator.Compile(query.Filter);
            result = result.Where(r => predicate(r));
        }

        if (query.VectorQuery is { Length: > 0 } q && query.VectorColumn is { } vc)
        {
            result = result
                .Select(r =>
                {
                    var d = Distance(r, vc, q, query.Metric);
                    r["_distance"] = (float)d; // surface the distance like LanceDB does
                    return (row: r, dist: d);
                })
                .OrderBy(x => x.dist)
                .Select(x => x.row);
        }
        else if (!string.IsNullOrWhiteSpace(query.FtsText))
        {
            var terms = Tokenize(query.FtsText!);
            result = result
                .Select(r => (row: r, score: FtsScore(r, query.FtsColumns, terms)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Select(x => x.row);
        }
        else if (query.OrderByColumn is { } orderCol)
        {
            var comparer = new RowValueComparer(orderCol);
            result = query.OrderDescending
                ? result.OrderByDescending(r => r, comparer)
                : result.OrderBy(r => r, comparer);
        }

        if (query.Offset is { } off && off > 0)
            result = result.Skip(off);

        if (query.Limit is { } lim)
            result = result.Take(lim);

        foreach (var row in result)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Project(row, query.Select);
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<string> CreateIndexAsync(string table, string column, IndexSpec spec, CancellationToken cancellationToken = default)
    {
        var name = $"{column}_idx";
        lock (_gate)
            Require(table).Indexes.Add(name); // indexing is a structural no-op in memory
        return Task.FromResult(name);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListIndexesAsync(string table, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<string>>(Require(table).Indexes.OrderBy(n => n, StringComparer.Ordinal).ToList());
    }

    /// <inheritdoc />
    public Task DropIndexAsync(string table, string indexName, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            Require(table).Indexes.Remove(indexName);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate)
            _tables.Clear();
        return ValueTask.CompletedTask;
    }

    private Table Require(string table) =>
        _tables.TryGetValue(table, out var t)
            ? t
            : throw new JoustException($"Table '{table}' does not exist.");

    private static IReadOnlyDictionary<string, object?> Project(
        IReadOnlyDictionary<string, object?> row, IReadOnlyList<string>? select)
    {
        if (select is null || select.Count == 0)
            return row;

        var projected = new Dictionary<string, object?>(select.Count, StringComparer.Ordinal);
        foreach (var col in select)
            if (row.TryGetValue(col, out var v))
                projected[col] = v;
        return projected;
    }

    private static double Distance(IReadOnlyDictionary<string, object?> row, string column, float[] query, DistanceMetric metric)
    {
        if (!row.TryGetValue(column, out var raw) || raw is null)
            return double.MaxValue;

        var vec = ToFloatList(raw);
        if (vec is null || vec.Count != query.Length)
            return double.MaxValue;

        return VectorMath.Distance(vec, query, metric);
    }

    private static readonly char[] FtsSeparators = { ' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '"', '\'', '(', ')', '-', '/' };

    private static List<string> Tokenize(string text) =>
        text.Split(FtsSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToList();

    private static int FtsScore(IReadOnlyDictionary<string, object?> row, IReadOnlyList<string>? columns, IReadOnlyList<string> terms)
    {
        var cols = columns is { Count: > 0 }
            ? (IEnumerable<string>)columns
            : row.Where(kv => kv.Value is string).Select(kv => kv.Key);

        int score = 0;
        foreach (var col in cols)
        {
            if (!row.TryGetValue(col, out var v) || v is not string s)
                continue;
            var words = Tokenize(s);
            foreach (var term in terms)
                score += words.Count(w => w == term || w.Contains(term, StringComparison.Ordinal));
        }
        return score;
    }

    private sealed class RowValueComparer(string column) : IComparer<Dictionary<string, object?>>
    {
        public int Compare(Dictionary<string, object?>? x, Dictionary<string, object?>? y)
        {
            var a = x is not null && x.TryGetValue(column, out var av) ? av : null;
            var b = y is not null && y.TryGetValue(column, out var bv) ? bv : null;

            if (a is null && b is null) return 0;
            if (a is null) return -1;
            if (b is null) return 1;

            if (a is IComparable ca && a.GetType() == b.GetType())
                return ca.CompareTo(b);

            // Fall back to numeric, then ordinal string comparison.
            try
            {
                double da = Convert.ToDouble(a, System.Globalization.CultureInfo.InvariantCulture);
                double db = Convert.ToDouble(b, System.Globalization.CultureInfo.InvariantCulture);
                return da.CompareTo(db);
            }
            catch
            {
                return string.CompareOrdinal(a.ToString(), b.ToString());
            }
        }
    }

    private static IReadOnlyList<float>? ToFloatList(object value) => value switch
    {
        float[] fa => fa,
        IReadOnlyList<float> rl => rl,
        IEnumerable<float> ef => ef.ToArray(),
        IEnumerable<double> ed => ed.Select(d => (float)d).ToArray(),
        IEnumerable e => e.Cast<object>().Select(o => Convert.ToSingle(o, System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
        _ => null,
    };
}
