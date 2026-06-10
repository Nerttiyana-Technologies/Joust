using Joust.Interop;
using Xunit;

namespace Joust.Tests;

public class Phase1ApiTests
{
    private static async Task<(LanceDbContext ctx, LanceTable<Product> table)> SetupAsync()
    {
        var ctx = await LanceDbContext.ConnectAsync("memory://p1",
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
    public async Task MergeInsert_updates_and_inserts()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        var result = await table.MergeInsert(x => x.Id)
            .WhenMatchedUpdateAll()
            .WhenNotMatchedInsertAll()
            .ExecuteAsync(new[]
            {
                new Product { Id = 1, Price = 999, Category = "Tools", Text = "hammer", Embedding = [1f, 0f, 0f, 0f] },
                new Product { Id = 4, Price = 5,   Category = "New",   Text = "nail",   Embedding = [0f, 0f, 1f, 0f] },
            });

        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Deleted);
        Assert.Equal(4, await table.CountAsync());

        var one = await table.Where(x => x.Id == 1).FirstOrDefaultAsync();
        Assert.NotNull(one);
        Assert.Equal(999, one!.Price);
    }

    [Fact]
    public async Task MergeInsert_deletes_rows_missing_from_source()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        var result = await table.MergeInsert(x => x.Id)
            .WhenMatchedUpdateAll()
            .WhenNotMatchedBySourceDelete()
            .ExecuteAsync(new[]
            {
                new Product { Id = 1, Price = 100, Category = "Tools", Text = "hammer", Embedding = [1f, 0f, 0f, 0f] },
            });

        Assert.Equal(2, result.Deleted);
        Assert.Equal(1, await table.CountAsync());
    }

    [Fact]
    public async Task Select_projects_results()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        var ids = await table.Where(x => x.Price > 50).Select(x => x.Id).ToListAsync();
        Assert.Equal(new[] { 1, 3 }, ids.OrderBy(i => i).ToArray());
    }

    [Fact]
    public async Task OrderByColumn_sorts_ascending_and_descending()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        var asc = await table.OrderByColumn(x => x.Price).Select(x => x.Id).ToListAsync();
        Assert.Equal(new[] { 2, 3, 1 }, asc.ToArray());

        var desc = await table.OrderByColumnDescending(x => x.Price).Select(x => x.Id).ToListAsync();
        Assert.Equal(new[] { 1, 3, 2 }, desc.ToArray());
    }

    [Fact]
    public async Task AnyAsync_reflects_filter()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        Assert.True(await table.Where(x => x.Price > 50).AnyAsync());
        Assert.False(await table.Where(x => x.Price > 200).AnyAsync());
    }

    [Fact]
    public async Task Index_management_create_list_drop()
    {
        var (ctx, table) = await SetupAsync();
        await using var _ = ctx;

        var name = await table.CreateIndexAsync(x => x.Embedding, new IndexSpec { Kind = "HNSW_SQ" });
        Assert.Equal("embedding_idx", name);

        var indexes = await table.ListIndexesAsync();
        Assert.Contains("embedding_idx", indexes);

        await table.DropIndexAsync("embedding_idx");
        Assert.Empty(await table.ListIndexesAsync());
    }
}
