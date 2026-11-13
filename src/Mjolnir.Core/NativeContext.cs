using System.Runtime.InteropServices;

namespace Mjolnir.Core;

/// <summary>Mirror of native_context.h. Append-only; version 2 is 72 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeContext
{
    public const uint ExpectedVersion = 2;

    public uint Size;
    public uint Version;
    public char* GameDir;
    public char* LoaderDir;
    public nint GameAssembly;
    public delegate* unmanaged<int, char*, void> Log;
    public delegate* unmanaged<void*, void*, void**, int> HookCreate;
    public delegate* unmanaged<void*, int> HookEnable;
    public delegate* unmanaged<void*, int> HookDisable;
    public delegate* unmanaged<void*, int> HookRemove;  // v2
}
