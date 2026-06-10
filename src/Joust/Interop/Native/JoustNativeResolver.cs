using System.Reflection;
using System.Runtime.InteropServices;

namespace Joust.Interop.Native;

/// <summary>
/// Installs a deterministic native-library resolver via <see cref="NativeLibrary.SetDllImportResolver"/>
/// (ADR-4). Rather than relying on the runtime's default probing — the documented cause of
/// cross-platform <c>DllNotFoundException</c> — Joust composes the current RID and loads the exact
/// binary, raising a descriptive <see cref="NativeLibraryNotFoundException"/> when it is missing.
/// </summary>
public static class JoustNativeResolver
{
    private static int _installed;

    /// <summary>
    /// Registers the resolver for the Joust assembly. Invoked from the native interop type's static
    /// constructor before any P/Invoke; safe to call more than once.
    /// </summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1)
            return;

        NativeLibrary.SetDllImportResolver(typeof(JoustNativeResolver).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, NativeMethods.LibraryName, StringComparison.Ordinal))
            return IntPtr.Zero; // defer to default resolution for anything else

        var rid = RidResolver.Current;
        var path = NativePathLocator.Locate(rid, NativeMethods.LibraryName);
        return NativeLibrary.Load(path);
    }
}
