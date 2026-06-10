using System.Runtime.InteropServices;

namespace Joust.Interop.Native;

/// <summary>
/// The raw P/Invoke surface bound to the <c>joust_lancedb</c> C ABI (see
/// <c>native/joust_lancedb.h</c>). All access goes through typed wrappers; nothing outside this
/// namespace touches these declarations. Native loading is handled by <see cref="JoustNativeResolver"/>.
/// </summary>
internal static class NativeMethods
{
    /// <summary>The logical library name; the resolver maps it to the per-RID binary.</summary>
    public const string LibraryName = "joust_lancedb";

    // Install the native-library resolver before any P/Invoke in this type runs.
    static NativeMethods() => JoustNativeResolver.Install();

    /// <summary>Returns the last error message for the current thread, or null.</summary>
    public static string? LastError()
    {
        var ptr = joust_last_error();
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
    }

    [DllImport(LibraryName, EntryPoint = "joust_last_error")]
    private static extern IntPtr joust_last_error();

    [DllImport(LibraryName, EntryPoint = "joust_connect", CharSet = CharSet.Ansi)]
    public static extern int Connect([MarshalAs(UnmanagedType.LPUTF8Str)] string uri, out IntPtr connection);

    [DllImport(LibraryName, EntryPoint = "joust_connect_with_options", CharSet = CharSet.Ansi)]
    public static extern int ConnectWithOptions(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string uri,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string jsonStorageOptions,
        out IntPtr connection);

    [DllImport(LibraryName, EntryPoint = "joust_connection_free")]
    public static extern void ConnectionFree(IntPtr connection);

    [DllImport(LibraryName, EntryPoint = "joust_table_exists", CharSet = CharSet.Ansi)]
    public static extern int TableExists(IntPtr connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out int exists);

    [DllImport(LibraryName, EntryPoint = "joust_table_open", CharSet = CharSet.Ansi)]
    public static extern int TableOpen(IntPtr connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out IntPtr table);

    [DllImport(LibraryName, EntryPoint = "joust_table_drop", CharSet = CharSet.Ansi)]
    public static extern int TableDrop(IntPtr connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibraryName, EntryPoint = "joust_table_free")]
    public static extern void TableFree(IntPtr table);

    [DllImport(LibraryName, EntryPoint = "joust_table_count", CharSet = CharSet.Ansi)]
    public static extern int TableCount(IntPtr table, [MarshalAs(UnmanagedType.LPUTF8Str)] string? filter, out long count);

    [DllImport(LibraryName, EntryPoint = "joust_table_delete", CharSet = CharSet.Ansi)]
    public static extern int TableDelete(IntPtr table, [MarshalAs(UnmanagedType.LPUTF8Str)] string filter, out long deleted);

    // -- Arrow IPC data path --------------------------------------------------

    [DllImport(LibraryName, EntryPoint = "joust_table_create_ipc", CharSet = CharSet.Ansi)]
    public static extern int TableCreateIpc(IntPtr connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        byte[] ipcSchema, long ipcLen, out IntPtr table);

    [DllImport(LibraryName, EntryPoint = "joust_table_add_ipc")]
    public static extern int TableAddIpc(IntPtr table, byte[] ipcData, long ipcLen);

    [DllImport(LibraryName, EntryPoint = "joust_table_query_ipc", CharSet = CharSet.Ansi)]
    public static extern int TableQueryIpc(IntPtr table,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? filter,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? selectCsv,
        float[]? vector, int vectorLen,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? vectorColumn, int metric,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? orderByColumn, int orderDescending,
        int limit, int offset,
        out IntPtr outIpc, out long outLen);

    [DllImport(LibraryName, EntryPoint = "joust_buffer_free")]
    public static extern void BufferFree(IntPtr data, long len);

    [DllImport(LibraryName, EntryPoint = "joust_table_merge_insert_ipc", CharSet = CharSet.Ansi)]
    public static extern int TableMergeInsertIpc(IntPtr table, [MarshalAs(UnmanagedType.LPUTF8Str)] string keyColumn,
        byte[] ipcData, long ipcLen, int whenMatchedUpdateAll, int whenNotMatchedInsertAll, int whenNotMatchedBySourceDelete,
        out long inserted, out long updated, out long deleted);

    // -- index management -----------------------------------------------------

    [DllImport(LibraryName, EntryPoint = "joust_index_create", CharSet = CharSet.Ansi)]
    public static extern int IndexCreate(IntPtr table, [MarshalAs(UnmanagedType.LPUTF8Str)] string column,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string kind, int metric, out IntPtr outName);

    [DllImport(LibraryName, EntryPoint = "joust_index_list")]
    public static extern int IndexList(IntPtr table, out IntPtr outNames);

    [DllImport(LibraryName, EntryPoint = "joust_index_drop", CharSet = CharSet.Ansi)]
    public static extern int IndexDrop(IntPtr table, [MarshalAs(UnmanagedType.LPUTF8Str)] string indexName);

    [DllImport(LibraryName, EntryPoint = "joust_string_free")]
    public static extern void StringFree(IntPtr s);
}
