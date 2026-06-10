using System.Runtime.InteropServices;

namespace Joust.Interop.Native;

/// <summary>
/// Resolves the current .NET Runtime Identifier (RID) used to locate the matching native binary.
/// Implements the OS/architecture half of the configuration-driven loading described in FR-1.2.
/// </summary>
public static class RidResolver
{
    /// <summary>The RID for the current process, e.g. <c>osx-arm64</c>, <c>linux-x64</c>, <c>win-arm64</c>.</summary>
    public static string Current { get; } = $"{OsPart()}-{ArchPart()}";

    /// <summary>The native library file name for the current OS, e.g. <c>liblancedb.dylib</c>.</summary>
    public static string NativeFileName(string baseName) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"{baseName}.dll"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? $"lib{baseName}.dylib"
        : $"lib{baseName}.so";

    private static string OsPart()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "win";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "osx";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        return "unknown";
    }

    private static string ArchPart() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        Architecture.Arm => "arm",
        _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
    };
}
