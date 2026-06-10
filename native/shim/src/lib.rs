//! Native C ABI shim bridging Joust (.NET) to the LanceDB Rust engine.
//!
//! This implements the lifecycle subset of `native/joust_lancedb.h`: connect, table
//! exists/open/drop/free, count, and delete. The Arrow data path (add/merge/query) is the next
//! step and is intentionally not exported yet — the managed `NativeEngine` throws a clear error for
//! those until they land.
//!
//! Design notes:
//!   * All exported functions are `extern "C"` with `#[no_mangle]` so the symbol names match the
//!     header and the .NET `[DllImport]` entry points.
//!   * Async LanceDB calls are driven on a single process-wide Tokio runtime via `block_on`.
//!   * Handles are `Box::into_raw` pointers owned by the caller; the matching `*_free` reclaims them.
//!   * Fallible calls return `0` on success and non-zero on error; the message is retrievable via
//!     `joust_last_error()` (thread-local, valid until the next call on that thread).

use std::cell::RefCell;
use std::ffi::{c_char, c_void, CStr, CString};
use std::io::Cursor;
use std::ptr;
use std::slice;
use std::sync::OnceLock;

use arrow::array::{RecordBatch, RecordBatchIterator, RecordBatchReader};
use arrow::datatypes::SchemaRef;
use arrow::ffi_stream::{ArrowArrayStreamReader, FFI_ArrowArrayStream};
use arrow::ipc::reader::StreamReader;
use futures::TryStreamExt;
use lance_index::scalar::FullTextSearchQuery;
use lancedb::index::scalar::FtsIndexBuilder;
use lancedb::index::Index;
use lancedb::query::{ExecutableQuery, QueryBase, Select};
use lancedb::{Connection, DistanceType, Table};
use tokio::runtime::Runtime;

// ---------------------------------------------------------------------------
// Runtime & error plumbing
// ---------------------------------------------------------------------------

fn runtime() -> &'static Runtime {
    static RT: OnceLock<Runtime> = OnceLock::new();
    RT.get_or_init(|| {
        Runtime::new().expect("failed to create Tokio runtime")
    })
}

thread_local! {
    static LAST_ERROR: RefCell<Option<CString>> = RefCell::new(None);
}

fn set_error(msg: impl Into<String>) {
    let c = CString::new(msg.into()).unwrap_or_else(|_| CString::new("error").unwrap());
    LAST_ERROR.with(|e| *e.borrow_mut() = Some(c));
}

fn clear_error() {
    LAST_ERROR.with(|e| *e.borrow_mut() = None);
}

