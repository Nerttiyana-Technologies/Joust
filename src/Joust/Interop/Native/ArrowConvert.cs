using System.Globalization;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using Joust.Schema;
using ArrowSchema = Apache.Arrow.Schema;

namespace Joust.Interop.Native;

/// <summary>
/// Converts between Joust's row dictionaries and Apache Arrow, and serializes to/from the Arrow IPC
/// stream format used to cross the native boundary. Supported column types: Int32, Int64, Boolean,
/// Float, Double, String (also used for Guid/DateTime), and FixedSizeList&lt;Float&gt; for vectors.
/// </summary>
internal static class ArrowConvert
{
    // -- schema -----------------------------------------------------------

    public static ArrowSchema BuildSchema(TableSchema schema)
    {
        var fields = new List<Field>(schema.Columns.Count);
        foreach (var col in schema.Columns)
        {
            IArrowType type = col.IsVector
                ? new FixedSizeListType(new Field("item", FloatType.Default, nullable: false), col.VectorDimensions)
                : MapType(col.ClrType);
            fields.Add(new Field(col.ColumnName, type, nullable: true));
        }
        return new ArrowSchema(fields, metadata: null);
    }

    private static IArrowType MapType(Type clr)
    {
        var t = Nullable.GetUnderlyingType(clr) ?? clr;
        if (t == typeof(int) || t == typeof(short) || t.IsEnum) return Int32Type.Default;
        if (t == typeof(long)) return Int64Type.Default;
        if (t == typeof(bool)) return BooleanType.Default;
        if (t == typeof(float)) return FloatType.Default;
        if (t == typeof(double)) return DoubleType.Default;
        // string, Guid, DateTime and anything else are stored as UTF-8 strings.
        return StringType.Default;
    }

    // -- build record batch ----------------------------------------------

    public static RecordBatch BuildBatch(TableSchema schema, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var arrowSchema = BuildSchema(schema);
        var arrays = new List<IArrowArray>(schema.Columns.Count);
        foreach (var col in schema.Columns)
            arrays.Add(BuildColumn(col, rows));
        return new RecordBatch(arrowSchema, arrays, rows.Count);
    }

    private static IArrowArray BuildColumn(ColumnDefinition col, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        if (col.IsVector)
            return BuildVector(col, rows);

        var t = Nullable.GetUnderlyingType(col.ClrType) ?? col.ClrType;

        if (t == typeof(int) || t == typeof(short) || t.IsEnum)
        {
            var b = new Int32Array.Builder();
            foreach (var row in rows) Append(b, Get(row, col), v => Convert.ToInt32(v, CultureInfo.InvariantCulture));
            return b.Build();
        }
        if (t == typeof(long))
        {
            var b = new Int64Array.Builder();
            foreach (var row in rows) Append(b, Get(row, col), v => Convert.ToInt64(v, CultureInfo.InvariantCulture));
            return b.Build();
        }
        if (t == typeof(bool))
        {
            var b = new BooleanArray.Builder();
            foreach (var row in rows) Append(b, Get(row, col), v => Convert.ToBoolean(v, CultureInfo.InvariantCulture));
            return b.Build();
        }
        if (t == typeof(float))
        {
            var b = new FloatArray.Builder();
            foreach (var row in rows) Append(b, Get(row, col), v => Convert.ToSingle(v, CultureInfo.InvariantCulture));
            return b.Build();
        }
        if (t == typeof(double))
        {
            var b = new DoubleArray.Builder();
            foreach (var row in rows) Append(b, Get(row, col), v => Convert.ToDouble(v, CultureInfo.InvariantCulture));
            return b.Build();
        }

        // string / Guid / DateTime / fallback
        var sb = new StringArray.Builder();
        foreach (var row in rows)
        {
            var v = Get(row, col);
            if (v is null) sb.AppendNull();
            else sb.Append(ToStringValue(v));
        }
        return sb.Build();
    }

