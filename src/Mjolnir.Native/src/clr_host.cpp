#include <windows.h>
#include <MinHook.h>
#include <cstdio>
#include <cstring>
#include <string>
#include "clr_host.h"
#include "coreclr_delegates.h"
#include "hostfxr.h"
#include "log.h"
#include "native_context.h"
#include "paths.h"

namespace mjolnir {
using start_fn = int(CORECLR_DELEGATE_CALLTYPE*)(NativeContext* ctx);

static NativeContext g_ctx;  // must outlive the managed side

static int __cdecl HookCreate(void* target, void* detour, void** original) { return MH_CreateHook(target, detour, original); }
static int __cdecl HookEnable(void* target) { return MH_EnableHook(target); }
static int __cdecl HookDisable(void* target) { return MH_DisableHook(target); }
static int __cdecl HookRemove(void* target) { return MH_RemoveHook(target); }

static void HOSTFXR_CALLTYPE HostfxrError(const char_t* message)
{
    log::Write(log::Error, L"hostfxr", L"%s", message);
}

// <dotnetRoot>\host\fxr\<highest version>\hostfxr.dll, or empty if there is none.
static std::wstring FindHostfxr(const std::wstring& dotnetRoot)
{
    std::wstring best;
    unsigned bestKey[4] = {};
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((dotnetRoot + L"\\host\\fxr\\*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return {};
    do
    {
        if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || fd.cFileName[0] == L'.') continue;
        unsigned key[4] = {};
        swscanf_s(fd.cFileName, L"%u.%u.%u.%u", &key[0], &key[1], &key[2], &key[3]);
        bool greater = best.empty();
        for (int i = 0; i < 4 && !greater && key[i] >= bestKey[i]; ++i)
            greater = key[i] > bestKey[i];
        if (greater)
        {
            best = fd.cFileName;
            memcpy(bestKey, key, sizeof(key));
        }
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    return best.empty() ? best : dotnetRoot + L"\\host\\fxr\\" + best + L"\\hostfxr.dll";
}

template <class T>
static T Proc(HMODULE module, const char* name)
{
    return reinterpret_cast<T>(GetProcAddress(module, name));
}

static __declspec(noinline) bool StartManaged(HMODULE gameAssembly)
{
    const std::wstring loader = paths::LoaderDir();
    const std::wstring dotnetRoot = loader + L"\\dotnet";
    const std::wstring coreDll = loader + L"\\core\\Mjolnir.Core.dll";
    const std::wstring coreConfig = loader + L"\\core\\Mjolnir.Core.runtimeconfig.json";

    std::wstring fxrPath = FindHostfxr(dotnetRoot);
    if (fxrPath.empty())
    {
        log::Write(log::Error, L"Native", L"hostfxr.dll not found under %s", dotnetRoot.c_str());
        return false;
    }
    HMODULE fxr = LoadLibraryExW(fxrPath.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!fxr)
    {
        log::Write(log::Error, L"Native", L"LoadLibrary(%s) failed: %lu", fxrPath.c_str(), GetLastError());
        return false;
    }

    auto init = Proc<hostfxr_initialize_for_runtime_config_fn>(fxr, "hostfxr_initialize_for_runtime_config");
    auto getDelegate = Proc<hostfxr_get_runtime_delegate_fn>(fxr, "hostfxr_get_runtime_delegate");
    auto closeContext = Proc<hostfxr_close_fn>(fxr, "hostfxr_close");
    auto setErrorWriter = Proc<hostfxr_set_error_writer_fn>(fxr, "hostfxr_set_error_writer");
    if (!init || !getDelegate || !closeContext)
    {
        log::Write(log::Error, L"Native", L"%s lacks the hosting exports", fxrPath.c_str());
        return false;
    }
    if (setErrorWriter) setErrorWriter(&HostfxrError);
    log::Write(log::Info, L"Native", L"using %s", fxrPath.c_str());

    hostfxr_initialize_parameters params{ sizeof(params), coreDll.c_str(), dotnetRoot.c_str() };
    hostfxr_handle context = nullptr;
    int rc = init(coreConfig.c_str(), &params, &context);
    if (rc < 0 || !context)
    {
        log::Write(log::Error, L"Native", L"hostfxr_initialize_for_runtime_config(%s) failed: 0x%08X", coreConfig.c_str(), rc);
        if (context) closeContext(context);
        return false;
    }

    load_assembly_fn loadAssembly = nullptr;
    get_function_pointer_fn getFunctionPointer = nullptr;
    int rcLoad = getDelegate(context, hdt_load_assembly, reinterpret_cast<void**>(&loadAssembly));
    int rcGet = getDelegate(context, hdt_get_function_pointer, reinterpret_cast<void**>(&getFunctionPointer));
    closeContext(context);
    if (rcLoad < 0 || rcGet < 0 || !loadAssembly || !getFunctionPointer)
    {
        log::Write(log::Error, L"Native", L"hostfxr_get_runtime_delegate failed: 0x%08X / 0x%08X", rcLoad, rcGet);
        return false;
    }

    // Default ALC, so Core, the SDK, Il2CppInterop and mods share one type identity.
    if ((rc = loadAssembly(coreDll.c_str(), nullptr, nullptr)) < 0)
    {
        log::Write(log::Error, L"Native", L"load_assembly(%s) failed: 0x%08X", coreDll.c_str(), rc);
        return false;
    }

    start_fn start = nullptr;
    rc = getFunctionPointer(L"Mjolnir.Core.Bootstrap, Mjolnir.Core", L"Start", UNMANAGEDCALLERSONLY_METHOD,
                            nullptr, nullptr, reinterpret_cast<void**>(&start));
    if (rc < 0 || !start)
    {
        log::Write(log::Error, L"Native", L"get_function_pointer(Bootstrap.Start) failed: 0x%08X", rc);
        return false;
    }

    g_ctx = NativeContext{ sizeof(NativeContext), kNativeContextVersion, paths::GameDir(), paths::LoaderDir(),
                           gameAssembly, &log::FromManaged, &HookCreate, &HookEnable, &HookDisable, &HookRemove };
    rc = start(&g_ctx);
    log::Write(rc == 0 ? log::Info : log::Error, L"Native", L"Bootstrap.Start returned %d", rc);
    return rc == 0;
}

// No objects with destructors here: __try cannot coexist with C++ unwinding in one function.
static int StartManagedSeh(HMODULE gameAssembly)
{
    __try
    {
        return StartManaged(gameAssembly) ? 1 : 0;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return -static_cast<int>(GetExceptionCode());
    }
}

void StartManagedRuntime(HMODULE gameAssembly)
{
    int r = StartManagedSeh(gameAssembly);
    if (r < 0)
        log::Write(log::Error, L"Native", L"native fault 0x%08X while starting CoreCLR; continuing without mods", -r);
    else if (r == 0)
        log::Write(log::Warn, L"Native", L"managed runtime not started; continuing without mods");
}
}
