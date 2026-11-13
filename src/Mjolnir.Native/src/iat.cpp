#include <cstring>
#include "iat.h"

namespace mjolnir::iat {
bool Patch(HMODULE module, const char* importDll, const char* function, void* replacement, void** original)
{
    auto base = reinterpret_cast<BYTE*>(module);
    auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return false;
    const auto& dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!dir.VirtualAddress) return false;

    for (auto desc = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + dir.VirtualAddress); desc->Name; ++desc)
    {
        if (_stricmp(reinterpret_cast<const char*>(base + desc->Name), importDll) != 0) continue;
        if (!desc->OriginalFirstThunk) return false;  // no name table: cannot match by name

        auto names = reinterpret_cast<IMAGE_THUNK_DATA64*>(base + desc->OriginalFirstThunk);
        auto slots = reinterpret_cast<IMAGE_THUNK_DATA64*>(base + desc->FirstThunk);
        for (; names->u1.AddressOfData; ++names, ++slots)
        {
            if (IMAGE_SNAP_BY_ORDINAL64(names->u1.Ordinal)) continue;
            auto byName = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + names->u1.AddressOfData);
            if (strcmp(reinterpret_cast<const char*>(byName->Name), function) != 0) continue;

            void** slot = reinterpret_cast<void**>(&slots->u1.Function);
            DWORD old;
            if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &old)) return false;
            *original = InterlockedExchangePointer(slot, replacement);
            VirtualProtect(slot, sizeof(void*), old, &old);
            return true;
        }
        return false;
    }
    return false;
}
}
