using Joust.Mapping;
using Joust.Schema;
using Xunit;

namespace Joust.Tests;

public class MapperTests
{
    [Fact]
    public void Compiled_mapper_round_trips()
    {
        var schema = SchemaCompiler.Compile<Product>("products");
        var mapper = new PocoMapper<Product>(schema);

        var product = new Product
        {
            Id = 7, IsActive = true, Price = 12.5, Category = "X", Text = "t", Embedding = [1f, 2f, 3f, 4f]
        };

        var row = mapper.ToRow(product);
        Assert.Equal(7, row["id"]);
        Assert.Equal("X", row["category"]);
        Assert.Equal("t", row["body"]);          // [Column("body")]

        var back = mapper.FromRow(row);
        Assert.Equal(7, back.Id);
        Assert.Equal(12.5, back.Price);
        Assert.Equal("t", back.Text);
        Assert.Equal(new[] { 1f, 2f, 3f, 4f }, back.Embedding);
    }

    [Fact]
    public void Registered_mapper_is_preferred_over_reflection()
    {
        MapperRegistry.Register<Widget>(new WidgetMapper());
        Assert.True(MapperRegistry.IsRegistered(typeof(Widget)));

        var schema = SchemaCompiler.Compile<Widget>("widgets");
        var mapper = new PocoMapper<Widget>(schema);

        var row = mapper.ToRow(new Widget { Id = 1 });
        Assert.Equal("generated", row["marker"]); // proves the registered mapper was used
    }

    private sealed class Widget
    {
        [Key] public int Id { get; set; }
    }

    private sealed class WidgetMapper : IRowMapper<Widget>
    {
        public Dictionary<string, object?> ToRow(Widget item) =>
            new() { ["marker"] = "generated", ["id"] = item.Id };

        public Widget FromRow(IReadOnlyDictionary<string, object?> row) =>
            new() { Id = System.Convert.ToInt32(row["id"]) };
    }
}
