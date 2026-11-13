#pragma once

namespace mjolnir::paths {
bool Init();              // fills the paths below from GetModuleFileNameW(nullptr)
bool IsHostAska();        // exe file name is Aska.exe (case-insensitive)
bool KillSwitchActive();  // "--no-mods" on the command line or Mjolnir\disable exists
bool LoaderDirExists();

const wchar_t* GameDir();
const wchar_t* LoaderDir();
const wchar_t* LogPath();
}
