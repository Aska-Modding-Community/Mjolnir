# Where the game and the Unity editor are on this machine, for the build scripts (dot-sourced by deploy.ps1,
# gen-interop.ps1 and build-bundles.ps1). Nothing is assumed about drives or folders: each is found the way its own
# launcher records it, and a parameter or an environment variable overrides the lookup.

$AskaAppId = '1898300'
$KitUnityVersion = '6000.3.12f1' # the game's Unity version: mods' AssetBundles must be built with it

# The ASKA install: -GameDir, else MJOLNIR_GAME_DIR, else Steam's own records (the registry's Steam folder, every
# library in its libraryfolders.vdf, and the library whose appmanifest_1898300.acf names the game's folder).
function Find-AskaDir([string] $GameDir) {
    if (-not $GameDir) { $GameDir = $env:MJOLNIR_GAME_DIR }
    if ($GameDir) {
        if (-not (Test-Path (Join-Path $GameDir 'Aska.exe'))) { throw "Aska.exe not found in $GameDir" }
        return (Resolve-Path $GameDir).Path
    }

    $steamRoots = @()
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        $item = Get-ItemProperty $key -ErrorAction SilentlyContinue
        if ($item -and $item.SteamPath) { $steamRoots += $item.SteamPath }
        if ($item -and $item.InstallPath) { $steamRoots += $item.InstallPath }
    }
    $libraries = @()
    foreach ($root in $steamRoots | Select-Object -Unique) {
        $libraries += $root
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            # Lines such as:  "path"  "D:\\SteamLibrary"  (backslashes doubled in the file)
            foreach ($match in [regex]::Matches([IO.File]::ReadAllText($vdf), '"path"\s+"([^"]+)"')) {
                $libraries += $match.Groups[1].Value.Replace('\\', '\')
            }
        }
    }
    foreach ($library in $libraries | Select-Object -Unique) {
        $manifest = Join-Path $library "steamapps\appmanifest_$AskaAppId.acf"
        if (-not (Test-Path $manifest)) { continue }
        $installDir = [regex]::Match([IO.File]::ReadAllText($manifest), '"installdir"\s+"([^"]+)"').Groups[1].Value
        if (-not $installDir) { $installDir = 'ASKA' }
        $dir = Join-Path $library "steamapps\common\$installDir"
        if (Test-Path (Join-Path $dir 'Aska.exe')) { return (Resolve-Path $dir).Path }
    }
    throw 'ASKA not found in any Steam library: pass -GameDir <folder with Aska.exe> or set MJOLNIR_GAME_DIR'
}

# The Unity editor of the game's version: -Unity, else MJOLNIR_UNITY, else Unity Hub's install folders (its
# secondaryInstallPath.json, then the default Program Files\Unity\Hub\Editor).
function Find-UnityEditor([string] $Unity) {
    if (-not $Unity) { $Unity = $env:MJOLNIR_UNITY }
    if ($Unity) {
        if (-not (Test-Path $Unity)) { throw "Unity editor not found at $Unity" }
        return (Resolve-Path $Unity).Path
    }
    $roots = @()
    $secondary = Join-Path $env:APPDATA 'UnityHub\secondaryInstallPath.json'
    if (Test-Path $secondary) {
        $path = [IO.File]::ReadAllText($secondary).Trim().Trim('"').Replace('\\', '\')
        if ($path) { $roots += $path }
    }
    $roots += Join-Path $env:ProgramFiles 'Unity\Hub\Editor'
    foreach ($root in $roots) {
        $exe = Join-Path $root "$KitUnityVersion\Editor\Unity.exe"
        if (Test-Path $exe) { return $exe }
    }
    throw "Unity $KitUnityVersion not found in Unity Hub's folders ($($roots -join ', ')): install it from Unity Hub, or pass -Unity <Unity.exe> or set MJOLNIR_UNITY"
}
