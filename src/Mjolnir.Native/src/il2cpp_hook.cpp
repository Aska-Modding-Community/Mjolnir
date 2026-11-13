#include <windows.h>
#include <MinHook.h>
#include <cstring>
#include "clr_host.h"
#include "iat.h"
#include "il2cpp_hook.h"
#include "log.h"

namespace mjolnir {
using il2cpp_init_fn = int (*)(const char* domain_name);

// RVA of il2cpp_init in the analysed build (Unity 6000.3.12f1). Diagnostic only: the hook target is
// always resolved by name, so a game update changes nothing but this log line.
constexpr uintptr_t kAnalysedIl2CppInitRva = 0x62A430;

static decltype(&LoadLibraryExW) g_LoadLibraryExW;
static decltype(&LoadLibraryW)   g_LoadLibraryW;
static decltype(&LoadLibraryA)   g_LoadLibraryA;
static decltype(&GetProcAddress) g_GetProcAddress;
static il2cpp_init_fn            g_il2cpp_init;
static volatile LONG             g_hookInstalled;
static HMODULE                   g_gameAssembly;

static int Hook_il2cpp_init(const char* domainName)
{
    int rc = g_il2cpp_init(domainName);
    log::Write(log::Info, L"Native", L"il2cpp_init(\"%hs\") returned %d", domainName ? domainName : "", rc);
    if (rc)
        StartManagedRuntime(g_gameAssembly);
    else
        log::Write(log::Error, L"Native", L"IL2CPP failed to initialize; mods will not be loaded");
    return rc;
}

static void InstallIl2CppInitHook(HMODULE gameAssembly)
{
    auto target = reinterpret_cast<void*>(GetProcAddress(gameAssembly, "il2cpp_init"));
    if (!target)
    {
        log::Write(log::Error, L"Native", L"GameAssembly.dll has no il2cpp_init export");
        return;
    }

    uintptr_t rva = reinterpret_cast<uintptr_t>(target) - reinterpret_cast<uintptr_t>(gameAssembly);
    bool known = rva == kAnalysedIl2CppInitRva;
    log::Write(known ? log::Info : log::Warn, L"Native", L"GameAssembly.dll @ %p, il2cpp_init @ RVA 0x%llX%s",
               gameAssembly, static_cast<unsigned long long>(rva),
               known ? L" (matches analysed build)" : L" (differs from analysed build 0x62A430; game was updated)");

    MH_STATUS st = MH_Initialize();
    if (st != MH_OK && st != MH_ERROR_ALREADY_INITIALIZED)
    {
        log::Write(log::Error, L"Native", L"MH_Initialize failed: %hs", MH_StatusToString(st));
        return;
    }
    if ((st = MH_CreateHook(target, reinterpret_cast<void*>(&Hook_il2cpp_init),
                            reinterpret_cast<void**>(&g_il2cpp_init))) != MH_OK ||
        (st = MH_EnableHook(target)) != MH_OK)
    {
        log::Write(log::Error, L"Native", L"hooking il2cpp_init failed: %hs", MH_StatusToString(st));
        return;
    }
    g_gameAssembly = gameAssembly;
    log::Write(log::Info, L"Native", L"il2cpp_init detour installed");
}

// True exactly once: for the first call whose result is the mapped GameAssembly.dll image.
// Datafile/resource loads return tagged handles and never match GetModuleHandleW.
static bool ClaimGameAssembly(HMODULE module)
{
    if (!module || g_hookInstalled) return false;
    HMODULE gameAssembly = GetModuleHandleW(L"GameAssembly.dll");
    if (!gameAssembly || module != gameAssembly) return false;
    return InterlockedCompareExchange(&g_hookInstalled, 1, 0) == 0;
}

static HMODULE WINAPI Hook_LoadLibraryExW(LPCWSTR name, HANDLE file, DWORD flags)
{
    HMODULE h = g_LoadLibraryExW(name, file, flags);
    DWORD err = GetLastError();
    if (ClaimGameAssembly(h))
    {
        log::Write(log::Info, L"Native", L"GameAssembly.dll loaded via LoadLibraryExW(\"%s\", flags 0x%lX)",
                   name ? name : L"", flags);
        InstallIl2CppInitHook(h);
    }
    SetLastError(err);
    return h;
}

static HMODULE WINAPI Hook_LoadLibraryW(LPCWSTR name)
{
    HMODULE h = g_LoadLibraryW(name);
    DWORD err = GetLastError();
    if (ClaimGameAssembly(h))
    {
        log::Write(log::Info, L"Native", L"GameAssembly.dll loaded via LoadLibraryW(\"%s\")", name ? name : L"");
        InstallIl2CppInitHook(h);
    }
    SetLastError(err);
    return h;
}

static HMODULE WINAPI Hook_LoadLibraryA(LPCSTR name)
{
    HMODULE h = g_LoadLibraryA(name);
    DWORD err = GetLastError();
    if (ClaimGameAssembly(h))
    {
        log::Write(log::Info, L"Native", L"GameAssembly.dll loaded via LoadLibraryA(\"%hs\")", name ? name : "");
        InstallIl2CppInitHook(h);
    }
    SetLastError(err);
    return h;
}

// Fallback trigger. Right after loading GameAssembly.dll, UnityPlayer resolves every il2cpp_* export
// by name. If the load itself bypassed the LoadLibrary* hooks, the "il2cpp_init" lookup
// still identifies the module, and the detour is installed before UnityPlayer can call it.
static FARPROC WINAPI Hook_GetProcAddress(HMODULE module, LPCSTR name)
{
    FARPROC proc = g_GetProcAddress(module, name);
    DWORD err = GetLastError();
    bool byName = (reinterpret_cast<uintptr_t>(name) >> 16) != 0;  // not an ordinal
    if (proc && !g_hookInstalled && byName && strcmp(name, "il2cpp_init") == 0 &&
        InterlockedCompareExchange(&g_hookInstalled, 1, 0) == 0)
    {
        log::Write(log::Warn, L"Native",
                   L"GameAssembly.dll load was not seen by the LoadLibrary* hooks; installing the detour from GetProcAddress(\"il2cpp_init\")");
        InstallIl2CppInitHook(module);
    }
    SetLastError(err);
    return proc;
}

bool InstallUnityPlayerHooks(HMODULE unityPlayer)
{
    struct Import { const char* name; void* hook; void** original; };
    const Import imports[] = {
        { "LoadLibraryExW", reinterpret_cast<void*>(&Hook_LoadLibraryExW), reinterpret_cast<void**>(&g_LoadLibraryExW) },
        { "LoadLibraryW",   reinterpret_cast<void*>(&Hook_LoadLibraryW),   reinterpret_cast<void**>(&g_LoadLibraryW) },
        { "LoadLibraryA",   reinterpret_cast<void*>(&Hook_LoadLibraryA),   reinterpret_cast<void**>(&g_LoadLibraryA) },
        { "GetProcAddress", reinterpret_cast<void*>(&Hook_GetProcAddress), reinterpret_cast<void**>(&g_GetProcAddress) },
    };
    int patched = 0;
    for (const Import& i : imports)
    {
        if (iat::Patch(unityPlayer, "KERNEL32.dll", i.name, i.hook, i.original))
            ++patched;
        else
            log::Write(log::Warn, L"Native", L"UnityPlayer.dll does not import KERNEL32.dll!%hs", i.name);
    }
    log::Write(log::Debug, L"Native", L"patched %d of %d UnityPlayer.dll loader imports", patched,
               static_cast<int>(sizeof(imports) / sizeof(imports[0])));
    return patched > 0;
}
}
