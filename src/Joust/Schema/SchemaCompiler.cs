using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace Joust.Schema;

/// <summary>
/// Compiles a POCO type into a <see cref="TableSchema"/> by reflecting over its public
/// instance properties and interpreting Joust attributes. Results are cached per (type, name).
/// </summary>
public static class SchemaCompiler
{
    private static readonly ConcurrentDictionary<(Type, string), TableSchema> Cache = new();

    /// <summary>Compiles (or returns a cached) schema for <typeparamref name="T"/> with the given table name.</summary>
    public static TableSchema Compile<T>(string tableName) => Compile(typeof(T), tableName);

    /// <summary>Compiles (or returns a cached) schema for <paramref name="clrType"/> with the given table name.</summary>
    public static TableSchema Compile(Type clrType, string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new SchemaValidationException("Table name must be non-empty.");

        return Cache.GetOrAdd((clrType, tableName), static key => Build(key.Item1, key.Item2));
    }

    private static TableSchema Build(Type clrType, string tableName)
    {
        var props = clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                           .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                           .ToArray();

        if (props.Length == 0)
            throw new SchemaValidationException(
                $"Type '{clrType.Name}' exposes no readable public properties to map.");

        var columns = new List<ColumnDefinition>(props.Length);
        var usedColumnNames = new HashSet<string>(StringComparer.Ordinal);
        int keyCount = 0;

        foreach (var p in props)
        {
            var columnAttr = p.GetCustomAttribute<ColumnAttribute>();
            var vectorAttr = p.GetCustomAttribute<VectorAttribute>();
            var indexAttr = p.GetCustomAttribute<LanceIndexAttribute>();
            bool isKey = p.GetCustomAttribute<KeyAttribute>() is not null;

            string columnName = columnAttr?.Name ?? Naming.ToSnakeCase(p.Name);
            if (!usedColumnNames.Add(columnName))
                throw new SchemaValidationException(
                    $"Duplicate column name '{columnName}' on type '{clrType.Name}'. " +
                    "Use [Column(\"...\")] to disambiguate.");

            if (isKey) keyCount++;

            int dims = 0;
            var metric = DistanceMetric.Cosine;
            bool isVector = vectorAttr is not null;
            if (isVector)
            {
                dims = vectorAttr!.Dimensions;
                metric = vectorAttr.Metric;

                if (dims <= 0)
                    throw new SchemaValidationException(
                        $"Vector property '{clrType.Name}.{p.Name}' must declare positive Dimensions (got {dims}).");

                if (!IsFloatSequence(p.PropertyType))
                    throw new SchemaValidationException(
                        $"Vector property '{clrType.Name}.{p.Name}' must be a float sequence " +
                        $"(float[] or IReadOnlyList<float>), but was '{p.PropertyType.Name}'.");
            }

            columns.Add(new ColumnDefinition
            {
                Property = p,
                PropertyName = p.Name,
                ColumnName = columnName,
                ClrType = p.PropertyType,
                IsKey = isKey,
                IsVector = isVector,
                VectorDimensions = dims,
                Metric = metric,
                Index = indexAttr?.Type,
            });
        }

        if (keyCount > 1)
            throw new SchemaValidationException(
                $"Type '{clrType.Name}' declares {keyCount} [Key] properties; exactly one is allowed.");

        return new TableSchema(tableName, clrType, columns);
    }

    private static bool IsFloatSequence(Type t)
    {
        if (t == typeof(float[]))
            return true;

        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if ((def == typeof(IReadOnlyList<>) || def == typeof(IList<>) || def == typeof(List<>) ||
                 def == typeof(IEnumerable<>)) && t.GetGenericArguments()[0] == typeof(float))
                return true;
        }

        // Any IEnumerable whose element type is float.
        if (typeof(IEnumerable).IsAssignableFrom(t))
        {
            var elem = t.GetInterfaces()
                        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                        .Select(i => i.GetGenericArguments()[0])
                        .FirstOrDefault();
            if (elem == typeof(float))
                return true;
        }

        return false;
    }
}
