namespace Joust.Interop;

/// <summary>
/// A backend-agnostic description of a read query. The LINQ engine produces this; an
/// <see cref="INativeEngine"/> implementation executes it (against native LanceDB or the
/// in-memory backend).
/// </summary>
public sealed class NativeQuery
{
    /// <summary>An optional LanceDB filter expression, e.g. <c>price &gt; 50 AND category = 'Tools'</c>.</summary>
    public string? Filter { get; set; }

    /// <summary>Optional projection: the physical column names to return. Null means all columns.</summary>
    public IReadOnlyList<string>? Select { get; set; }

    /// <summary>The query vector for nearest-neighbour search, when ordering by vector distance.</summary>
    public float[]? VectorQuery { get; set; }

    /// <summary>The physical vector column to search against, when <see cref="VectorQuery"/> is set.</summary>
    public string? VectorColumn { get; set; }

    /// <summary>The distance metric to use for vector search.</summary>
    public DistanceMetric Metric { get; set; } = DistanceMetric.Cosine;

    /// <summary>An optional scalar column to order by (ignored when a vector search is present).</summary>
    public string? OrderByColumn { get; set; }

    /// <summary>Whether scalar ordering is descending.</summary>
    public bool OrderDescending { get; set; }

    /// <summary>Maximum number of rows to return.</summary>
    public int? Limit { get; set; }

    /// <summary>Number of rows to skip.</summary>
    public int? Offset { get; set; }
}

/// <summary>Describes a vector index to build over a column.</summary>
public sealed class IndexSpec
{
    /// <summary>The index family (e.g. "IVF_PQ", "HNSW_SQ", or a scalar index name).</summary>
    public required string Kind { get; init; }

    /// <summary>The distance metric for vector indexes.</summary>
    public DistanceMetric Metric { get; init; } = DistanceMetric.Cosine;
}
