#include <windows.h>
#include <cwchar>
#include "paths.h"

namespace mjolnir::paths {
static wchar_t g_exe[MAX_PATH], g_game[MAX_PATH], g_loader[MAX_PATH], g_log[MAX_PATH];

// base + suffix into a MAX_PATH buffer; false when it does not fit (the secure CRT functions would end the process).
static bool Join(wchar_t (&out)[MAX_PATH], const wchar_t* base, const wchar_t* suffix)
{
    size_t a = wcslen(base), b = wcslen(suffix);
    if (a + b >= MAX_PATH) return false;
    wmemcpy(out, base, a);
    wmemcpy(out + a, suffix, b);
    out[a + b] = L'\0';
    return true;
}

// False for a game installed under a path too long for these buffers: the DLL then stays a pure proxy.
bool Init()
{
    DWORD n = GetModuleFileNameW(nullptr, g_exe, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return false;
    wmemcpy(g_game, g_exe, n + 1);
    wchar_t* slash = wcsrchr(g_game, L'\\');
    if (!slash) return false;
    *slash = L'\0';
    return Join(g_loader, g_game, L"\\Mjolnir") && Join(g_log, g_loader, L"\\LogOutput.log");
}

bool IsHostAska()
{
    const wchar_t* name = wcsrchr(g_exe, L'\\');
    return name && _wcsicmp(name + 1, L"Aska.exe") == 0;
}

bool KillSwitchActive()
{
    // CommandLineToArgvW lives in shell32, which must not be loaded under loader lock;
    // a substring check is enough for a flag nobody else passes.
    if (wcsstr(GetCommandLineW(), L"--no-mods")) return true;
    wchar_t flag[MAX_PATH];
    if (!Join(flag, g_loader, L"\\disable")) return false;
    return GetFileAttributesW(flag) != INVALID_FILE_ATTRIBUTES;
}

bool LoaderDirExists()
{
    DWORD a = GetFileAttributesW(g_loader);
    return a != INVALID_FILE_ATTRIBUTES && (a & FILE_ATTRIBUTE_DIRECTORY);
}

const wchar_t* GameDir()   { return g_game; }
const wchar_t* LoaderDir() { return g_loader; }
const wchar_t* LogPath()   { return g_log; }
}
