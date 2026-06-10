namespace Joust;

/// <summary>
/// The distance function used to compare vectors during search and indexing.
/// </summary>
public enum DistanceMetric
{
    /// <summary>Cosine distance (1 - cosine similarity). Smaller is closer.</summary>
    Cosine = 0,

    /// <summary>Euclidean (L2) distance. Smaller is closer.</summary>
    Euclidean = 1,

    /// <summary>Negative dot product. Smaller is closer.</summary>
    Dot = 2,
}
