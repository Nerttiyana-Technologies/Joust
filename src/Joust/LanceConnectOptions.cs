using Joust.Interop;

namespace Joust;

/// <summary>Options controlling how a <see cref="LanceDbContext"/> connects.</summary>
public sealed class LanceConnectOptions
{
    /// <summary>
    /// Forces use of the in-memory backend regardless of the connection URI. Useful for tests and
    /// local development.
    /// </summary>
    public bool UseInMemoryEngine { get; set; }

    /// <summary>
    /// Supplies a pre-constructed engine (e.g. a fake or a custom backend). When set, it takes
    /// precedence over URI-based resolution. Ownership transfers to the context.
    /// </summary>
    public INativeEngine? EngineOverride { get; set; }

    /// <summary>
    /// Routes non-in-memory URIs to the P/Invoke <c>NativeEngine</c>. Requires the native LanceDB
    /// binary for the current RID to be present, or connecting throws
    /// <see cref="NativeLibraryNotFoundException"/>. Defaults to false while the native data path is
    /// under construction; the in-memory backend remains the supported path.
    /// </summary>
    public bool EnableNativeBackend { get; set; }

    /// <summary>Free-form storage options forwarded to the native backend (e.g. cloud credentials).</summary>
    public IDictionary<string, string> StorageOptions { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Options for creating a table.</summary>
public sealed class CreateTableOptions
{
    /// <summary>If true and the table already exists, it is dropped and recreated. Defaults to false.</summary>
    public bool DropIfExists { get; set; }
}
