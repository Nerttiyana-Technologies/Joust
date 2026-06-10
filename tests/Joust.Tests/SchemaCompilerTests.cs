using Joust.Schema;
using Xunit;

namespace Joust.Tests;

public class SchemaCompilerTests
{
    [Fact]
    public void Compiles_key_vector_and_columns()
    {
        var schema = SchemaCompiler.Compile<Product>("products");

        Assert.Equal("products", schema.Name);
        Assert.NotNull(schema.KeyColumn);
        Assert.Equal("id", schema.KeyColumn!.ColumnName);

        Assert.NotNull(schema.VectorColumn);
        Assert.Equal("embedding", schema.VectorColumn!.ColumnName);
        Assert.Equal(4, schema.VectorColumn.VectorDimensions);
        Assert.Equal(DistanceMetric.Cosine, schema.VectorColumn.Metric);
    }

    [Fact]
    public void Column_attribute_overrides_name()
    {
        var schema = SchemaCompiler.Compile<Product>("products");
        Assert.Equal("body", schema.ByPropertyName("Text")!.ColumnName);
    }

    [Fact]
    public void Default_names_are_snake_case()
    {
        var schema = SchemaCompiler.Compile<Product>("products");
        Assert.Equal("is_active", schema.ByPropertyName("IsActive")!.ColumnName);
    }

    [Fact]
    public void Index_attribute_is_captured()
    {
        var schema = SchemaCompiler.Compile<Product>("products");
        Assert.Equal(IndexType.Scalar, schema.ByPropertyName("Category")!.Index);
    }

    [Fact]
    public void Zero_dimension_vector_is_rejected()
    {
        Assert.Throws<SchemaValidationException>(() => SchemaCompiler.Compile<BadZeroVector>("t"));
    }

    [Fact]
    public void Non_float_vector_is_rejected()
    {
        Assert.Throws<SchemaValidationException>(() => SchemaCompiler.Compile<BadVectorType>("t"));
    }

    [Fact]
    public void Multiple_keys_are_rejected()
    {
        Assert.Throws<SchemaValidationException>(() => SchemaCompiler.Compile<TwoKeys>("t"));
    }

    private sealed class BadZeroVector
    {
        [Vector]
        public float[] Vec { get; set; } = [];
    }

    private sealed class BadVectorType
    {
        [Vector(4)]
        public string Vec { get; set; } = "";
    }

    private sealed class TwoKeys
    {
        [Key] public int A { get; set; }
        [Key] public int B { get; set; }
    }
}
