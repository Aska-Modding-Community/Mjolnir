#include <windows.h>
#include "il2cpp_hook.h"
#include "log.h"
#include "paths.h"

using namespace mjolnir;

// Runs under loader lock: kernel32 calls only, no threads, no LoadLibrary. GameAssembly.dll is not
// loaded yet, so the only thing done here is patching UnityPlayer.dll's loader IAT slots
// (LoadLibrary* and GetProcAddress).
static void OnProcessAttach()
{
    if (!paths::Init() || !paths::IsHostAska() || !paths::LoaderDirExists()) return;  // pure proxy
    log::Open(paths::LogPath());
    log::Write(log::Info, L"Native", L"Mjolnir.Native attached to Aska.exe");
    if (paths::KillSwitchActive())
    {
        log::Write(log::Info, L"Native", L"kill switch active; mods disabled");
        return;
    }

    HMODULE unityPlayer = GetModuleHandleW(L"UnityPlayer.dll");
    if (!unityPlayer)
    {
        log::Write(log::Error, L"Native", L"UnityPlayer.dll is not mapped");
        return;
    }
    if (!InstallUnityPlayerHooks(unityPlayer))
    {
        log::Write(log::Error, L"Native", L"no loader import could be patched in UnityPlayer.dll");
        return;
    }
    log::Write(log::Info, L"Native", L"UnityPlayer.dll loader imports patched; waiting for GameAssembly.dll");
}

BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(self);
        OnProcessAttach();
    }
    return TRUE;  // never fail the load: the game must start even if the loader does nothing
}
