using Joust.VectorData;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace Joust.Tests;

public class VectorDataAdapterTests
{
    private static async Task<VectorStoreCollection<int, Product>> SetupAsync()
    {
        var ctx = await LanceDbContext.ConnectAsync("memory://vd",
            new LanceConnectOptions { UseInMemoryEngine = true });

        var store = new JoustVectorStore(ctx);
        var collection = store.GetCollection<int, Product>("products");
        await collection.EnsureCollectionExistsAsync();

        await collection.UpsertAsync(new[]
        {
            new Product { Id = 1, IsActive = true,  Price = 100, Category = "Tools", Text = "hammer", Embedding = [1f, 0f, 0f, 0f] },
            new Product { Id = 2, IsActive = true,  Price = 20,  Category = "Toys",  Text = "ball",   Embedding = [0f, 1f, 0f, 0f] },
            new Product { Id = 3, IsActive = false, Price = 80,  Category = "Tools", Text = "drill",  Embedding = [0.9f, 0.1f, 0f, 0f] },
        });

        return collection;
    }

    [Fact]
    public async Task Get_by_key_returns_record()
    {
        var collection = await SetupAsync();
        var record = await collection.GetAsync(1);
        Assert.NotNull(record);
        Assert.Equal("hammer", record!.Text);
    }

    [Fact]
    public async Task Vector_search_returns_nearest()
    {
        var collection = await SetupAsync();
        float[] query = [1f, 0f, 0f, 0f];

        var ids = new List<int>();
        await foreach (var hit in collection.SearchAsync(query, top: 2))
            ids.Add(hit.Record.Id);

        Assert.Equal(2, ids.Count);
        Assert.Equal(1, ids[0]); // exact match closest
    }

    [Fact]
    public async Task Filtered_get_returns_matches()
    {
        var collection = await SetupAsync();

        var ids = new List<int>();
        await foreach (var p in collection.GetAsync(x => x.Category == "Tools", top: 10))
            ids.Add(p.Id);

        Assert.Equal(new[] { 1, 3 }, ids.OrderBy(i => i).ToArray());
    }

    [Fact]
    public async Task Upsert_updates_existing_record()
    {
        var collection = await SetupAsync();

        await collection.UpsertAsync(new Product
        {
            Id = 1, Price = 999, Category = "Tools", Text = "sledgehammer", Embedding = [1f, 0f, 0f, 0f]
        });

        var record = await collection.GetAsync(1);
        Assert.Equal("sledgehammer", record!.Text);
        Assert.Equal(999, record.Price);
    }

    [Fact]
    public async Task Delete_removes_record()
    {
        var collection = await SetupAsync();
        await collection.DeleteAsync(2);
        Assert.Null(await collection.GetAsync(2));
    }
}
