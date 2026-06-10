namespace Joust;

/// <summary>Vector distance functions shared by the query surface and the in-memory backend.</summary>
public static class VectorMath
{
    /// <summary>Computes the distance between two equal-length vectors for the given metric.</summary>
    public static double Distance(IReadOnlyList<float> a, IReadOnlyList<float> b, DistanceMetric metric) => metric switch
    {
        DistanceMetric.Cosine => CosineDistance(a, b),
        DistanceMetric.Euclidean => EuclideanDistance(a, b),
        DistanceMetric.Dot => -DotProduct(a, b),
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unknown distance metric."),
    };

    /// <summary>Cosine distance: 1 - cosine similarity. Smaller is closer.</summary>
    public static double CosineDistance(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        EnsureSameLength(a, b);
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Count; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        if (na == 0 || nb == 0)
            return 1.0;
        return 1.0 - dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>Euclidean (L2) distance. Smaller is closer.</summary>
    public static double EuclideanDistance(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        EnsureSameLength(a, b);
        double sum = 0;
        for (int i = 0; i < a.Count; i++)
        {
            double d = (double)a[i] - b[i];
            sum += d * d;
        }
        return Math.Sqrt(sum);
    }

    /// <summary>Dot product of two vectors.</summary>
    public static double DotProduct(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        EnsureSameLength(a, b);
        double sum = 0;
        for (int i = 0; i < a.Count; i++)
            sum += (double)a[i] * b[i];
        return sum;
    }

    private static void EnsureSameLength(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a.Count != b.Count)
            throw new ArgumentException($"Vectors must have equal length ({a.Count} vs {b.Count}).");
    }
}
