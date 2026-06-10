using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Apache.Arrow.C;
using Joust.Schema;

namespace Joust.Interop.Native;

/// <summary>
/// The P/Invoke-backed <see cref="INativeEngine"/> that calls the native LanceDB shim (ADR-1).
/// Connection and table lifecycle are wired through <see cref="SafeConnectionHandle"/>/
/// <see cref="SafeTableHandle"/>. The row data path (add/merge/query) requires Arrow C Data
/// Interface marshalling and is the next implementation step; those members throw a descriptive
/// error until the shim and marshalling land. This type is internal because it is incomplete; the
/// in-memory backend is the supported path today.
/// </summary>
internal sealed class NativeEngine : INativeEngine
{
    private readonly SafeConnectionHandle _connection;

    internal NativeEngine(string uri, IReadOnlyDictionary<string, string>? storageOptions = null)
    {
        int rc = storageOptions is { Count: > 0 }
            ? NativeMethods.ConnectWithOptions(uri, SerializeOptions(storageOptions), out var conn)
            : NativeMethods.Connect(uri, out conn);
        Check(rc, $"connect to '{uri}'");
        _connection = new SafeConnectionHandle(conn);
    }

    public Task CreateTableAsync(TableSchema schema, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ipc = ArrowConvert.SchemaOnlyIpc(schema);
        Check(NativeMethods.TableCreateIpc(Conn, schema.Name, ipc, ipc.LongLength, out IntPtr t),
            $"create table '{schema.Name}'");
        if (t != IntPtr.Zero)
            NativeMethods.TableFree(t); // we reopen per operation; don't retain the handle
        return Task.CompletedTask;
    }

    public Task<bool> TableExistsAsync(string table, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Check(NativeMethods.TableExists(Conn, table, out int exists), $"table exists '{table}'");
        return Task.FromResult(exists != 0);
    }

