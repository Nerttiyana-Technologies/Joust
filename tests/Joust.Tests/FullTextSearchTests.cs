using Xunit;

namespace Joust.Tests;

public class FullTextSearchTests
{
    private static async Task<(LanceDbContext ctx, LanceTable<Product> table)> SetupAsync()
    {
        var ctx = await LanceDbContext.ConnectAsync("memory://fts",
            new LanceConnectOptions { UseInMemoryEngine = true });
        var table = await ctx.CreateTableAsync<Product>("products");
        await table.AddAsync(new[]
        {
            new Product { Id = 1, IsActive = true,  Price = 100, Category = "Tools", Text = "steel hammer", Embedding = [1f, 0f, 0f, 0f] },
            new Product { Id = 2, IsActive = true,  Price = 20,  Category = "Toys",  Text = "rubber ball",  Embedding = [0f, 1f, 0f, 0f] },
            new Product { Id = 3, IsActive = false, Price = 80,  Category = "Tools", Text = "power drill",  Embedding = [0.9f, 0.1f, 0f, 0f] },
        });
        return (ctx, table);
    }

    [Fact]
    public async Task Full_text_search_finds_matching_rows()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        var hits = await table.NearestToText("hammer").ToListAsync();
        Assert.Single(hits);
        Assert.Equal(1, hits[0].Id);
    }

    [Fact]
    public async Task Full_text_search_on_specific_column()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        var hits = await table.NearestToText("Tools", "category").ToListAsync();
        Assert.Equal(new[] { 1, 3 }, hits.Select(r => r.Id).OrderBy(i => i).ToArray());
    }

    [Fact]
    public async Task Hybrid_search_fuses_vector_and_text()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        float[] query = [1f, 0f, 0f, 0f]; // closest to id 1
        var hits = await table
            .VectorSearch(query)
            .NearestToText("drill")          // text match is id 3
            .Take(3)
            .ToListAsync();

        var ids = hits.Select(r => r.Id).ToHashSet();
        Assert.Contains(1, ids); // surfaced by the vector leg
        Assert.Contains(3, ids); // surfaced by the full-text leg
    }
}
