<#
.SYNOPSIS
    Builds Mjolnir and lays it out inside the ASKA install.

.DESCRIPTION
    Builds version.dll (tools\build-native.cmd), Mjolnir.Core, Mjolnir.SDK and the SDK's modules (dotnet build), then
    copies:
      version.dll                         -> <game>\
      Mjolnir.Core + its dependencies     -> <game>\Mjolnir\core\
        (Il2CppInterop, HarmonyX, MonoMod, Mono.Cecil, Iced)
      Mjolnir.SDK + Tomlyn                -> <game>\Mjolnir\core\
      the SDK's modules (Mjolnir.SDK.UI, Mjolnir.SDK.Player, Mjolnir.SDK.Content)
                                          -> <game>\Mjolnir\core\  (each with its XML documentation)
      tools\Mjolnir.InteropGen            -> <game>\Mjolnir\interopgen\  (mirrored)
      private .NET runtime (host\fxr + shared\Microsoft.NETCore.App, matching major version)
                                          -> <game>\Mjolnir\dotnet\
    and creates <game>\Mjolnir\Mods\. The interop assemblies are not copied: Core generates <game>\Mjolnir\interop
    itself with the generator at the game's start when they are not for the game's build (InteropCache), as on a
    player's machine; build\interop (gen-interop.ps1) is what the projects compile against.

.PARAMETER GameDir
    ASKA install folder. Defaults to MJOLNIR_GAME_DIR, else the one Steam has (tools\locations.ps1).

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER SkipBuild
    Deploy the existing build outputs without rebuilding.

.PARAMETER SmokeMod
    Also build the test mods tests\Mjolnir.SmokeMod and tests\Mjolnir.FaultMod and deploy each to
    <game>\Mjolnir\Mods\<name>. Without this switch those folders are removed, so the test patches (infinite
    stamina) and the deliberate fault never stay behind by accident.

.PARAMETER UiLab
    Also build the in-game test of the SDK's modules, tests\Mjolnir.UiLab (two stacked mod windows, a dialog, F8, a cursor
    probe and a one-shot autopilot, whose "player" scenario checks Mjolnir.SDK.Player), and deploy it to
    <game>\Mjolnir\Mods\Mjolnir.UiLab. Without this switch that folder is removed.

.PARAMETER Examples
    Also build the example mods (examples\Mjolnir.InfiniteStamina, examples\Mjolnir.UiExample, examples\Mjolnir.PlayerExample,
    examples\Mjolnir.ItemsExample, examples\Mjolnir.VillagersExample, examples\Mjolnir.AltarExample, examples\Mjolnir.StatsExample,
    examples\Mjolnir.CheatInventory, examples\Mjolnir.EquipmentExample)
    and deploy each to <game>\Mjolnir\Mods\<name>.
    Without this switch those folders are removed. Their configs in <game>\Mjolnir\Config are kept.

.PARAMETER Uninstall
    Remove only <game>\version.dll; the Mjolnir folder (logs, mods, configs) is left in place.
#>
[CmdletBinding()]
param(
    [string] $GameDir,
    [ValidateSet('Release', 'Debug')] [string] $Configuration = 'Release',
    [switch] $SkipBuild,
    [switch] $SmokeMod,
    [switch] $UiLab,
    [switch] $Examples,
    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot 'locations.ps1')
$GameDir = Find-AskaDir $GameDir

$proxy = Join-Path $GameDir 'version.dll'
if ($Uninstall) {
    if (Test-Path $proxy) { Remove-Item $proxy; Write-Host "Removed $proxy" } else { Write-Host 'version.dll is not installed' }
    return
}

$preset = if ($Configuration -eq 'Debug') { 'x64-debug' } else { 'x64-release' }
$nativeOut = Join-Path $repo "build\native\$preset"
$coreOut = Join-Path $repo "src\Mjolnir.Core\bin\$Configuration\net10.0"
$generatorOut = Join-Path $repo "tools\Mjolnir.InteropGen\bin\$Configuration\net10.0"
# The SDK and its modules ($sdkAssemblies), listed once for deploy.ps1 and package.ps1, and what both lay out
# (platform-files.ps1: the SDK's copy, the private runtime).
. (Join-Path $PSScriptRoot 'sdk-assemblies.ps1')
. (Join-Path $PSScriptRoot 'platform-files.ps1')
# Mods built from this repo, deployed only on request; one not asked for is removed from the game.
$repoMods = @(
    @{ Dir = 'tests'; Name = 'Mjolnir.SmokeMod'; Deploy = [bool]$SmokeMod },
    @{ Dir = 'tests'; Name = 'Mjolnir.FaultMod'; Deploy = [bool]$SmokeMod },
    @{ Dir = 'tests'; Name = 'Mjolnir.UiLab'; Deploy = [bool]$UiLab },
    @{ Dir = 'examples'; Name = 'Mjolnir.InfiniteStamina'; Deploy = [bool]$Examples },
    @{ Dir = 'examples'; Name = 'Mjolnir.UiExample'; Deploy = [bool]$Examples },
    @{ Dir = 'examples'; Name = 'Mjolnir.PlayerExample'; Deploy = [bool]$Examples },
    @{ Dir = 'examples'; Name = 'Mjolnir.ItemsExample'; Deploy = [bool]$Examples },
    @{ Dir = 'examples'; Name = 'Mjolnir.VillagersExample'; Deploy = [bool]$Examples },
    @{ Dir = 'examples'; Name = 'Mjolnir.AltarExample'; Deploy = [bool]$Examples },
    @{ Dir = 'examples'; Name = 'Mjolnir.StatsExample'; Deploy = [bool]$Examples },
    @{ Dir = 'examples'; Name = 'Mjolnir.AnimationsExample'; Deploy = [bool]$Examples },
    @{ Dir = 'examples'; Name = 'Mjolnir.CheatInventory'; Deploy = [bool]$Examples },
    @{ Dir = 'examples'; Name = 'Mjolnir.EquipmentExample'; Deploy = [bool]$Examples }
)

