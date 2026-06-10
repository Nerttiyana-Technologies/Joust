using System.Reflection;

namespace Joust.Schema;

/// <summary>Describes a single physical column derived from a POCO property.</summary>
public sealed class ColumnDefinition
{
    /// <summary>The CLR property this column maps to.</summary>
    public required PropertyInfo Property { get; init; }

    /// <summary>The CLR property name.</summary>
    public required string PropertyName { get; init; }

    /// <summary>The physical column name (from <see cref="ColumnAttribute"/> or snake_case of the property).</summary>
    public required string ColumnName { get; init; }

    /// <summary>The CLR type of the property.</summary>
    public required Type ClrType { get; init; }

    /// <summary>True if this column is the table key.</summary>
    public bool IsKey { get; init; }

    /// <summary>True if this column holds an embedding vector.</summary>
    public bool IsVector { get; init; }

    /// <summary>The vector dimensionality, when <see cref="IsVector"/> is true.</summary>
    public int VectorDimensions { get; init; }

    /// <summary>The distance metric for this vector, when <see cref="IsVector"/> is true.</summary>
    public DistanceMetric Metric { get; init; }

    /// <summary>The secondary index requested for this column, if any.</summary>
    public IndexType? Index { get; init; }
}
