#pragma once
#include <windows.h>

namespace mjolnir::iat {
// Replaces `module`'s import of importDll!function with `replacement` and stores the previous
// pointer in `original`. Returns false if the module does not import that function by name.
bool Patch(HMODULE module, const char* importDll, const char* function, void* replacement, void** original);
}
