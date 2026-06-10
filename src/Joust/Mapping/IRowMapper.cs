namespace Joust.Mapping;

/// <summary>
/// Maps between a POCO and the column/value dictionary exchanged with the engine. The default
/// implementation uses cached compiled delegates; a source generator can supply an AOT-friendly
/// implementation registered via <see cref="MapperRegistry"/>.
/// </summary>
public interface IRowMapper<T> where T : class
{
    /// <summary>Projects a POCO into a column-name → value dictionary.</summary>
    Dictionary<string, object?> ToRow(T item);

    /// <summary>Materializes a POCO from a column-name → value dictionary.</summary>
    T FromRow(IReadOnlyDictionary<string, object?> row);
}
