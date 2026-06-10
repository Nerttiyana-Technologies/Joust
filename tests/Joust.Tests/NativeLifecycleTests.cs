using Joust.Interop.Native;
using Xunit;

namespace Joust.Tests;

/// <summary>
/// Exercises the real C# → Rust → LanceDB native path. These tests are skipped (pass trivially)
/// until the native binary is built and present for the current RID — build it with
/// <c>./build-native.sh</c>, then <c>dotnet test</c> runs them automatically.
/// </summary>
public class NativeLifecycleTests
{
    private static bool NativeAvailable() =>
        NativePathLocator.TryLocate(RidResolver.Current, "joust_lancedb") is not null;

    [Fact]
    public async Task Connect_and_table_exists_against_native_engine()
    {
        if (!NativeAvailable())
            return; // native binary not built yet — nothing to exercise

        var dir = Path.Combine(Path.GetTempPath(), "joust-native-" + Guid.NewGuid().ToString("N"));
        await using var ctx = await LanceDbContext.ConnectAsync(dir,
            new LanceConnectOptions { EnableNativeBackend = true });

        // A fresh database has no tables.
        Assert.False(await ctx.TableExistsAsync("does_not_exist"));
    }

    [Fact]
    public async Task Count_on_missing_table_surfaces_native_error()
    {
        if (!NativeAvailable())
            return;

        var dir = Path.Combine(Path.GetTempPath(), "joust-native-" + Guid.NewGuid().ToString("N"));
        await using var ctx = await LanceDbContext.ConnectAsync(dir,
            new LanceConnectOptions { EnableNativeBackend = true });

        var table = ctx.Table<Product>("missing");
        await Assert.ThrowsAsync<NativeInteropException>(() => table.CountAsync());
    }
}
