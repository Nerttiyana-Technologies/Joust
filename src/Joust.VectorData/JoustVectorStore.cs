using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Joust;
using Joust.Schema;
using Microsoft.Extensions.VectorData;

namespace Joust.VectorData;

/// <summary>
/// A <see cref="VectorStore"/> backed by Joust / LanceDB. Because the GA Vector Data extensions are
/// shared by Semantic Kernel, this is also the Semantic Kernel connector. Record types are ordinary
/// Joust POCOs annotated with <c>[Key]</c> / <c>[Vector]</c> / <c>[Column]</c>.
/// </summary>
public sealed class JoustVectorStore : VectorStore
{
    private readonly LanceDbContext _context;

    /// <summary>Creates a vector store over an open Joust context.</summary>
    public JoustVectorStore(LanceDbContext context) => _context = context;

    /// <inheritdoc />
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(
        string name, VectorStoreCollectionDefinition? definition = null)
        => new JoustVectorStoreCollection<TKey, TRecord>(_context, name);

    /// <inheritdoc />
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(
        string name, VectorStoreCollectionDefinition definition) =>
        throw new NotSupportedException("Joust does not support dynamic (dictionary) collections; use a typed POCO record.");

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default) =>
        _context.TableExistsAsync(name, cancellationToken);

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default) =>
        _context.DropTableAsync(name, cancellationToken);

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Joust does not expose a table listing on the context yet; return nothing rather than throw.
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceType.IsInstanceOfType(this) ? this : null;
    }
}

/// <summary>A <see cref="VectorStoreCollection{TKey, TRecord}"/> bound to a single Joust table.</summary>
public sealed class JoustVectorStoreCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>
    where TKey : notnull
    where TRecord : class
{
    private readonly LanceDbContext _context;
    private readonly string _name;
    private readonly TableSchema _schema;
    private readonly LanceTable<TRecord> _table;

    internal JoustVectorStoreCollection(LanceDbContext context, string name)
    {
        _context = context;
        _name = name;
        _schema = SchemaCompiler.Compile<TRecord>(name);
        _table = context.Table<TRecord>(name);
    }

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default) =>
        _context.TableExistsAsync(_name, cancellationToken);

    /// <inheritdoc />
    public override async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        if (!await _context.TableExistsAsync(_name, cancellationToken).ConfigureAwait(false))
            await _context.CreateTableAsync<TRecord>(_name, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default) =>
        _context.DropTableAsync(_name, cancellationToken);

    /// <inheritdoc />
    public override Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default) =>
        UpsertAsync(new[] { record }, cancellationToken);

    /// <inheritdoc />
    public override Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default) =>
        _table.MergeInsert(KeySelectorExpr())
              .WhenMatchedUpdateAll()
              .WhenNotMatchedInsertAll()
              .ExecuteAsync(records, cancellationToken);

    /// <inheritdoc />
    public override Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default) =>
        _table.Where(KeyEquals(key)).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public override IAsyncEnumerable<TRecord> GetAsync(
        Expression<Func<TRecord, bool>> filter, int top,
        FilteredRecordRetrievalOptions<TRecord>? options = null, CancellationToken cancellationToken = default) =>
        _table.Where(filter).Take(top).AsAsyncEnumerable(cancellationToken);

    /// <inheritdoc />
    public override Task DeleteAsync(TKey key, CancellationToken cancellationToken = default) =>
        _table.DeleteAsync(KeyEquals(key), cancellationToken);

    /// <inheritdoc />
    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(
        TInput searchValue, int top, VectorSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var vector = ToVector(searchValue);
        int skip = options?.Skip ?? 0;

        var query = _table.VectorSearch(vector).Take(top + skip);

        int index = 0;
        await foreach (var record in query.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
        {
            if (index++ < skip)
                continue;
            yield return new VectorSearchResult<TRecord>(record, score: null);
        }
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    // -- helpers ----------------------------------------------------------

    private static ReadOnlyMemory<float> ToVector<TInput>(TInput value) => value switch
    {
        ReadOnlyMemory<float> m => m,
        Memory<float> m => m,
        float[] a => a,
        _ => throw new NotSupportedException(
            $"SearchAsync supports float vectors (ReadOnlyMemory<float> or float[]); got '{typeof(TInput).Name}'. " +
            "Automatic embedding generation is not configured for this collection."),
    };

    private PropertyInfo KeyProperty() =>
        _schema.KeyColumn?.Property
        ?? throw new InvalidOperationException(
            $"Record type '{typeof(TRecord).Name}' has no [Key] property for collection '{_name}'.");

    private Expression<Func<TRecord, bool>> KeyEquals(TKey key)
    {
        var keyProp = KeyProperty();
        var p = Expression.Parameter(typeof(TRecord), "x");
        var member = Expression.Property(p, keyProp);
        var constant = Expression.Constant(key, keyProp.PropertyType);
        return Expression.Lambda<Func<TRecord, bool>>(Expression.Equal(member, constant), p);
    }

    private Expression<Func<TRecord, object?>> KeySelectorExpr()
    {
        var keyProp = KeyProperty();
        var p = Expression.Parameter(typeof(TRecord), "x");
        var access = Expression.Convert(Expression.Property(p, keyProp), typeof(object));
        return Expression.Lambda<Func<TRecord, object?>>(access, p);
    }
}
