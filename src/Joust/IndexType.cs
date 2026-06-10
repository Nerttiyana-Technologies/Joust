namespace Joust;

/// <summary>
/// The kind of secondary index to build over a scalar or text column.
/// Vector indexes (IVF/HNSW families) are configured separately at index-creation time.
/// </summary>
public enum IndexType
{
    /// <summary>A scalar index (engine chooses an appropriate default, typically B-Tree).</summary>
    Scalar = 0,

    /// <summary>B-Tree index — good for high-cardinality, range-queried columns.</summary>
    BTree = 1,

    /// <summary>Bitmap index — good for low-cardinality categorical columns.</summary>
    Bitmap = 2,

    /// <summary>Label-list index — good for multi-valued tag columns.</summary>
    LabelList = 3,

    /// <summary>Full-text search index over a string column.</summary>
    FullText = 4,
}
