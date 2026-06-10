using Joust.Interop;
using Joust.Interop.Native;
using Xunit;

namespace Joust.Tests;

/// <summary>
/// Exercises the full native data path (create + insert + filter + vector search) through real
/// LanceDB. Opt-in: set <c>JOUST_NATIVE_DATA=1</c> and have the native binary built
/// (<c>./build-native.sh</c>). Otherwise these pass trivially so they don't gate the suite while
/// the Arrow path is being brought up.
/// </summary>
public class NativeDataTests
{
    private static bool Enabled() =>
        Environment.GetEnvironmentVariable("JOUST_NATIVE_DATA") == "1" &&
        NativePathLocator.TryLocate(RidResolver.Current, "joust_lancedb") is not null;

    private static Product[] Seed() =>
    [
        new Product { Id = 1, IsActive = true,  Price = 100, Category = "Tools", Text = "hammer", Embedding = [1f, 0f, 0f, 0f] },
        new Product { Id = 2, IsActive = true,  Price = 20,  Category = "Toys",  Text = "ball",   Embedding = [0f, 1f, 0f, 0f] },
        new Product { Id = 3, IsActive = false, Price = 80,  Category = "Tools", Text = "drill",  Embedding = [0.9f, 0.1f, 0f, 0f] },
    ];

    [Fact]
    public async Task Insert_count_filter_and_vector_search_via_native()
    {
        if (!Enabled())
            return;

        var dir = Path.Combine(Path.GetTempPath(), "joust-data-" + Guid.NewGuid().ToString("N"));
        await using var ctx = await LanceDbContext.ConnectAsync(dir,
            new LanceConnectOptions { EnableNativeBackend = true });

        var table = await ctx.CreateTableAsync<Product>("products");
        await table.AddAsync(Seed());

        // Count through real LanceDB.
        Assert.Equal(3, await table.CountAsync());

        // Scalar filter pushed down to LanceDB.
        var filtered = await table.Where(x => x.Price > 50).ToListAsync();
        Assert.Equal(new[] { 1, 3 }, filtered.Select(r => r.Id).OrderBy(i => i).ToArray());

        // Vector nearest-neighbour search.
        float[] query = [1f, 0f, 0f, 0f];
        var nearest = await table
            .OrderBy(x => x.Embedding.CosineDistance(query))
            .Take(2)
            .ToListAsync();
        Assert.Equal(2, nearest.Count);
        Assert.Equal(1, nearest[0].Id); // exact match is closest
    }

    [Fact]
    public async Task Merge_insert_via_native()
    {
        if (!Enabled())
            return;

        var dir = Path.Combine(Path.GetTempPath(), "joust-data-" + Guid.NewGuid().ToString("N"));
        await using var ctx = await LanceDbContext.ConnectAsync(dir,
            new LanceConnectOptions { EnableNativeBackend = true });

        var table = await ctx.CreateTableAsync<Product>("products");
        await table.AddAsync(Seed());

        var result = await table.MergeInsert(x => x.Id)
            .WhenMatchedUpdateAll()
            .WhenNotMatchedInsertAll()
            .ExecuteAsync(new[]
            {
                new Product { Id = 1, Price = 999, Category = "Tools", Text = "hammer", Embedding = [1f, 0f, 0f, 0f] },
                new Product { Id = 4, Price = 5,   Category = "New",   Text = "nail",   Embedding = [0f, 0f, 1f, 0f] },
            });

        Assert.Equal(4, await table.CountAsync());
        Assert.True(result.Inserted + result.Updated >= 1);

        var one = await table.Where(x => x.Id == 1).FirstOrDefaultAsync();
        Assert.NotNull(one);
        Assert.Equal(999, one!.Price);
    }

    [Fact]
    public async Task Scalar_order_via_native()
    {
        if (!Enabled())
            return;

        var dir = Path.Combine(Path.GetTempPath(), "joust-data-" + Guid.NewGuid().ToString("N"));
        await using var ctx = await LanceDbContext.ConnectAsync(dir,
            new LanceConnectOptions { EnableNativeBackend = true });

        var table = await ctx.CreateTableAsync<Product>("products");
        await table.AddAsync(Seed());

        var asc = await table.OrderByColumn(x => x.Price).Select(x => x.Id).ToListAsync();
        Assert.Equal(new[] { 2, 3, 1 }, asc.ToArray());
    }

    [Fact]
    public async Task List_tables_and_score_via_native()
    {
        if (!Enabled())
            return;

        var dir = Path.Combine(Path.GetTempPath(), "joust-data-" + Guid.NewGuid().ToString("N"));
        await using var ctx = await LanceDbContext.ConnectAsync(dir,
            new LanceConnectOptions { EnableNativeBackend = true });

        var table = await ctx.CreateTableAsync<Product>("products");
        await table.AddAsync(Seed());

        var names = await ctx.ListTablesAsync();
        Assert.Contains("products", names);

        float[] query = [1f, 0f, 0f, 0f];
        (Product Item, double? Score)? top = null;
        await foreach (var scored in table.VectorSearch(query).Take(1).AsScoredAsyncEnumerable())
        {
            top = scored;
            break;
        }
        Assert.NotNull(top);
        Assert.NotNull(top!.Value.Score); // LanceDB _distance surfaced
    }

    [Fact]
    public async Task Full_text_search_via_native()
    {
        if (!Enabled())
            return;

        var dir = Path.Combine(Path.GetTempPath(), "joust-data-" + Guid.NewGuid().ToString("N"));
        await using var ctx = await LanceDbContext.ConnectAsync(dir,
            new LanceConnectOptions { EnableNativeBackend = true });

        var table = await ctx.CreateTableAsync<Product>("products");
        await table.AddAsync(new[]
        {
            new Product { Id = 1, Category = "Tools", Text = "steel hammer", Embedding = [1f, 0f, 0f, 0f] },
            new Product { Id = 2, Category = "Toys",  Text = "rubber ball",  Embedding = [0f, 1f, 0f, 0f] },
            new Product { Id = 3, Category = "Tools", Text = "power drill",  Embedding = [0.9f, 0.1f, 0f, 0f] },
        });

        await table.CreateFullTextIndexAsync(x => x.Text);

        var hits = await table.NearestToText("drill").ToListAsync();
        Assert.Contains(3, hits.Select(r => r.Id));
    }

    [Fact]
    public async Task Index_create_list_drop_via_native()
    {
        if (!Enabled())
            return;

        var dir = Path.Combine(Path.GetTempPath(), "joust-data-" + Guid.NewGuid().ToString("N"));
        await using var ctx = await LanceDbContext.ConnectAsync(dir,
            new LanceConnectOptions { EnableNativeBackend = true });

        var table = await ctx.CreateTableAsync<Product>("products");
        await table.AddAsync(Seed());

        // Scalar index on a scalar column works on small datasets (a vector index needs more rows).
        var name = await table.CreateIndexAsync(x => x.Category, new IndexSpec { Kind = "SCALAR" });
        Assert.Contains(name, await table.ListIndexesAsync());

        await table.DropIndexAsync(name);
        Assert.DoesNotContain(name, await table.ListIndexesAsync());
    }
}
