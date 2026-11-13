#pragma once

namespace mjolnir::log {
enum Level : int { Debug = 0, Info = 1, Warn = 2, Error = 3 };

void Open(const wchar_t* path);                               // kernel32 only: safe under loader lock
void Write(Level level, const wchar_t* source, const wchar_t* fmt, ...);
void __cdecl FromManaged(int level, const wchar_t* message);  // NativeContext::log
}
