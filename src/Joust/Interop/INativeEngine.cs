using Joust.Schema;

namespace Joust.Interop;

/// <summary>
/// The single boundary between Joust's managed layers and a storage engine. The production
/// implementation calls native LanceDB via P/Invoke; <see cref="InMemoryEngine"/> provides a
/// pure-managed backend for development and testing. The public API depends only on this
/// interface, so the backend is swappable without changing application code.
/// </summary>
public interface INativeEngine : IAsyncDisposable
{
    /// <summary>Creates a table from a compiled schema. No-op if it already exists with a matching shape.</summary>
    Task CreateTableAsync(TableSchema schema, CancellationToken cancellationToken = default);

    /// <summary>Returns true if a table with the given name exists.</summary>
    Task<bool> TableExistsAsync(string table, CancellationToken cancellationToken = default);

    /// <summary>Lists the names of all tables in the database.</summary>
    Task<IReadOnlyList<string>> ListTablesAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops a table and all its data.</summary>
    Task DropTableAsync(string table, CancellationToken cancellationToken = default);

    /// <summary>Appends rows to a table. The schema describes column types for backends that need them.</summary>
    Task AddAsync(string table, TableSchema schema, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, CancellationToken cancellationToken = default);

    /// <summary>Deletes rows matching the filter. Returns the number of rows removed.</summary>
    Task<int> DeleteAsync(string table, string filter, CancellationToken cancellationToken = default);

    /// <summary>Counts rows, optionally constrained by a filter.</summary>
    Task<long> CountAsync(string table, string? filter, CancellationToken cancellationToken = default);

    /// <summary>Merges (upserts) rows keyed by <paramref name="keyColumn"/>. Returns insert/update/delete counts.</summary>
    Task<MergeInsertResult> MergeInsertAsync(string table, TableSchema schema, string keyColumn, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, MergeInsertOptions options, CancellationToken cancellationToken = default);

    /// <summary>Executes a query, streaming result rows as column → value dictionaries.</summary>
    IAsyncEnumerable<IReadOnlyDictionary<string, object?>> QueryAsync(string table, NativeQuery query, CancellationToken cancellationToken = default);

    /// <summary>Builds an index over a column. Returns the created index name.</summary>
    Task<string> CreateIndexAsync(string table, string column, IndexSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Lists the names of indexes on a table.</summary>
    Task<IReadOnlyList<string>> ListIndexesAsync(string table, CancellationToken cancellationToken = default);

    /// <summary>Drops an index by name.</summary>
    Task DropIndexAsync(string table, string indexName, CancellationToken cancellationToken = default);
}