if (-not $SkipBuild) {
    & cmd /c "`"$repo\tools\build-native.cmd`" $preset"
    if ($LASTEXITCODE -ne 0) { throw 'native build failed' }
    & dotnet build (Join-Path $repo 'src\Mjolnir.Core') -c $Configuration -nologo
    if ($LASTEXITCODE -ne 0) { throw 'Mjolnir.Core build failed' }
    & dotnet build (Join-Path $repo 'tools\Mjolnir.InteropGen') -c $Configuration -nologo
    if ($LASTEXITCODE -ne 0) { throw 'Mjolnir.InteropGen build failed' }
    foreach ($sdk in $sdkAssemblies) {
        & dotnet build (Join-Path $repo "src\$($sdk.Project)") -c $Configuration -nologo
        if ($LASTEXITCODE -ne 0) { throw "$($sdk.Project) build failed" }
    }
    foreach ($mod in $repoMods | Where-Object { $_.Deploy }) {
        & dotnet build (Join-Path $repo "$($mod.Dir)\$($mod.Name)") -c $Configuration -nologo
        if ($LASTEXITCODE -ne 0) { throw "$($mod.Name) build failed" }
    }
}

# Private runtime: copied from the local .NET install (DOTNET_ROOT, else Program Files), matching the major version Core
# targets.
$runtime = Get-PrivateRuntime $coreOut

$loader = Join-Path $GameDir 'Mjolnir'
$coreDst = Join-Path $loader 'core'
foreach ($d in @($coreDst, (Join-Path $loader 'Mods'))) { New-Item -ItemType Directory -Force $d | Out-Null }

Copy-Item (Join-Path $nativeOut 'version.dll') $proxy -Force
Copy-Item (Join-Path $coreOut '*') $coreDst -Include '*.dll', '*.pdb', '*.json' -Force
Copy-SdkAssemblies $sdkAssemblies $repo $Configuration $coreDst
# A module that was renamed or taken out would still be loaded from core\ by the SDK.
Remove-StaleSdkFiles $sdkAssemblies $coreDst

# The generator has a folder of its own: its libraries (Cpp2IL, AsmResolver, Il2CppInterop.Generator) are not Core's and
# must not resolve for mods. Mirrored, so an updated set drops stale files.
$generatorDst = Join-Path $loader 'interopgen'
if (-not (Test-Path (Join-Path $generatorOut 'Mjolnir.InteropGen.exe'))) { throw "$generatorOut is missing; build tools\Mjolnir.InteropGen" }
& robocopy $generatorOut $generatorDst /MIR /NJH /NJS /NFL /NDL /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "copying $generatorOut failed (robocopy exit code $LASTEXITCODE)" }

foreach ($mod in $repoMods) {
    $modDst = Join-Path $loader "Mods\$($mod.Name)"
    if ($mod.Deploy) {
        New-Item -ItemType Directory -Force $modDst | Out-Null
        $modOut = Join-Path $repo "$($mod.Dir)\$($mod.Name)\bin\$Configuration\net10.0"
        # icon.png: the mod's picture in the Mods window; lang\: its texts (Mjolnir.SDK.Content); items\: its item
        # pictures and bundles\: its AssetBundles, models among them (Mjolnir.SDK.Content). Only what the project ships now:
        # an old picture or file is not kept.
        Remove-Item (Join-Path $modDst 'icon.png') -Force -ErrorAction SilentlyContinue
        foreach ($folder in 'lang', 'items', 'bundles') { Remove-Item (Join-Path $modDst $folder) -Recurse -Force -ErrorAction SilentlyContinue }
        Copy-Item (Join-Path $modOut '*') $modDst -Include "$($mod.Name).dll", "$($mod.Name).pdb", 'icon.png' -Force
        foreach ($folder in 'lang', 'items', 'bundles') {
            if (Test-Path (Join-Path $modOut $folder)) { Copy-Item (Join-Path $modOut $folder) $modDst -Recurse -Force }
        }
    } elseif (Test-Path $modDst) {
        Remove-Item $modDst -Recurse -Force
        Write-Host "Removed the mod $($mod.Name) from $modDst"
    }
}
Copy-PrivateRuntime $runtime $loader -OnlyIfMissing

Write-Host "Deployed to $GameDir"
Write-Host "  version.dll  <- $nativeOut"
Write-Host "  core         <- $coreOut"
Write-Host "  sdk          <- $(($sdkAssemblies | ForEach-Object { "src\$($_.Project)" }) -join ', ')"
Write-Host "  interopgen   <- tools\Mjolnir.InteropGen (Mjolnir\interop: generated by Core when not for the game's build)"
$deployed = @($repoMods | Where-Object { $_.Deploy } | ForEach-Object { "$($_.Dir)\$($_.Name)" })
if ($deployed.Count -gt 0) { Write-Host "  mods         <- $($deployed -join ', ')" }
Write-Host "  dotnet       <- $($runtime.Source): host\fxr\$($runtime.Fxr.Name), Microsoft.NETCore.App\$($runtime.NetCore.Name)"
Write-Host "Log: $(Join-Path $loader 'LogOutput.log')"
