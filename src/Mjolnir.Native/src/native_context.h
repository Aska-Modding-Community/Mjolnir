#pragma once
#include <cstdint>

// ABI shared with Mjolnir.Core (NativeContext.cs). Append-only; bump kNativeContextVersion on change.
struct NativeContext
{
    uint32_t       size;          // sizeof(NativeContext)
    uint32_t       version;       // kNativeContextVersion
    const wchar_t* gameDir;       // ...\ASKA
    const wchar_t* loaderDir;     // ...\ASKA\Mjolnir
    void*          gameAssembly;  // HMODULE of GameAssembly.dll
    void (*log)(int level, const wchar_t* message);
    int  (*hookCreate)(void* target, void* detour, void** original);
    int  (*hookEnable)(void* target);
    int  (*hookDisable)(void* target);
    int  (*hookRemove)(void* target);   // v2: re-patching a method needs the old MinHook hook removed
};

constexpr uint32_t kNativeContextVersion = 2;
static_assert(sizeof(NativeContext) == 72, "NativeContext ABI changed");
