#pragma once
#include <windows.h>

namespace mjolnir {
// Starts CoreCLR from Mjolnir\dotnet and calls Mjolnir.Core.Bootstrap.Start.
// Never throws or terminates; every failure is logged and the game continues without mods.
void StartManagedRuntime(HMODULE gameAssembly);
}
