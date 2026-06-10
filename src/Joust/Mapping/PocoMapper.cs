using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Joust.Schema;

namespace Joust.Mapping;

/// <summary>
/// Translates between POCO instances of <typeparamref name="T"/> and the column/value dictionaries
/// exchanged with the engine.
/// </summary>
/// <remarks>
/// Property access uses delegates compiled once per type (cached), avoiding per-call reflection
/// (PERF-2). If a source-generated <see cref="IRowMapper{T}"/> is registered in
/// <see cref="MapperRegistry"/>, it is used instead — the fully AOT-friendly path.
/// </remarks>
public sealed class PocoMapper<T> where T : class
{
    private static readonly Func<T> Factory = BuildFactory();
    private static readonly ConcurrentDictionary<PropertyInfo, Func<T, object?>> Getters = new();
    private static readonly ConcurrentDictionary<PropertyInfo, Action<T, object?>> Setters = new();

    private readonly TableSchema _schema;
    private readonly IRowMapper<T>? _generated;

    /// <summary>Creates a mapper bound to a compiled schema.</summary>
    public PocoMapper(TableSchema schema)
    {
        _schema = schema;
        _generated = MapperRegistry.Get<T>();
    }

    /// <summary>Projects a POCO into a column-name → value dictionary for writing.</summary>
    public Dictionary<string, object?> ToRow(T item)
    {
        if (_generated is not null)
            return _generated.ToRow(item);

        var row = new Dictionary<string, object?>(_schema.Columns.Count, StringComparer.Ordinal);
        foreach (var col in _schema.Columns)
            row[col.ColumnName] = GetGetter(col.Property)(item);
        return row;
    }

    /// <summary>Materializes a POCO from a column-name → value dictionary read from the engine.</summary>
    public T FromRow(IReadOnlyDictionary<string, object?> row)
    {
        if (_generated is not null)
            return _generated.FromRow(row);

        var item = Factory();
        foreach (var col in _schema.Columns)
        {
            if (!row.TryGetValue(col.ColumnName, out var value) || value is null)
                continue;

            var converted = MapConvert.ConvertTo(value, col.ClrType);
            if (converted is not null)
                GetSetter(col.Property)(item, converted);
        }
        return item;
    }

    // -- compiled accessors ----------------------------------------------

    private static Func<T, object?> GetGetter(PropertyInfo prop) => Getters.GetOrAdd(prop, static p =>
    {
        var obj = Expression.Parameter(typeof(T), "x");
        Expression access = Expression.Property(obj, p);
        var body = Expression.Convert(access, typeof(object));
        return Expression.Lambda<Func<T, object?>>(body, obj).Compile();
    });

    private static Action<T, object?> GetSetter(PropertyInfo prop) => Setters.GetOrAdd(prop, static p =>
    {
        var obj = Expression.Parameter(typeof(T), "x");
        var value = Expression.Parameter(typeof(object), "v");
        var assign = Expression.Assign(
            Expression.Property(obj, p),
            Expression.Convert(value, p.PropertyType));
        return Expression.Lambda<Action<T, object?>>(assign, obj, value).Compile();
    });

    private static Func<T> BuildFactory()
    {
        if (typeof(T).GetConstructor(Type.EmptyTypes) is not null)
            return Expression.Lambda<Func<T>>(Expression.New(typeof(T))).Compile();
        return static () => (T)Activator.CreateInstance(typeof(T))!;
    }
}
