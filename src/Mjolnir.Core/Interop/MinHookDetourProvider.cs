using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.Injection;

namespace Mjolnir.Core.Interop;

/// <summary>
/// Il2CppInterop's native detours (its injector hooks and every HarmonyX patch on an IL2CPP method)
/// on top of the MinHook instance in Mjolnir.Native, reached through the NativeContext hook functions.
/// </summary>
internal sealed unsafe class MinHookDetourProvider : IDetourProvider
{
    private readonly NativeImage _gameAssembly;

    public MinHookDetourProvider(NativeContext* ctx, NativeImage gameAssembly)
    {
        CreateHook = ctx->HookCreate;
        EnableHook = ctx->HookEnable;
        DisableHook = ctx->HookDisable;
        RemoveHook = ctx->HookRemove;
        _gameAssembly = gameAssembly;
    }

    internal delegate* unmanaged<void*, void*, void**, int> CreateHook { get; }
    internal delegate* unmanaged<void*, int> EnableHook { get; }
    internal delegate* unmanaged<void*, int> DisableHook { get; }
    internal delegate* unmanaged<void*, int> RemoveHook { get; }

    public IDetour Create<TDelegate>(nint original, TDelegate target) where TDelegate : Delegate
    {
        string kind = typeof(TDelegate).FullName ?? typeof(TDelegate).Name;
        string section = _gameAssembly.SectionOf(original) ?? "outside GameAssembly";
        string where = section == "outside GameAssembly"
            ? $"0x{original:X}"
            : $"GameAssembly+0x{original - _gameAssembly.Base:X} ({section})";

        // Il2CppInterop's injector hooks must land in IL2CPP runtime code. On ASKA one of its signature
        // scans (GetFieldDefaultValue) matches a compiled game method instead;
        // detouring that would corrupt the game, so such a hook is skipped. Harmony patches on game
        // methods are not injector hooks and legitimately target the "il2cpp" section.
        bool injectorHook = kind.StartsWith("Il2CppInterop.Runtime.Injection", StringComparison.Ordinal);
        if (injectorHook && section != ".text")
        {
            Log.Warn("Interop", $"Skipping injector hook {kind} at {where}: target is not IL2CPP runtime code");
            return new SkippedDetour(original);
        }

        Log.Info("Interop", $"Detour {kind} at {where}");
        return new MinHookDetour(this, original, target);
    }

    internal static string Status(int status) => status switch
    {
        -1 => "MH_UNKNOWN", 0 => "MH_OK", 1 => "MH_ERROR_ALREADY_INITIALIZED", 2 => "MH_ERROR_NOT_INITIALIZED",
        3 => "MH_ERROR_ALREADY_CREATED", 4 => "MH_ERROR_NOT_CREATED", 5 => "MH_ERROR_ENABLED",
        6 => "MH_ERROR_DISABLED", 7 => "MH_ERROR_NOT_EXECUTABLE", 8 => "MH_ERROR_UNSUPPORTED_FUNCTION",
        9 => "MH_ERROR_MEMORY_ALLOC", 10 => "MH_ERROR_MEMORY_PROTECT",
        _ => $"MinHook status {status}",
    };
}

internal sealed unsafe class MinHookDetour : IDetour
{
    private readonly MinHookDetourProvider _hooks;
    private readonly Delegate _target;  // keeps the detour's native thunk alive while the detour exists
    private bool _disposed;

    public MinHookDetour(MinHookDetourProvider hooks, nint original, Delegate target)
    {
        _hooks = hooks;
        _target = target;
        Target = original;
        Detour = Marshal.GetFunctionPointerForDelegate(target);
        void* trampoline;
        int status = hooks.CreateHook((void*)original, (void*)Detour, &trampoline);
        if (status != 0)
            throw new InvalidOperationException($"MH_CreateHook(0x{original:X}) failed: {MinHookDetourProvider.Status(status)}");
        OriginalTrampoline = (nint)trampoline;
    }

    public nint Target { get; }
    public nint Detour { get; }
    public nint OriginalTrampoline { get; }

    public void Apply()
    {
        int status = _hooks.EnableHook((void*)Target);
        if (status != 0)
        {
            // The hook was created but never enabled: removed, the same method can be detoured again later.
            _disposed = true;
            _hooks.RemoveHook((void*)Target);
            throw new InvalidOperationException($"MH_EnableHook(0x{Target:X}) failed: {MinHookDetourProvider.Status(status)}");
        }
    }

    public T GenerateTrampoline<T>() where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(OriginalTrampoline);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hooks.DisableHook((void*)Target);
        _hooks.RemoveHook((void*)Target);  // so the same method can be detoured again (HarmonyX re-patching)
    }
}

/// <summary>A detour that was refused: nothing is patched and the "original" is the target itself.</summary>
internal sealed class SkippedDetour(nint original) : IDetour
{
    public nint Target { get; } = original;
    public nint Detour => 0;
    public nint OriginalTrampoline { get; } = original;
    public void Apply() { }
    public T GenerateTrampoline<T>() where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(OriginalTrampoline);
    public void Dispose() { }
}
