#include <windows.h>
#include <cstdarg>
#include <cstdio>
#include <cwchar>
#include "log.h"

namespace mjolnir::log {
static HANDLE  g_file = INVALID_HANDLE_VALUE;
static SRWLOCK g_lock = SRWLOCK_INIT;
static const wchar_t* const kNames[] = { L"Debug", L"Info ", L"Warn ", L"Error" };

// A record longer than this is cut, ending in an ellipsis (a stack trace is far shorter; this only bounds a runaway).
static const size_t kMaxText = 1 << 20;

void Open(const wchar_t* path)
{
    g_file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                         OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
}

// A buffer on the stack when the record fits, else on the process heap (kernel32 only, as the rest of the logger).
template <typename T, size_t N>
class Buffer
{
public:
    explicit Buffer(size_t count)
    {
        if (count > N)
        {
            heap_ = static_cast<T*>(HeapAlloc(GetProcessHeap(), 0, count * sizeof(T)));
            data_ = heap_;
        }
    }
    ~Buffer()
    {
        if (heap_) HeapFree(GetProcessHeap(), 0, heap_);
    }
    Buffer(const Buffer&) = delete;
    Buffer& operator=(const Buffer&) = delete;
    T* Data() const { return data_; }

private:
    T  stack_[N];
    T* heap_ = nullptr;
    T* data_ = stack_;
};

static void WriteLine(int level, const wchar_t* source, const wchar_t* text)
{
    if (g_file == INVALID_HANDLE_VALUE) return;
    SYSTEMTIME t;
    GetLocalTime(&t);
    wchar_t prefix[160];
    int p = _snwprintf_s(prefix, _TRUNCATE, L"[%04u-%02u-%02u %02u:%02u:%02u.%03u] [%s] %s%s%s",
                         t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond, t.wMilliseconds,
                         kNames[(level >= Debug && level <= Error) ? level : Info],
                         source ? L"[" : L"", source ? source : L"", source ? L"] " : L"");
    if (p < 0) p = static_cast<int>(wcslen(prefix));

    size_t length = wcslen(text);
    bool cut = length > kMaxText;
    if (cut) length = kMaxText;
    size_t total = static_cast<size_t>(p) + length + (cut ? 1 : 0) + 2;

    Buffer<wchar_t, 4096> line(total);
    if (!line.Data()) return;
    wchar_t* w = line.Data();
    wmemcpy(w, prefix, static_cast<size_t>(p));
    wmemcpy(w + p, text, length);
    size_t n = static_cast<size_t>(p) + length;
    if (cut) w[n++] = L'\x2026';
    w[n++] = L'\r';
    w[n++] = L'\n';

    int bytes = WideCharToMultiByte(CP_UTF8, 0, w, static_cast<int>(n), nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) return;
    Buffer<char, 12288> utf8(static_cast<size_t>(bytes));
    if (!utf8.Data()) return;
    bytes = WideCharToMultiByte(CP_UTF8, 0, w, static_cast<int>(n), utf8.Data(), bytes, nullptr, nullptr);
    if (bytes <= 0) return;
    AcquireSRWLockExclusive(&g_lock);
    DWORD written;
    WriteFile(g_file, utf8.Data(), static_cast<DWORD>(bytes), &written, nullptr);
    ReleaseSRWLockExclusive(&g_lock);
}

void Write(Level level, const wchar_t* source, const wchar_t* fmt, ...)
{
    wchar_t text[3072];
    va_list args;
    va_start(args, fmt);
    _vsnwprintf_s(text, _TRUNCATE, fmt, args);
    va_end(args);
    WriteLine(level, source, text);
}

// Managed callers put their own "[source]" prefix into the message, so no source column here. A managed line (a stack
// trace) may be of any length: it is written whole.
void __cdecl FromManaged(int level, const wchar_t* message)
{
    WriteLine(level, nullptr, message ? message : L"");
}
}
