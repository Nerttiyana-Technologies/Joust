namespace Joust;

/// <summary>
/// Marks a property as a vector (embedding) column and declares its dimensionality and
/// distance metric. Supports both positional and named forms:
/// <code>
/// [Vector(1536)]
/// [Vector(Dimensions = 1536, Metric = DistanceMetric.Cosine)]
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class VectorAttribute : Attribute
{
    /// <summary>Creates the attribute, optionally specifying the dimension count positionally.</summary>
    /// <param name="dimensions">The fixed length of the embedding vector. Must be greater than zero.</param>
    public VectorAttribute(int dimensions = 0) => Dimensions = dimensions;

    /// <summary>The fixed length of the embedding vector.</summary>
    public int Dimensions { get; set; }

    /// <summary>The distance metric used to compare this vector. Defaults to <see cref="DistanceMetric.Cosine"/>.</summary>
    public DistanceMetric Metric { get; set; } = DistanceMetric.Cosine;
}
