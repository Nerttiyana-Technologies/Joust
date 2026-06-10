using Joust.Interop.InMemory;
using Joust.Interop.Native;

namespace Joust.Interop;

/// <summary>
/// Selects the storage backend from the connection URI — the single point that implements ADR-7
/// (backend selection is configuration-driven). The same application code runs against any backend;
/// only the URI changes.
/// </summary>
public static class EngineFactory
{
    /// <summary>Creates an engine for the given URI and options.</summary>
    public static INativeEngine Create(string uri, LanceConnectOptions options)
    {
        if (options.EngineOverride is not null)
            return options.EngineOverride;

        if (options.UseInMemoryEngine || IsInMemory(uri))
            return new InMemoryEngine();

        // file path, s3://, gs://, az://  -> embedded native engine
        // db://                            -> remote distributed engine
        // Both are served by the same P/Invoke-backed NativeEngine; only the URI changes (ADR-7).
        if (options.EnableNativeBackend)
        {
            var storage = options.StorageOptions.Count > 0
                ? new Dictionary<string, string>(options.StorageOptions, StringComparer.Ordinal)
                : null;
            return new NativeEngine(uri, storage);
        }

        throw new BackendNotAvailableException(
            $"The native LanceDB backend for URI '{uri}' is not enabled in this build. " +
            "Set LanceConnectOptions.EnableNativeBackend = true (requires the native binary for this RID), " +
            "or use the in-memory backend (UseInMemoryEngine / a 'memory://' URI). The native engine drops " +
            "in behind INativeEngine without any change to application code (see ADR-1 and ADR-7).");
    }

    private static bool IsInMemory(string uri) =>
        string.IsNullOrWhiteSpace(uri) ||
        uri.StartsWith("memory:", StringComparison.OrdinalIgnoreCase) ||
        uri.Equals(":memory:", StringComparison.OrdinalIgnoreCase);
}