    public Task<IReadOnlyList<string>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Check(NativeMethods.ListTables(Conn, out IntPtr namesPtr), "list tables");
        var csv = (namesPtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(namesPtr) : null) ?? "";
        if (namesPtr != IntPtr.Zero)
            NativeMethods.StringFree(namesPtr);
        IReadOnlyList<string> names = csv.Length == 0
            ? Array.Empty<string>()
            : csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return Task.FromResult(names);
    }

    public Task DropTableAsync(string table, CancellationToken cancellationToken = default)
    {
        Check(NativeMethods.TableDrop(Conn, table), $"drop table '{table}'");
        return Task.CompletedTask;
    }

    public Task AddAsync(string table, TableSchema schema, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (rows.Count == 0)
            return Task.CompletedTask;

        var batch = ArrowConvert.BuildBatch(schema, rows);
        using var t = OpenTable(table);
        AddBatch(t, batch);
        return Task.CompletedTask;
    }

    private static unsafe void AddBatch(SafeTableHandle table, Apache.Arrow.RecordBatch batch)
    {
        var stream = (CArrowArrayStream*)NativeMemory.AllocZeroed((nuint)sizeof(CArrowArrayStream));
        try
        {
            CArrowArrayStreamExporter.ExportArrayStream(ArrowConvert.ToArrayStream(batch.Schema, new[] { batch }), stream);
            Check(NativeMethods.TableAddStream(table.DangerousGetHandle(), (IntPtr)stream), "add (stream)");
        }
        finally
        {
            NativeMemory.Free(stream);
        }
    }

    public Task<MergeInsertResult> MergeInsertAsync(string table, TableSchema schema, string keyColumn, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, MergeInsertOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var batch = ArrowConvert.BuildBatch(schema, rows);
        var ipc = ArrowConvert.WriteIpc(batch.Schema, new[] { batch });
        using var t = OpenTable(table);
        Check(NativeMethods.TableMergeInsertIpc(
            t.DangerousGetHandle(), keyColumn, ipc, ipc.LongLength,
            options.WhenMatchedUpdateAll ? 1 : 0,
            options.WhenNotMatchedInsertAll ? 1 : 0,
            options.WhenNotMatchedBySourceDelete ? 1 : 0,
            out long inserted, out long updated, out long deleted), $"merge-insert into '{table}'");
        return Task.FromResult(new MergeInsertResult((int)inserted, (int)updated, (int)deleted));
    }

    public Task<int> DeleteAsync(string table, string filter, CancellationToken cancellationToken = default)
    {
        using var t = OpenTable(table);
        Check(NativeMethods.TableDelete(t.DangerousGetHandle(), filter, out long deleted), $"delete from '{table}'");
        return Task.FromResult((int)deleted);
    }

    public Task<long> CountAsync(string table, string? filter, CancellationToken cancellationToken = default)
    {
        using var t = OpenTable(table);
        Check(NativeMethods.TableCount(t.DangerousGetHandle(), filter, out long count), $"count '{table}'");
        return Task.FromResult(count);
    }

    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> QueryAsync(
        string table, NativeQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // LanceDB applies the filter, vector ordering and (when no client-side reshaping is needed)
        // the limit. Scalar ordering and offset are applied here over the materialized result, since
        // the native query API does not expose them directly.
        int nativeLimit;
        if (query.OrderByColumn is not null)
            nativeLimit = -1; // need the full filtered set to sort correctly
        else if (query.Offset is { } off && off > 0)
            nativeLimit = query.Limit is { } lim ? off + lim : -1;
        else
            nativeLimit = query.Limit ?? -1;

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        using (var t = OpenTable(table))
            rows = RunQueryStream(t, query, nativeLimit);

        IEnumerable<IReadOnlyDictionary<string, object?>> seq = rows;
        if (query.OrderByColumn is { } orderCol)
        {
            seq = query.OrderDescending
                ? rows.OrderByDescending(r => GetField(r, orderCol), ValueComparer.Instance)
                : rows.OrderBy(r => GetField(r, orderCol), ValueComparer.Instance);
        }
        if (query.Offset is { } skip && skip > 0)
            seq = seq.Skip(skip);
        if (query.Limit is { } take && (query.OrderByColumn is not null || (query.Offset is { } o && o > 0)))
            seq = seq.Take(take);

        foreach (var row in seq)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static object? GetField(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var v) ? v : null;

    private sealed class ValueComparer : IComparer<object?>
    {
        public static readonly ValueComparer Instance = new();

        public int Compare(object? a, object? b)
        {
            if (a is null && b is null) return 0;
            if (a is null) return -1;
            if (b is null) return 1;

            if (a is IComparable ca && a.GetType() == b.GetType())
                return ca.CompareTo(b);

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

    private static unsafe List<IReadOnlyDictionary<string, object?>> RunQueryStream(SafeTableHandle table, NativeQuery query, int nativeLimit)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var stream = (CArrowArrayStream*)NativeMemory.AllocZeroed((nuint)sizeof(CArrowArrayStream));
        try
        {
            Check(NativeMethods.TableQueryStream(
                table.DangerousGetHandle(),
                query.Filter,
                query.Select is null ? null : string.Join(",", query.Select),
                query.VectorQuery, query.VectorQuery?.Length ?? 0,
                query.VectorColumn, (int)query.Metric,
                query.FtsText,
                query.FtsColumns is null ? null : string.Join(",", query.FtsColumns),
                query.OrderByColumn, query.OrderDescending ? 1 : 0,
                nativeLimit, 0,
                (IntPtr)stream), "query (stream)");

            using var reader = CArrowArrayStreamImporter.ImportArrayStream(stream);
            while (reader.ReadNextRecordBatchAsync().AsTask().GetAwaiter().GetResult() is { } batch)
            {
                rows.AddRange(ArrowConvert.RowsFromBatch(batch));
                batch.Dispose();
            }
        }
        finally
        {
            NativeMemory.Free(stream);
        }
        return rows;
    }

    public Task<string> CreateIndexAsync(string table, string column, IndexSpec spec, CancellationToken cancellationToken = default)
    {
        using var t = OpenTable(table);
        Check(NativeMethods.IndexCreate(t.DangerousGetHandle(), column, spec.Kind, (int)spec.Metric, out IntPtr namePtr),
            $"create index on '{table}.{column}'");
        var name = (namePtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(namePtr) : null) ?? $"{column}_idx";
        if (namePtr != IntPtr.Zero)
            NativeMethods.StringFree(namePtr);
        return Task.FromResult(name);
    }

    public Task<IReadOnlyList<string>> ListIndexesAsync(string table, CancellationToken cancellationToken = default)
    {
        using var t = OpenTable(table);
        Check(NativeMethods.IndexList(t.DangerousGetHandle(), out IntPtr namesPtr), $"list indexes on '{table}'");
        var csv = (namesPtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(namesPtr) : null) ?? "";
        if (namesPtr != IntPtr.Zero)
            NativeMethods.StringFree(namesPtr);
        IReadOnlyList<string> names = csv.Length == 0
            ? Array.Empty<string>()
            : csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return Task.FromResult(names);
    }

    public Task DropIndexAsync(string table, string indexName, CancellationToken cancellationToken = default)
    {
        using var t = OpenTable(table);
        Check(NativeMethods.IndexDrop(t.DangerousGetHandle(), indexName), $"drop index '{indexName}' on '{table}'");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }

    private IntPtr Conn => _connection.DangerousGetHandle();

    private SafeTableHandle OpenTable(string name)
    {
        Check(NativeMethods.TableOpen(Conn, name, out IntPtr t), $"open table '{name}'");
        return new SafeTableHandle(t);
    }

    private static void Check(int rc, string what)
    {
        if (rc != 0)
            throw new NativeInteropException(
                $"Native call failed ({what}): {NativeMethods.LastError() ?? "unknown error"} [code {rc}].");
    }

    private static NotImplementedException PendingArrow(string op) =>
        new($"{op} on the native backend requires Arrow C Data Interface marshalling, which is the next " +
            "implementation step. The in-memory backend supports this operation today.");

    private static string SerializeOptions(IReadOnlyDictionary<string, string> opts) =>
        "{" + string.Join(",", opts.Select(kv => $"\"{Escape(kv.Key)}\":\"{Escape(kv.Value)}\"")) + "}";

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
