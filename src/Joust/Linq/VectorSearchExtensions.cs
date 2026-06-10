namespace Joust;

/// <summary>
/// Vector-distance helpers used inside <c>OrderBy</c> selectors to express nearest-neighbour
/// search, e.g. <c>OrderBy(x =&gt; x.Embedding.CosineDistance(query))</c>. The LINQ engine
/// recognises these calls and pushes them down to the engine's vector index. They also compute
/// a real distance when invoked directly, which the in-memory backend uses for ordering.
/// </summary>
public static class VectorSearchExtensions
{
    /// <summary>Cosine distance between an embedding column and a query vector.</summary>
    public static double CosineDistance(this float[] vector, float[] query) =>
        VectorMath.CosineDistance(vector, query);

    /// <summary>Euclidean (L2) distance between an embedding column and a query vector.</summary>
    public static double EuclideanDistance(this float[] vector, float[] query) =>
        VectorMath.EuclideanDistance(vector, query);

    /// <summary>Negative dot product (so that smaller is closer) between an embedding column and a query vector.</summary>
    public static double DotDistance(this float[] vector, float[] query) =>
        -VectorMath.DotProduct(vector, query);
}
