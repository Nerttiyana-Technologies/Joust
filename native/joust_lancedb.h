/*
 * joust_lancedb.h — C ABI contract for the Joust native LanceDB shim.
 *
 * This header defines the narrow, stable C ABI that the managed Joust.Interop.Native
 * layer binds to via P/Invoke. The shim is a thin Rust crate that wraps the `lancedb`
 * crate and exposes these functions with `#[no_mangle] extern "C"`. The managed side
 * never sees Rust types — only opaque handles, UTF-8 strings, and Arrow C Data Interface
 * structures.
 *
 * Conventions:
 *   - Every fallible call returns int32 status: 0 = OK, non-zero = error.
 *     On error, call joust_last_error() for a UTF-8, null-terminated message
 *     (owned by the library, valid until the next call on the same thread).
 *   - Handles are opaque pointers owned by the caller; free them with the matching
 *     *_free function. The managed side wraps each in a SafeHandle.
 *   - Strings passed in are UTF-8, null-terminated. Strings returned are owned by the
 *     library unless documented otherwise.
 *   - Row data crosses the boundary via the Arrow C Data Interface (ArrowArrayStream),
 *     so no row-by-row marshalling is required.
 */

#ifndef JOUST_LANCEDB_H
#define JOUST_LANCEDB_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Opaque handles. */
typedef void* joust_connection;
typedef void* joust_table;
typedef void* joust_stream;   /* wraps an ArrowArrayStream producer */

/* Returns the last error message on the current thread, or NULL if none. */
const char* joust_last_error(void);

/* Connection lifecycle. */
int32_t joust_connect(const char* uri, joust_connection* out_connection);
int32_t joust_connect_with_options(const char* uri, const char* json_storage_options, joust_connection* out_connection);
void    joust_connection_free(joust_connection connection);

/* Table lifecycle. */
int32_t joust_table_exists(joust_connection connection, const char* name, int32_t* out_exists);
int32_t joust_list_tables(joust_connection connection, char** out_names /* newline-separated; free with joust_string_free */);
int32_t joust_table_open(joust_connection connection, const char* name, joust_table* out_table);
int32_t joust_table_create(joust_connection connection, const char* name, const void* arrow_schema /* ArrowSchema* */, joust_table* out_table);
int32_t joust_table_drop(joust_connection connection, const char* name);
void    joust_table_free(joust_table table);

/*
 * Data operations. Rows cross the boundary as Arrow IPC stream bytes (the IPC "stream" format),
 * which avoids manual C Data Interface struct marshalling. The managed side encodes/decodes with
 * Apache.Arrow; the native side uses arrow::ipc. (Zero-copy via the Arrow C Data Interface is a
 * future optimization.)
 */

/* Create a table from an IPC payload carrying the schema (and optionally initial batches). */
int32_t joust_table_create_ipc(joust_connection connection, const char* name,
                               const uint8_t* ipc_schema, int64_t ipc_len, joust_table* out_table);

/*
 * Append rows via the Arrow C Stream interface (zero-copy). arrow_array_stream is an
 * ArrowArrayStream* producer the library consumes (and releases).
 */
int32_t joust_table_add_stream(joust_table table, void* arrow_array_stream);

int32_t joust_table_delete(joust_table table, const char* filter, int64_t* out_deleted);
int32_t joust_table_count(joust_table table, const char* filter /* nullable */, int64_t* out_count);

/*
 * Execute a query. The result set is exported into the caller-provided ArrowArrayStream
 * (out_arrow_array_stream), which the caller imports and releases.
 */
int32_t joust_table_query_stream(joust_table table, const char* filter /* nullable */,
                                 const char* select_csv /* nullable */,
                                 const float* vector /* nullable */, int32_t vector_len,
                                 const char* vector_column /* nullable */, int32_t metric,
                                 const char* fts_text /* nullable */, const char* fts_columns /* nullable, csv */,
                                 const char* order_by_column /* nullable */, int32_t order_descending,
                                 int32_t limit /* -1 = none */, int32_t offset /* 0 = none */,
                                 void* out_arrow_array_stream);

/* Merge-insert (upsert) rows supplied as an IPC payload, keyed by key_column. Flags are 0/1. */
int32_t joust_table_merge_insert_ipc(joust_table table, const char* key_column,
                                     const uint8_t* ipc_data, int64_t ipc_len,
                                     int32_t when_matched_update_all,
                                     int32_t when_not_matched_insert_all,
                                     int32_t when_not_matched_by_source_delete,
                                     int64_t* out_inserted, int64_t* out_updated, int64_t* out_deleted);

/* Index management. Strings returned via out_* are owned by the library; free with joust_string_free. */
int32_t joust_index_create(joust_table table, const char* column, const char* kind, int32_t metric, char** out_name);
int32_t joust_index_list(joust_table table, char** out_names /* newline-separated */);
int32_t joust_index_drop(joust_table table, const char* index_name);
void    joust_string_free(char* s);

#ifdef __cplusplus
}
#endif

#endif /* JOUST_LANCEDB_H */
