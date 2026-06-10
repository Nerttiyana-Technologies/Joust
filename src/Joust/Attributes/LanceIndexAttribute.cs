namespace Joust;

/// <summary>
/// Declares that a secondary index should be created over the annotated column.
/// <code>
/// [LanceIndex(IndexType.Scalar)]
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class LanceIndexAttribute : Attribute
{
    /// <summary>Creates the attribute with the given index type.</summary>
    public LanceIndexAttribute(IndexType type = IndexType.Scalar) => Type = type;

    /// <summary>The kind of index to build.</summary>
    public IndexType Type { get; }
}
