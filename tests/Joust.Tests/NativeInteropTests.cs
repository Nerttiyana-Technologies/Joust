using Joust.Interop.Native;
using Xunit;

namespace Joust.Tests;

public class NativeInteropTests
{
    [Fact]
    public void Rid_is_well_formed()
    {
        var rid = RidResolver.Current;
        Assert.False(string.IsNullOrWhiteSpace(rid));
        Assert.Contains('-', rid);

        var os = rid.Split('-')[0];
        Assert.Contains(os, new[] { "win", "osx", "linux", "unknown" });
    }

    [Theory]
    [InlineData("joust_lancedb")]
    public void Native_file_name_matches_platform(string baseName)
    {
        var file = RidResolver.NativeFileName(baseName);
        Assert.Contains(baseName, file);
        Assert.True(
            file.EndsWith(".dll", StringComparison.Ordinal) ||
            file.EndsWith(".so", StringComparison.Ordinal) ||
            file.EndsWith(".dylib", StringComparison.Ordinal),
            $"Unexpected native file name '{file}'.");
    }

    [Fact]
    public void Candidate_paths_include_runtimes_layout()
    {
        var rid = RidResolver.Current;
        var candidates = NativePathLocator.CandidatePaths(rid, "joust_lancedb");

        Assert.NotEmpty(candidates);
        Assert.Contains(candidates, p =>
            p.Replace('\\', '/').Contains($"runtimes/{rid}/native/", StringComparison.Ordinal));
    }

    [Fact]
    public void TryLocate_returns_null_when_no_binary_present()
    {
        Assert.Null(NativePathLocator.TryLocate(RidResolver.Current, "definitely_missing_lib"));
    }

    [Fact]
    public void Locate_throws_descriptive_error_when_missing()
    {
        var rid = RidResolver.Current;
        var ex = Assert.Throws<NativeLibraryNotFoundException>(
            () => NativePathLocator.Locate(rid, "definitely_missing_lib"));
        Assert.Contains(rid, ex.Message);
    }

    [Fact]
    public async Task Native_backend_disabled_by_default_throws_clear_error()
    {
        await Assert.ThrowsAsync<BackendNotAvailableException>(
            () => LanceDbContext.ConnectAsync("db://cluster"));
    }
}
