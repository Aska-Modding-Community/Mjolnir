<#
.SYNOPSIS
    Generates Il2CppInterop interop assemblies for ASKA (step 0).

.DESCRIPTION
    1. Reads the Unity version from UnityPlayer.dll (ProductVersion, e.g. 6000.3.12f1).
    2. Downloads the matching Unity base libraries for unstripping once:
         https://unity.bepinex.dev/libraries/<Major.Minor.Build>.zip -> build\unity-libs\<ver>\
    3. Builds tools\Mjolnir.InteropGen and runs it:
         Cpp2IL dummy assemblies -> Il2CppInterop.Generator -> <OutDir>\*.dll
    4. The generator verifies the result (SSSGame.*, PlayerCharacter, IStaminaUser) and fails otherwise.

.PARAMETER GameDir
    ASKA install folder. Defaults to MJOLNIR_GAME_DIR, else the one Steam has (tools\locations.ps1).

.PARAMETER OutDir
    Output folder for the interop assemblies. Defaults to build\interop in the repository.

.PARAMETER NoUnstrip
    Skip the Unity base libraries (no download, no unstripping).

.PARAMETER DummyDir
    Also write the Cpp2IL dummy assemblies to this folder (for inspection).
#>
[CmdletBinding()]
param(
    [string] $GameDir,
    [string] $OutDir,
    [switch] $NoUnstrip,
    [string] $DummyDir
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repo 'build\interop' }
. (Join-Path $PSScriptRoot 'locations.ps1')
$GameDir = Find-AskaDir $GameDir

$unityPlayer = Join-Path $GameDir 'UnityPlayer.dll'
if (-not (Test-Path $unityPlayer)) { throw "UnityPlayer.dll not found in $GameDir" }

# 6000.3.12f1 (fca03ac9b0d5) -> 6000.3.12
$productVersion = (Get-Item $unityPlayer).VersionInfo.ProductVersion
if ($productVersion -notmatch '^(\d+\.\d+\.\d+)') { throw "cannot parse Unity version from '$productVersion'" }
$unityVersion = $Matches[1]
Write-Host "Unity version: $productVersion -> base libraries $unityVersion"

$genArgs = @('--game', $GameDir, '--out', $OutDir)

if (-not $NoUnstrip) {
    $libsRoot = Join-Path $repo 'build\unity-libs'
    $libsDir = Join-Path $libsRoot $unityVersion
    if (-not (Test-Path (Join-Path $libsDir 'UnityEngine.CoreModule.dll'))) {
        New-Item -ItemType Directory -Force $libsRoot | Out-Null
        $zip = Join-Path $libsRoot "$unityVersion.zip"
        if (-not (Test-Path $zip)) {
            # Downloaded under another name and renamed once complete: an interrupted download leaves no zip behind
            # that a later run would take for the archive.
            $url = "https://unity.bepinex.dev/libraries/$unityVersion.zip"
            $partial = "$zip.download"
            Write-Host "Downloading $url"
            try {
                Invoke-WebRequest -Uri $url -OutFile $partial -UseBasicParsing
                Move-Item -LiteralPath $partial -Destination $zip -Force
            }
            finally {
                if (Test-Path $partial) { Remove-Item -LiteralPath $partial -Force }
            }
        }
        # Unpacked into a fresh folder beside it (Expand-Archive refuses entries that would land outside it) and moved
        # into place only when it holds the libraries, so a half-unpacked folder never passes for them.
        $unpacked = "$libsDir.tmp"
        if (Test-Path $unpacked) { Remove-Item -LiteralPath $unpacked -Recurse -Force }
        try {
            Expand-Archive -LiteralPath $zip -DestinationPath $unpacked
            if (-not (Test-Path (Join-Path $unpacked 'UnityEngine.CoreModule.dll'))) {
                throw "$zip holds no UnityEngine.CoreModule.dll; remove it and rerun"
            }
            if (Test-Path $libsDir) { Remove-Item -LiteralPath $libsDir -Recurse -Force }
            Move-Item -LiteralPath $unpacked -Destination $libsDir
        }
        finally {
            if (Test-Path $unpacked) { Remove-Item -LiteralPath $unpacked -Recurse -Force }
        }
    }
    $genArgs += @('--unity-libs', $libsDir)
}
if ($DummyDir) { $genArgs += @('--dummy', $DummyDir) }

$project = Join-Path $repo 'tools\Mjolnir.InteropGen'
& dotnet build $project -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Mjolnir.InteropGen build failed' }

& (Join-Path $project 'bin\Release\net10.0\Mjolnir.InteropGen.exe') @genArgs
if ($LASTEXITCODE -ne 0) { throw "interop generation failed (exit $LASTEXITCODE)" }
Write-Host "Interop assemblies: $OutDir"