/// Returns the last error message for the current thread, or NULL.
#[no_mangle]
pub extern "C" fn joust_last_error() -> *const c_char {
    LAST_ERROR.with(|e| match &*e.borrow() {
        Some(s) => s.as_ptr(),
        None => ptr::null(),
    })
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/// Converts a borrowed C string to a Rust &str, or records an error and returns None.
unsafe fn cstr<'a>(p: *const c_char) -> Option<&'a str> {
    if p.is_null() {
        set_error("null string argument");
        return None;
    }
    match CStr::from_ptr(p).to_str() {
        Ok(s) => Some(s),
        Err(_) => {
            set_error("invalid UTF-8 in string argument");
            None
        }
    }
}

/// Optional C string (may be null) -> Option<String>.
unsafe fn opt_cstr(p: *const c_char) -> Option<String> {
    if p.is_null() {
        None
    } else {
        CStr::from_ptr(p).to_str().ok().map(|s| s.to_owned())
    }
}

const OK: i32 = 0;
const ERR: i32 = 1;

fn fail(msg: impl Into<String>) -> i32 {
    set_error(msg);
    ERR
}

// ---------------------------------------------------------------------------
// Connection
// ---------------------------------------------------------------------------

#[no_mangle]
pub unsafe extern "C" fn joust_connect(uri: *const c_char, out_connection: *mut *mut c_void) -> i32 {
    clear_error();
    let Some(uri) = cstr(uri) else { return ERR };
    if out_connection.is_null() {
        return fail("out_connection is null");
    }

    match runtime().block_on(async { lancedb::connect(uri).execute().await }) {
        Ok(conn) => {
            *out_connection = Box::into_raw(Box::new(conn)) as *mut c_void;
            OK
        }
        Err(e) => fail(format!("connect failed: {e}")),
    }
}

#[no_mangle]
pub unsafe extern "C" fn joust_connect_with_options(
    uri: *const c_char,
    _json_storage_options: *const c_char,
    out_connection: *mut *mut c_void,
) -> i32 {
    // Storage options wiring (cloud credentials, etc.) is a follow-up; for now behave like connect.
    joust_connect(uri, out_connection)
}

#[no_mangle]
pub unsafe extern "C" fn joust_connection_free(connection: *mut c_void) {
    if !connection.is_null() {
        drop(Box::from_raw(connection as *mut Connection));
    }
}

fn conn_ref<'a>(p: *mut c_void) -> Option<&'a Connection> {
    if p.is_null() {
        set_error("null connection handle");
        None
    } else {
        Some(unsafe { &*(p as *const Connection) })
    }
}

fn table_ref<'a>(p: *mut c_void) -> Option<&'a Table> {
    if p.is_null() {
        set_error("null table handle");
        None
    } else {
        Some(unsafe { &*(p as *const Table) })
    }
}

// ---------------------------------------------------------------------------
// Table lifecycle
// ---------------------------------------------------------------------------

#[no_mangle]
pub unsafe extern "C" fn joust_table_exists(
    connection: *mut c_void,
    name: *const c_char,
    out_exists: *mut i32,
) -> i32 {
    clear_error();
    let (Some(conn), Some(name)) = (conn_ref(connection), cstr(name)) else { return ERR };
    if out_exists.is_null() {
        return fail("out_exists is null");
    }

    match runtime().block_on(async { conn.table_names().execute().await }) {
        Ok(names) => {
            *out_exists = if names.iter().any(|n| n == name) { 1 } else { 0 };
            OK
        }
        Err(e) => fail(format!("table_names failed: {e}")),
    }
}

#[no_mangle]
pub unsafe extern "C" fn joust_list_tables(connection: *mut c_void, out_names: *mut *mut c_char) -> i32 {
    clear_error();
    let Some(conn) = conn_ref(connection) else { return ERR };

    match runtime().block_on(async { conn.table_names().execute().await }) {
        Ok(names) => {
            if !out_names.is_null() {
                *out_names = to_cstring_ptr(names.join("\n"));
            }
            OK
        }
        Err(e) => fail(format!("table_names failed: {e}")),
    }
}

#[no_mangle]
pub unsafe extern "C" fn joust_table_open(
    connection: *mut c_void,
    name: *const c_char,
    out_table: *mut *mut c_void,
) -> i32 {
    clear_error();
    let (Some(conn), Some(name)) = (conn_ref(connection), cstr(name)) else { return ERR };
    if out_table.is_null() {
        return fail("out_table is null");
    }

    match runtime().block_on(async { conn.open_table(name).execute().await }) {
        Ok(table) => {
            *out_table = Box::into_raw(Box::new(table)) as *mut c_void;
            OK
        }
        Err(e) => fail(format!("open_table '{name}' failed: {e}")),
    }
}

#[no_mangle]
pub unsafe extern "C" fn joust_table_drop(connection: *mut c_void, name: *const c_char) -> i32 {
    clear_error();
    let (Some(conn), Some(name)) = (conn_ref(connection), cstr(name)) else { return ERR };

    match runtime().block_on(async { conn.drop_table(name).await }) {
        Ok(_) => OK,
        Err(e) => fail(format!("drop_table '{name}' failed: {e}")),
    }
}

#[no_mangle]
pub unsafe extern "C" fn joust_table_free(table: *mut c_void) {
    if !table.is_null() {
        drop(Box::from_raw(table as *mut Table));
    }
}

// ---------------------------------------------------------------------------
// Data operations (count / delete)
// ---------------------------------------------------------------------------

#[no_mangle]
pub unsafe extern "C" fn joust_table_count(
    table: *mut c_void,
    filter: *const c_char,
    out_count: *mut i64,
) -> i32 {
    clear_error();
    let Some(table) = table_ref(table) else { return ERR };
    if out_count.is_null() {
        return fail("out_count is null");
    }
    let filter = opt_cstr(filter);

    match runtime().block_on(async { table.count_rows(filter).await }) {
        Ok(n) => {
            *out_count = n as i64;
            OK
        }
        Err(e) => fail(format!("count_rows failed: {e}")),
    }
}

#[no_mangle]
pub unsafe extern "C" fn joust_table_delete(
    table: *mut c_void,
    filter: *const c_char,
    out_deleted: *mut i64,
) -> i32 {
    clear_error();
    let (Some(table), Some(filter)) = (table_ref(table), cstr(filter)) else { return ERR };
    if out_deleted.is_null() {
        return fail("out_deleted is null");
    }

    let result = runtime().block_on(async {
        // LanceDB's delete does not return a count, so measure before/after.
        let before = table.count_rows(Some(filter.to_owned())).await?;
        table.delete(filter).await?;
        Ok::<usize, lancedb::Error>(before)
    });

    match result {
        Ok(deleted) => {
            *out_deleted = deleted as i64;
            OK
        }
        Err(e) => fail(format!("delete failed: {e}")),
    }
}

// ---------------------------------------------------------------------------
// Arrow IPC data path (create / add / query)
// ---------------------------------------------------------------------------

fn map_metric(metric: i32) -> DistanceType {
    match metric {
        1 => DistanceType::L2,
        2 => DistanceType::Dot,
        _ => DistanceType::Cosine,
    }
}

fn read_ipc(bytes: &[u8]) -> Result<(SchemaRef, Vec<RecordBatch>), String> {
    let reader = StreamReader::try_new(Cursor::new(bytes), None)
        .map_err(|e| format!("read ipc: {e}"))?;
    let schema = reader.schema();
    let batches = reader
        .collect::<Result<Vec<_>, _>>()
        .map_err(|e| format!("read ipc batches: {e}"))?;
    Ok((schema, batches))
}

#[no_mangle]
pub unsafe extern "C" fn joust_table_create_ipc(
    connection: *mut c_void,
    name: *const c_char,
    ipc_schema: *const u8,
    ipc_len: i64,
    out_table: *mut *mut c_void,
) -> i32 {
    clear_error();
    let (Some(conn), Some(name)) = (conn_ref(connection), cstr(name)) else { return ERR };
    if out_table.is_null() {
        return fail("out_table is null");
    }

    let bytes = slice::from_raw_parts(ipc_schema, ipc_len as usize);
    let (schema, _batches) = match read_ipc(bytes) {
        Ok(v) => v,
        Err(e) => return fail(e),
    };

    match runtime().block_on(async { conn.create_empty_table(name, schema).execute().await }) {
        Ok(table) => {
            *out_table = Box::into_raw(Box::new(table)) as *mut c_void;
            OK
        }
        Err(e) => fail(format!("create_empty_table '{name}' failed: {e}")),
    }
}

#[no_mangle]
pub unsafe extern "C" fn joust_table_add_stream(
    table: *mut c_void,
    arrow_array_stream: *mut c_void,
) -> i32 {
    clear_error();
    let Some(table) = table_ref(table) else { return ERR };
    if arrow_array_stream.is_null() {
        return fail("null arrow_array_stream");
    }

    // Import the C stream and drain it into owned batches (Send-safe for table.add).
    let reader = match ArrowArrayStreamReader::from_raw(arrow_array_stream as *mut FFI_ArrowArrayStream) {
        Ok(r) => r,
        Err(e) => return fail(format!("import stream: {e}")),
    };
    let schema = reader.schema();
    let batches: Vec<RecordBatch> = match reader.collect::<Result<Vec<_>, _>>() {
        Ok(b) => b,
        Err(e) => return fail(format!("read stream: {e}")),
    };

    let owned: Box<dyn RecordBatchReader + Send> =
        Box::new(RecordBatchIterator::new(batches.into_iter().map(Ok), schema));

    match runtime().block_on(async { table.add(owned).execute().await }) {
        Ok(_) => OK,
        Err(e) => fail(format!("add failed: {e}")),
    }
}

#[no_mangle]
pub unsafe extern "C" fn joust_table_query_stream(
    table: *mut c_void,
    filter: *const c_char,
    select_csv: *const c_char,
    vector: *const f32,
    vector_len: i32,
    vector_column: *const c_char,
    metric: i32,
    fts_text: *const c_char,
    fts_columns: *const c_char,
    _order_by_column: *const c_char,
    _order_descending: i32,
    limit: i32,
    _offset: i32,
    out_stream: *mut c_void,
) -> i32 {
    clear_error();
    let Some(table) = table_ref(table) else { return ERR };
    if out_stream.is_null() {
        return fail("null out_stream");
    }

    let filter = opt_cstr(filter);
    let select = opt_cstr(select_csv);
    let vcol = opt_cstr(vector_column);
    let fts = opt_cstr(fts_text);
    let fts_cols = opt_cstr(fts_columns);
    let query_vector: Option<Vec<f32>> = if !vector.is_null() && vector_len > 0 {
        Some(slice::from_raw_parts(vector, vector_len as usize).to_vec())
    } else {
        None
    };

    // NOTE: scalar order_by and offset are not yet applied on the native path; vector search is
    // ordered by distance, and limit is honoured. Those are easy follow-ups.
    let result: Result<(SchemaRef, Vec<RecordBatch>), lancedb::Error> = runtime().block_on(async {
        let mut base = table.query();
        if let Some(f) = &filter {
            base = base.only_if(f.clone());
        }
        if let Some(s) = &select {
            let cols: Vec<String> = s.split(',').map(|c| c.trim().to_string()).collect();
            base = base.select(Select::columns(&cols));
        }

        let batches = if let Some(qv) = &query_vector {
            let mut vq = base.nearest_to(qv.as_slice())?;
            if let Some(c) = &vcol {
                vq = vq.column(c.as_str());
            }
            vq = vq.distance_type(map_metric(metric));
            if limit >= 0 {
                vq = vq.limit(limit as usize);
            }
            vq.execute().await?.try_collect::<Vec<_>>().await?
        } else if let Some(text) = &fts {
            // Column targeting is honoured by the in-memory backend; the native path searches all
            // FTS-indexed columns (LanceDB's default). fts_cols is intentionally unused here.
            let _ = &fts_cols;
            let fts_query = FullTextSearchQuery::new(text.clone());
            base = base.full_text_search(fts_query);
            if limit >= 0 {
                base = base.limit(limit as usize);
            }
            base.execute().await?.try_collect::<Vec<_>>().await?
        } else {
            if limit >= 0 {
                base = base.limit(limit as usize);
            }
            base.execute().await?.try_collect::<Vec<_>>().await?
        };

        let schema = table.schema().await?;
        Ok((schema, batches))
    });

    let (full_schema, batches) = match result {
        Ok(v) => v,
        Err(e) => return fail(format!("query failed: {e}")),
    };

    // Prefer the schema of the returned data (handles column projection); fall back to table schema.
    let schema: SchemaRef = batches.first().map(|b| b.schema()).unwrap_or(full_schema);

    // Export the result as a zero-copy Arrow C stream into the caller-provided struct.
    let reader: Box<dyn RecordBatchReader + Send> =
        Box::new(RecordBatchIterator::new(batches.into_iter().map(Ok), schema));
    let ffi = FFI_ArrowArrayStream::new(reader);
    std::ptr::write(out_stream as *mut FFI_ArrowArrayStream, ffi);
    OK
}

#[no_mangle]
pub unsafe extern "C" fn joust_buffer_free(data: *mut u8, len: i64) {
    if !data.is_null() && len > 0 {
        let slice = std::slice::from_raw_parts_mut(data, len as usize);
        drop(Box::from_raw(slice as *mut [u8]));
    }
}

// ---------------------------------------------------------------------------
// Merge-insert (upsert)
// ---------------------------------------------------------------------------

#[no_mangle]
pub unsafe extern "C" fn joust_table_merge_insert_ipc(
    table: *mut c_void,
    key_column: *const c_char,
    ipc_data: *const u8,
    ipc_len: i64,
    when_matched_update_all: i32,
    when_not_matched_insert_all: i32,
    when_not_matched_by_source_delete: i32,
    out_inserted: *mut i64,
    out_updated: *mut i64,
    out_deleted: *mut i64,
) -> i32 {
    clear_error();
    let (Some(table), Some(key)) = (table_ref(table), cstr(key_column)) else { return ERR };
    if out_inserted.is_null() || out_updated.is_null() || out_deleted.is_null() {
        return fail("null out parameter");
    }

    let bytes = slice::from_raw_parts(ipc_data, ipc_len as usize);
    let (schema, batches) = match read_ipc(bytes) {
        Ok(v) => v,
        Err(e) => return fail(e),
    };
    let reader: Box<dyn RecordBatchReader + Send> =
        Box::new(RecordBatchIterator::new(batches.into_iter().map(Ok), schema));

    let res = runtime().block_on(async {
        let mut b = table.merge_insert(&[key]);
        if when_matched_update_all != 0 {
            b.when_matched_update_all(None);
        }
        if when_not_matched_insert_all != 0 {
            b.when_not_matched_insert_all();
        }
        if when_not_matched_by_source_delete != 0 {
            b.when_not_matched_by_source_delete(None);
        }
        b.execute(reader).await
    });

    match res {
        Ok(stats) => {
            *out_inserted = stats.num_inserted_rows as i64;
            *out_updated = stats.num_updated_rows as i64;
            *out_deleted = stats.num_deleted_rows as i64;
            OK
        }
        Err(e) => fail(format!("merge_insert failed: {e}")),
    }
}

// ---------------------------------------------------------------------------
// Index management
// ---------------------------------------------------------------------------

fn to_cstring_ptr(s: String) -> *mut c_char {
    match CString::new(s) {
        Ok(c) => c.into_raw(),
        Err(_) => ptr::null_mut(),
    }
}

#[no_mangle]
pub unsafe extern "C" fn joust_index_create(
    table: *mut c_void,
    column: *const c_char,
    kind: *const c_char,
    _metric: i32,
    out_name: *mut *mut c_char,
) -> i32 {
    clear_error();
    let (Some(table), Some(column)) = (table_ref(table), cstr(column)) else { return ERR };

    let kind_upper = opt_cstr(kind).unwrap_or_default().to_uppercase();
    let index = if kind_upper == "FTS" || kind_upper == "FULLTEXT" {
        Index::FTS(FtsIndexBuilder::default())
    } else {
        Index::Auto // LanceDB picks a scalar/vector index based on the column type
    };

    let res = runtime().block_on(async {
        table.create_index(&[column], index).execute().await?;
        let indices = table.list_indices().await?;
        let name = indices
            .into_iter()
            .rev()
            .find(|ix| ix.columns.iter().any(|c| c == column))
            .map(|ix| ix.name);
        Ok::<Option<String>, lancedb::Error>(name)
    });

    match res {
        Ok(name) => {
            if !out_name.is_null() {
                let n = name.unwrap_or_else(|| format!("{column}_idx"));
                *out_name = to_cstring_ptr(n);
            }
            OK
        }
        Err(e) => fail(format!("create_index failed: {e}")),
    }
}

#[no_mangle]
pub unsafe extern "C" fn joust_index_list(table: *mut c_void, out_names: *mut *mut c_char) -> i32 {
    clear_error();
    let Some(table) = table_ref(table) else { return ERR };

    match runtime().block_on(async { table.list_indices().await }) {
        Ok(indices) => {
            let joined = indices
                .iter()
                .map(|ix| ix.name.clone())
                .collect::<Vec<_>>()
                .join("\n");
            if !out_names.is_null() {
                *out_names = to_cstring_ptr(joined);
            }
            OK
        }
        Err(e) => fail(format!("list_indices failed: {e}")),
    }
}

#[no_mangle]
pub unsafe extern "C" fn joust_index_drop(table: *mut c_void, index_name: *const c_char) -> i32 {
    clear_error();
    let (Some(table), Some(name)) = (table_ref(table), cstr(index_name)) else { return ERR };

    match runtime().block_on(async { table.drop_index(name).await }) {
        Ok(_) => OK,
        Err(e) => fail(format!("drop_index failed: {e}")),
    }
}

#[no_mangle]
pub unsafe extern "C" fn joust_string_free(s: *mut c_char) {
    if !s.is_null() {
        drop(CString::from_raw(s));
    }
}
