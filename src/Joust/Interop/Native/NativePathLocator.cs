using System.Reflection;

namespace Joust.Interop.Native;

/// <summary>
/// Locates the native binary for a given RID by probing the conventional
/// <c>runtimes/&lt;rid&gt;/native/</c> layout (and a few fallbacks) relative to the loaded
/// assembly and the application base directory.
/// </summary>
public static class NativePathLocator
{
    /// <summary>Returns candidate absolute paths for the native library, in probe order.</summary>
    public static IReadOnlyList<string> CandidatePaths(string rid, string baseName)
    {
        var fileName = RidResolver.NativeFileName(baseName);
        var roots = new List<string>();

        var asmDir = Path.GetDirectoryName(typeof(NativePathLocator).Assembly.Location);
        if (!string.IsNullOrEmpty(asmDir)) roots.Add(asmDir!);

        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir) && !roots.Contains(baseDir)) roots.Add(baseDir);

        var paths = new List<string>();
        foreach (var root in roots)
        {
            paths.Add(Path.Combine(root, "runtimes", rid, "native", fileName));
            paths.Add(Path.Combine(root, fileName)); // flattened (self-contained publish)
        }
        return paths;
    }

    /// <summary>Returns the first existing native library path, or null if none is present.</summary>
    public static string? TryLocate(string rid, string baseName) =>
        CandidatePaths(rid, baseName).FirstOrDefault(File.Exists);

    /// <summary>Returns the native library path, or throws with the probed locations if not found.</summary>
    public static string Locate(string rid, string baseName)
    {
        var candidates = CandidatePaths(rid, baseName);
        var found = candidates.FirstOrDefault(File.Exists);
        if (found is not null)
            return found;

        throw new NativeLibraryNotFoundException(
            $"Could not find the native '{baseName}' binary for RID '{rid}'. Probed:{Environment.NewLine}" +
            string.Join(Environment.NewLine, candidates.Select(p => "  - " + p)) + Environment.NewLine +
            "Ensure the Joust package shipped a binary for this RID, or build the native shim and place it " +
            "under runtimes/" + rid + "/native/.");
    }
}
