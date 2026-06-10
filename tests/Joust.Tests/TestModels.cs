namespace Joust.Tests;

/// <summary>A representative POCO used across the test suite.</summary>
public sealed class Product
{
    [Key]
    public int Id { get; set; }

    public bool IsActive { get; set; }

    public double Price { get; set; }

    [LanceIndex(IndexType.Scalar)]
    public string Category { get; set; } = "";

    [Column("body")]
    public string Text { get; set; } = "";

    [Vector(Dimensions = 4, Metric = DistanceMetric.Cosine)]
    public float[] Embedding { get; set; } = [];
}
