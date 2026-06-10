using Joust.Interop;
using Joust.Mapping;
using Joust.Schema;

namespace Joust;

/// <summary>
/// Fluent builder for a merge-insert (upsert). Configure which actions to take on matched and
/// unmatched rows, then call <see cref="ExecuteAsync"/> with the source rows.
/// <code>
/// await table.MergeInsert(x =&gt; x.Id)
///            .WhenMatchedUpdateAll()
///            .WhenNotMatchedInsertAll()
///            .ExecuteAsync(rows);
/// </code>
/// </summary>
public sealed class MergeInsertBuilder<T> where T : class
{
    private readonly INativeEngine _engine;
    private readonly TableSchema _schema;
    private readonly string _keyColumn;
    private readonly PocoMapper<T> _mapper;
    private readonly MergeInsertOptions _options = new();

    internal MergeInsertBuilder(INativeEngine engine, TableSchema schema, string keyColumn, PocoMapper<T> mapper)
    {
        _engine = engine;
        _schema = schema;
        _keyColumn = keyColumn;
        _mapper = mapper;
    }

    /// <summary>Replace existing rows whose key matches a source row.</summary>
    public MergeInsertBuilder<T> WhenMatchedUpdateAll()
    {
        _options.WhenMatchedUpdateAll = true;
        return this;
    }

    /// <summary>Insert source rows that have no matching existing row.</summary>
    public MergeInsertBuilder<T> WhenNotMatchedInsertAll()
    {
        _options.WhenNotMatchedInsertAll = true;
        return this;
    }

    /// <summary>Delete existing rows whose key is not present in the source.</summary>
    public MergeInsertBuilder<T> WhenNotMatchedBySourceDelete()
    {
        _options.WhenNotMatchedBySourceDelete = true;
        return this;
    }

    /// <summary>Executes the merge against the supplied source rows.</summary>
    public Task<MergeInsertResult> ExecuteAsync(IEnumerable<T> source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var rows = source.Select(i => (IReadOnlyDictionary<string, object?>)_mapper.ToRow(i)).ToList();
        return _engine.MergeInsertAsync(_schema.Name, _schema, _keyColumn, rows, _options, cancellationToken);
    }
}
