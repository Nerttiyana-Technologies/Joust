using System.Runtime.InteropServices;

namespace Joust.Interop.Native;

/// <summary>
/// Owns a native connection pointer and guarantees it is freed exactly once, even on exceptional
/// paths (FR-1.3 — zero-leak enforcement). Raw <see cref="IntPtr"/> values never escape the
/// interop layer.
/// </summary>
internal sealed class SafeConnectionHandle : SafeHandle
{
    public SafeConnectionHandle() : base(IntPtr.Zero, ownsHandle: true) { }

    public SafeConnectionHandle(IntPtr handle) : base(IntPtr.Zero, ownsHandle: true) => SetHandle(handle);

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        NativeMethods.ConnectionFree(handle);
        return true;
    }
}

/// <summary>Owns a native table pointer and frees it deterministically.</summary>
internal sealed class SafeTableHandle : SafeHandle
{
    public SafeTableHandle() : base(IntPtr.Zero, ownsHandle: true) { }

    public SafeTableHandle(IntPtr handle) : base(IntPtr.Zero, ownsHandle: true) => SetHandle(handle);

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        NativeMethods.TableFree(handle);
        return true;
    }
}
