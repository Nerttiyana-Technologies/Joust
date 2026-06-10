using Xunit;

namespace Joust.Tests;

public class EndToEndTests
{
    private static async Task<(LanceDbContext ctx, LanceTable<Product> table)> SetupAsync()
    {
        var ctx = await LanceDbContext.ConnectAsync("memory://test",
            new LanceConnectOptions { UseInMemoryEngine = true });

        var table = await ctx.CreateTableAsync<Product>("products");

        await table.AddAsync(new[]
        {
            new Product { Id = 1, IsActive = true,  Price = 100, Category = "Tools", Text = "hammer", Embedding = [1f, 0f, 0f, 0f] },
            new Product { Id = 2, IsActive = true,  Price = 20,  Category = "Toys",  Text = "ball",   Embedding = [0f, 1f, 0f, 0f] },
            new Product { Id = 3, IsActive = false, Price = 80,  Category = "Tools", Text = "drill",  Embedding = [0.9f, 0.1f, 0f, 0f] },
        });

        return (ctx, table);
    }

    [Fact]
    public async Task Full_scan_returns_all_rows()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        var all = await table.ToListAsync();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task Scalar_filter_pushes_down()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        var results = await table.Where(x => x.Price > 50).ToListAsync();
        Assert.Equal(new[] { 1, 3 }, results.Select(r => r.Id).OrderBy(i => i));
    }

    [Fact]
    public async Task Compound_filter_works()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        var results = await table.Where(x => x.IsActive && x.Category == "Tools").ToListAsync();
        Assert.Single(results);
        Assert.Equal(1, results[0].Id);
    }

    [Fact]
    public async Task Count_with_predicate()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        Assert.Equal(2, await table.CountAsync(x => x.Category == "Tools"));
        Assert.Equal(3, await table.CountAsync());
    }

    [Fact]
    public async Task Delete_removes_matching_rows()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        int removed = await table.DeleteAsync(x => x.Price < 50);
        Assert.Equal(1, removed);
        Assert.Equal(2, await table.CountAsync());
    }

    [Fact]
    public async Task Vector_search_returns_nearest_first()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        float[] query = [1f, 0f, 0f, 0f];
        var nearest = await table
            .OrderBy(x => x.Embedding.CosineDistance(query))
            .Take(2)
            .ToListAsync();

        Assert.Equal(new[] { 1, 3 }, nearest.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task Filtered_vector_search_excludes_non_matches()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        float[] query = [1f, 0f, 0f, 0f];
        var nearest = await table
            .Where(x => x.Category == "Tools")
            .OrderBy(x => x.Embedding.CosineDistance(query))
            .Take(5)
            .ToListAsync();

        Assert.Equal(new[] { 1, 3 }, nearest.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task Unavailable_native_backend_throws_clear_error()
    {
        await Assert.ThrowsAsync<BackendNotAvailableException>(
            () => LanceDbContext.ConnectAsync("db://remote-cluster"));
    }
}