    private static FixedSizeListArray BuildVector(ColumnDefinition col, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        int dim = col.VectorDimensions;
        var values = new FloatArray.Builder();
        var validity = new ArrowBuffer.BitmapBuilder();
        int nulls = 0;

        foreach (var row in rows)
        {
            var floats = AsFloats(Get(row, col));
            if (floats is null)
            {
                validity.Append(false);
                nulls++;
                for (int i = 0; i < dim; i++) values.Append(0f);
            }
            else
            {
                validity.Append(true);
                for (int i = 0; i < dim; i++) values.Append(i < floats.Length ? floats[i] : 0f);
            }
        }

        var valueArray = values.Build();
        var type = new FixedSizeListType(new Field("item", FloatType.Default, nullable: false), dim);
        var data = new ArrayData(
            type,
            length: rows.Count,
            nullCount: nulls,
            offset: 0,
            buffers: new[] { validity.Build() },
            children: new[] { valueArray.Data });
        return new FixedSizeListArray(data);
    }

    private static void Append(Int32Array.Builder b, object? v, Func<object, int> conv)
    { if (v is null) b.AppendNull(); else b.Append(conv(v)); }
    private static void Append(Int64Array.Builder b, object? v, Func<object, long> conv)
    { if (v is null) b.AppendNull(); else b.Append(conv(v)); }
    private static void Append(BooleanArray.Builder b, object? v, Func<object, bool> conv)
    { if (v is null) b.AppendNull(); else b.Append(conv(v)); }
    private static void Append(FloatArray.Builder b, object? v, Func<object, float> conv)
    { if (v is null) b.AppendNull(); else b.Append(conv(v)); }
    private static void Append(DoubleArray.Builder b, object? v, Func<object, double> conv)
    { if (v is null) b.AppendNull(); else b.Append(conv(v)); }

    // -- IPC --------------------------------------------------------------

    public static byte[] WriteIpc(ArrowSchema schema, IReadOnlyList<RecordBatch> batches)
    {
        using var ms = new MemoryStream();
        using (var writer = new ArrowStreamWriter(ms, schema))
        {
            writer.WriteStart();
            foreach (var batch in batches)
                writer.WriteRecordBatch(batch);
            writer.WriteEnd();
        }
        return ms.ToArray();
    }

    public static byte[] SchemaOnlyIpc(TableSchema schema) =>
        WriteIpc(BuildSchema(schema), System.Array.Empty<RecordBatch>());

    public static IEnumerable<IReadOnlyDictionary<string, object?>> ReadIpcRows(ReadOnlySpan<byte> data)
    {
        // Copy out of the span so we can hand a stream to the reader.
        var bytes = data.ToArray();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        using var ms = new MemoryStream(bytes);
        using var reader = new ArrowStreamReader(ms);
        while (reader.ReadNextRecordBatch() is { } batch)
        {
            var schema = batch.Schema;
            for (int r = 0; r < batch.Length; r++)
            {
                var dict = new Dictionary<string, object?>(batch.ColumnCount, StringComparer.Ordinal);
                for (int c = 0; c < batch.ColumnCount; c++)
                {
                    var field = schema.GetFieldByIndex(c);
                    dict[field.Name] = ReadValue(batch.Column(c), r);
                }
                rows.Add(dict);
            }
        }
        return rows;
    }

    private static object? ReadValue(IArrowArray array, int i)
    {
        if (array.IsNull(i))
            return null;

        return array switch
        {
            Int32Array a => a.GetValue(i),
            Int64Array a => a.GetValue(i),
            BooleanArray a => a.GetValue(i),
            FloatArray a => a.GetValue(i),
            DoubleArray a => a.GetValue(i),
            StringArray a => a.GetString(i),
            FixedSizeListArray a => ReadVector(a, i),
            _ => null,
        };
    }

    private static float[] ReadVector(FixedSizeListArray array, int i)
    {
        int dim = ((FixedSizeListType)array.Data.DataType).ListSize;
        var values = (FloatArray)array.Values;
        int start = i * dim;
        var result = new float[dim];
        for (int j = 0; j < dim; j++)
            result[j] = values.GetValue(start + j) ?? 0f;
        return result;
    }

    // -- helpers ----------------------------------------------------------

    private static object? Get(IReadOnlyDictionary<string, object?> row, ColumnDefinition col) =>
        row.TryGetValue(col.ColumnName, out var v) ? v : null;

    private static string ToStringValue(object v) => v switch
    {
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
        Guid g => g.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    private static float[]? AsFloats(object? v) => v switch
    {
        null => null,
        float[] fa => fa,
        IEnumerable<float> ef => ef.ToArray(),
        IEnumerable<double> ed => ed.Select(d => (float)d).ToArray(),
        _ => null,
    };
}
