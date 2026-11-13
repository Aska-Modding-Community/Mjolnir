#pragma once
#include <windows.h>

namespace mjolnir {
// Called from DllMain: patches UnityPlayer.dll's LoadLibraryExW/W/A imports (primary trigger) and
// its GetProcAddress import (fallback trigger), so that the il2cpp_init detour is installed as soon
// as GameAssembly.dll is mapped and before UnityPlayer calls il2cpp_init.
bool InstallUnityPlayerHooks(HMODULE unityPlayer);
}
