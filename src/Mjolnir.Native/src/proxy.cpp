#include <windows.h>
#include <cwchar>

// Forwarders to the real %SystemRoot%\System32\version.dll, loaded on first call (never in DllMain).
// Exported under the original names through version.def.

static HMODULE   g_real;
static INIT_ONCE g_once = INIT_ONCE_STATIC_INIT;

static BOOL CALLBACK LoadReal(PINIT_ONCE, PVOID, PVOID*)
{
    wchar_t path[MAX_PATH];
    UINT n = GetSystemDirectoryW(path, MAX_PATH);
    if (n && n < MAX_PATH - 13)
    {
        wcscat_s(path, L"\\version.dll");
        g_real = LoadLibraryW(path);
    }
    return TRUE;
}

static FARPROC Real(const char* name)
{
    InitOnceExecuteOnce(&g_once, &LoadReal, nullptr, nullptr);
    return g_real ? GetProcAddress(g_real, name) : nullptr;
}

#define FORWARD(ret, name, params, args, failValue)                        \
    extern "C" ret WINAPI proxy_##name params                               \
    {                                                                       \
        using Fn = ret(WINAPI*) params;                                     \
        static Fn fn = reinterpret_cast<Fn>(Real(#name));                   \
        if (!fn)                                                            \
        {                                                                   \
            SetLastError(ERROR_PROC_NOT_FOUND);                             \
            return failValue;                                               \
        }                                                                   \
        return fn args;                                                     \
    }

FORWARD(BOOL, GetFileVersionInfoA, (LPCSTR f, DWORD h, DWORD l, LPVOID d), (f, h, l, d), FALSE)
FORWARD(BOOL, GetFileVersionInfoW, (LPCWSTR f, DWORD h, DWORD l, LPVOID d), (f, h, l, d), FALSE)
FORWARD(BOOL, GetFileVersionInfoExA, (DWORD fl, LPCSTR f, DWORD h, DWORD l, LPVOID d), (fl, f, h, l, d), FALSE)
FORWARD(BOOL, GetFileVersionInfoExW, (DWORD fl, LPCWSTR f, DWORD h, DWORD l, LPVOID d), (fl, f, h, l, d), FALSE)
FORWARD(DWORD, GetFileVersionInfoSizeA, (LPCSTR f, LPDWORD h), (f, h), 0)
FORWARD(DWORD, GetFileVersionInfoSizeW, (LPCWSTR f, LPDWORD h), (f, h), 0)
FORWARD(DWORD, GetFileVersionInfoSizeExA, (DWORD fl, LPCSTR f, LPDWORD h), (fl, f, h), 0)
FORWARD(DWORD, GetFileVersionInfoSizeExW, (DWORD fl, LPCWSTR f, LPDWORD h), (fl, f, h), 0)
FORWARD(DWORD, VerFindFileA, (DWORD u, LPCSTR f, LPCSTR w, LPCSTR a, LPSTR c, PUINT cl, LPSTR d, PUINT dl),
        (u, f, w, a, c, cl, d, dl), 0)
FORWARD(DWORD, VerFindFileW, (DWORD u, LPCWSTR f, LPCWSTR w, LPCWSTR a, LPWSTR c, PUINT cl, LPWSTR d, PUINT dl),
        (u, f, w, a, c, cl, d, dl), 0)
FORWARD(DWORD, VerInstallFileA, (DWORD u, LPCSTR s, LPCSTR d, LPCSTR sd, LPCSTR dd, LPCSTR cd, LPSTR t, PUINT tl),
        (u, s, d, sd, dd, cd, t, tl), 0)
FORWARD(DWORD, VerInstallFileW, (DWORD u, LPCWSTR s, LPCWSTR d, LPCWSTR sd, LPCWSTR dd, LPCWSTR cd, LPWSTR t, PUINT tl),
        (u, s, d, sd, dd, cd, t, tl), 0)
FORWARD(DWORD, VerLanguageNameA, (DWORD l, LPSTR s, DWORD n), (l, s, n), 0)
FORWARD(DWORD, VerLanguageNameW, (DWORD l, LPWSTR s, DWORD n), (l, s, n), 0)
FORWARD(BOOL, VerQueryValueA, (LPCVOID b, LPCSTR s, LPVOID* p, PUINT l), (b, s, p, l), FALSE)
FORWARD(BOOL, VerQueryValueW, (LPCVOID b, LPCWSTR s, LPVOID* p, PUINT l), (b, s, p, l), FALSE)

// Undocumented and not imported by UnityPlayer.dll: forward the four x64 register arguments untouched.
FORWARD(INT_PTR, GetFileVersionInfoByHandle, (INT_PTR a, INT_PTR b, INT_PTR c, INT_PTR d), (a, b, c, d), 0)
