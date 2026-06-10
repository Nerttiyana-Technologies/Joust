using System.Linq.Expressions;
using Joust.Linq;
using Joust.Schema;
using Xunit;

namespace Joust.Tests;

public class FilterTranslatorTests
{
    private static readonly TableSchema Schema = SchemaCompiler.Compile<Product>("products");

    private static string Translate(Expression<Func<Product, bool>> predicate) =>
        FilterTranslator.Translate(predicate, Schema);

    [Fact]
    public void Boolean_equality_matches_srs_example()
    {
        Assert.Equal("is_active = true", Translate(x => x.IsActive == true));
    }

    [Fact]
    public void Compound_and_matches_srs_example()
    {
        Assert.Equal("(price > 50 AND category = 'Tools')",
            Translate(x => x.Price > 50 && x.Category == "Tools"));
    }

    [Fact]
    public void Bare_boolean_column_renders_equals_true()
    {
        Assert.Equal("is_active = true", Translate(x => x.IsActive));
    }

    [Fact]
    public void Negated_boolean_column_renders_equals_false()
    {
        Assert.Equal("is_active = false", Translate(x => !x.IsActive));
    }

    [Fact]
    public void Not_equal_uses_angle_brackets()
    {
        Assert.Equal("category <> 'n/a'", Translate(x => x.Category != "n/a"));
    }

    [Fact]
    public void Null_equality_becomes_is_null()
    {
        Assert.Equal("body IS NULL", Translate(x => x.Text == null!));
    }

    [Fact]
    public void Null_inequality_becomes_is_not_null()
    {
        Assert.Equal("body IS NOT NULL", Translate(x => x.Text != null!));
    }

    [Fact]
    public void Or_is_supported()
    {
        Assert.Equal("(price < 10 OR price > 100)",
            Translate(x => x.Price < 10 || x.Price > 100));
    }

    [Fact]
    public void Contains_on_collection_becomes_in()
    {
        var ids = new[] { 1, 2, 3 };
        Assert.Equal("id IN (1, 2, 3)", Translate(x => ids.Contains(x.Id)));
    }

    [Fact]
    public void Contains_on_list_becomes_in()
    {
        var cats = new List<string> { "a", "b" };
        Assert.Equal("category IN ('a', 'b')", Translate(x => cats.Contains(x.Category)));
    }

    [Fact]
    public void String_contains_becomes_like()
    {
        Assert.Equal("category LIKE '%oo%'", Translate(x => x.Category.Contains("oo")));
    }

    [Fact]
    public void String_startswith_becomes_prefix_like()
    {
        Assert.Equal("category LIKE 'foo%'", Translate(x => x.Category.StartsWith("foo")));
    }

    [Fact]
    public void Single_quotes_are_escaped()
    {
        Assert.Equal("category = 'O''Brien'", Translate(x => x.Category == "O'Brien"));
    }

    [Fact]
    public void Captured_variable_is_inlined()
    {
        double threshold = 42.5;
        Assert.Equal("price >= 42.5", Translate(x => x.Price >= threshold));
    }

    [Fact]
    public void Reversed_operand_order_is_normalized()
    {
        Assert.Equal("price < 50", Translate(x => 50 > x.Price));
    }

    [Fact]
    public void Unsupported_expression_throws_loudly()
    {
        Assert.Throws<NotSupportedExpressionException>(() => Translate(x => x.Price * 2 > 10));
    }
}
