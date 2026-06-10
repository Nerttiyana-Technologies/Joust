using Joust.Schema;
using Xunit;

namespace Joust.Tests;

public class NamingTests
{
    [Theory]
    [InlineData("IsActive", "is_active")]
    [InlineData("Price", "price")]
    [InlineData("Category", "category")]
    [InlineData("Embedding", "embedding")]
    [InlineData("HTTPStatusCode", "http_status_code")]
    [InlineData("ID", "id")]
    [InlineData("customerId", "customer_id")]
    public void ToSnakeCase_converts_correctly(string input, string expected)
    {
        Assert.Equal(expected, Naming.ToSnakeCase(input));
    }
}
