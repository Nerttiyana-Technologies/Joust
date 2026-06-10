namespace Joust.Schema;

/// <summary>The compiled schema for a table, derived from a POCO type.</summary>
public sealed class TableSchema
{
    /// <summary>Creates a table schema.</summary>
    public TableSchema(string name, Type clrType, IReadOnlyList<ColumnDefinition> columns)
    {
        Name = name;
        ClrType = clrType;
        Columns = columns;
        KeyColumn = columns.FirstOrDefault(c => c.IsKey);
        VectorColumn = columns.FirstOrDefault(c => c.IsVector);
        _byColumnName = columns.ToDictionary(c => c.ColumnName, StringComparer.Ordinal);
        _byPropertyName = columns.ToDictionary(c => c.PropertyName, StringComparer.Ordinal);
    }

    private readonly Dictionary<string, ColumnDefinition> _byColumnName;
    private readonly Dictionary<string, ColumnDefinition> _byPropertyName;

    /// <summary>The table name.</summary>
    public string Name { get; }

    /// <summary>The POCO type this schema was compiled from.</summary>
    public Type ClrType { get; }

    /// <summary>All columns, in declaration order.</summary>
    public IReadOnlyList<ColumnDefinition> Columns { get; }

    /// <summary>The key column, if one is declared.</summary>
    public ColumnDefinition? KeyColumn { get; }

    /// <summary>The first vector column, if one is declared.</summary>
    public ColumnDefinition? VectorColumn { get; }

    /// <summary>Looks up a column by its physical name.</summary>
    public ColumnDefinition? ByColumnName(string name) =>
        _byColumnName.TryGetValue(name, out var c) ? c : null;

    /// <summary>Looks up a column by its CLR property name.</summary>
    public ColumnDefinition? ByPropertyName(string name) =>
        _byPropertyName.TryGetValue(name, out var c) ? c : null;
}
