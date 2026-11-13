<#
.SYNOPSIS
    Builds the release archive players install: artifacts\package\Mjolnir-<version>.zip and its .sha256.

.DESCRIPTION
    Builds version.dll, Mjolnir.Core, Mjolnir.SDK with its modules and the interop generator in Release, lays them out
    as they go into the game folder, and zips that:
      version.dll                         the proxy (game root)
      Mjolnir\core\                       Core and its libraries, the SDK and its modules with their XML documentation
      Mjolnir\interopgen\                 the interop generator (only the win-x64 native libraries)
      Mjolnir\dotnet\                     the private .NET runtime (host\fxr + shared\Microsoft.NETCore.App)
      Mjolnir\Mods\                       empty
      Mjolnir\licenses\                   THIRD-PARTY-NOTICES.txt and every third-party license, see below
      Mjolnir\LICENSE.txt, README.txt    Mjolnir's MIT license; how to install and uninstall (the game's root gets
                                          nothing but version.dll and the Mjolnir folder)
    Never in it: the interop assemblies (Core generates them on the player's machine, InteropCache), the test mods and
    the examples, configs and logs.

    Licenses: every NuGet package in the deps.json of Core, the SDK and the generator, with the license file the package
    carries, else its SPDX expression's text from tools\licenses (the package's copyright put in), plus the .NET runtime,
    MinHook and the Capstone engine the generator's disassembler bundles. A package whose license or source cannot be
    found stops the script, so no archive goes out without one.

    The staging folder is build\package (intermediate, git-ignored); the archive goes to artifacts\package, the place of
    finished outputs (the .NET SDK's artifacts layout), also git-ignored.

.PARAMETER SkipBuild
    Package the existing Release outputs without rebuilding.

.PARAMETER OutDir
    Where the archive goes. Defaults to artifacts\package in the repository.
#>
[CmdletBinding()]
param(
    [switch] $SkipBuild,
    [string] $OutDir
)

$ErrorActionPreference = 'Stop'
# Text files are read as UTF-8 whatever their BOM: Windows PowerShell's Get-Content takes a file without one for the
# system's code page, which garbles a license's (c) sign or a Russian line.
function Read-Text([string] $path) { [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) }
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repo 'artifacts\package' }
. (Join-Path $PSScriptRoot 'sdk-assemblies.ps1')
. (Join-Path $PSScriptRoot 'platform-files.ps1')

# The platform's version, the one Core, the SDK and its modules carry (Directory.Build.props).
$props = (Read-Text (Join-Path $repo 'Directory.Build.props'))
if ($props -notmatch '<MjolnirPlatformVersion>([^<]+)</MjolnirPlatformVersion>') { throw 'MjolnirPlatformVersion not found in Directory.Build.props' }
$version = $Matches[1]
$name = "Mjolnir-$version"

$nativeOut = Join-Path $repo 'build\native\x64-release'
$coreOut = Join-Path $repo 'src\Mjolnir.Core\bin\Release\net10.0'
$generatorOut = Join-Path $repo 'tools\Mjolnir.InteropGen\bin\Release\net10.0'

if (-not $SkipBuild) {
    & cmd /c "`"$repo\tools\build-native.cmd`" x64-release"
    if ($LASTEXITCODE -ne 0) { throw 'native build failed' }
    $projects = @('src\Mjolnir.Core', 'tools\Mjolnir.InteropGen') + @($sdkAssemblies | ForEach-Object { "src\$($_.Project)" })
    foreach ($project in $projects) {
        & dotnet build (Join-Path $repo $project) -c Release -nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "$project build failed" }
    }
}

# ---- Lay out the game folder ----
$stage = Join-Path $repo "build\package\$name"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$loader = Join-Path $stage 'Mjolnir'
$coreDst = Join-Path $loader 'core'
$generatorDst = Join-Path $loader 'interopgen'
$licensesDst = Join-Path $loader 'licenses'
foreach ($d in $coreDst, $generatorDst, $licensesDst, (Join-Path $loader 'Mods')) { New-Item -ItemType Directory -Force $d | Out-Null }

Copy-Item (Join-Path $nativeOut 'version.dll') $stage
Copy-Item (Join-Path $coreOut '*') $coreDst -Include '*.dll', '*.pdb', '*.json'
Copy-SdkAssemblies $sdkAssemblies $repo 'Release' $coreDst

# The generator's own folder; of its disassembler's native libraries only the game's platform.
Copy-Item (Join-Path $generatorOut '*') $generatorDst -Recurse -Exclude 'runtimes'
$capstone = Join-Path $generatorOut 'runtimes\win-x64'
if (Test-Path $capstone) {
    New-Item -ItemType Directory -Force (Join-Path $generatorDst 'runtimes') | Out-Null
    Copy-Item $capstone (Join-Path $generatorDst 'runtimes') -Recurse
}

# The private runtime, matching the major version Core targets (as deploy.ps1 takes it: DOTNET_ROOT, else Program Files).
$runtime = Get-PrivateRuntime $coreOut
$dotnetSrc = $runtime.Source
$netcore = $runtime.NetCore
Copy-PrivateRuntime $runtime $loader

# ---- Licenses ----
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$templates = Join-Path $PSScriptRoot 'licenses'
# Packages whose nuspec names no source; LGPL asks for the way to the source of what is shipped.
$knownSources = @{ 'Il2CppInterop' = 'https://github.com/BepInEx/Il2CppInterop' }

$packages = @{}
$depsFiles = @(
    @{ File = Join-Path $coreOut 'Mjolnir.Core.deps.json'; Where = 'core' },
    @{ File = Join-Path $repo 'src\Mjolnir.SDK\bin\Release\net10.0\Mjolnir.SDK.deps.json'; Where = 'core' },
    @{ File = Join-Path $generatorOut 'Mjolnir.InteropGen.deps.json'; Where = 'interopgen' }
)
foreach ($deps in $depsFiles) {
    $json = (Read-Text $deps.File) | ConvertFrom-Json
    foreach ($library in $json.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        if (-not $packages.ContainsKey($library.Name)) { $packages[$library.Name] = New-Object System.Collections.Generic.SortedSet[string] }
        [void]$packages[$library.Name].Add($deps.Where)
    }
}

$notices = New-Object System.Collections.Generic.List[string]
$expressionsUsed = New-Object System.Collections.Generic.HashSet[string]
foreach ($key in ($packages.Keys | Sort-Object)) {
    $id, $packageVersion = $key -split '/', 2
    $dir = Join-Path $nugetRoot "$($id.ToLowerInvariant())\$($packageVersion.ToLowerInvariant())"
    $nuspecPath = Get-ChildItem $dir -Filter '*.nuspec' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $nuspecPath) { throw "no nuspec for $key in $dir" }
    [xml]$nuspec = (Read-Text $nuspecPath.FullName)
    $meta = $nuspec.package.metadata
    $license = $meta.license
    $copyright = if ($meta.copyright) { $meta.copyright } else { $meta.authors }
    if ($copyright -notmatch "^(Copyright|\(c\)|$([char]0x00A9))") { $copyright = "Copyright (c) $copyright" }
    $source = if ($meta.projectUrl) { $meta.projectUrl } elseif ($meta.repository -and $meta.repository.url) { $meta.repository.url } else { $null }
    if (-not $source) {
        $known = $knownSources.Keys | Where-Object { $id -like "$_*" } | Select-Object -First 1
        if ($known) { $source = $knownSources[$known] } else { throw "no source address for ${key}: add it to `$knownSources" }
    }
    if ($meta.repository -and $meta.repository.commit) { $source = "$source (commit $($meta.repository.commit))" }

    $text = $null; $kind = $null
    $own = Get-ChildItem $dir -File | Where-Object { $_.Name -match '^(licen[cs]e|copying)(\.(txt|md))?$' } | Select-Object -First 1
    if ($license -and $license.type -eq 'file') { $text = (Read-Text (Join-Path $dir $license.'#text')); $kind = 'see the file' }
    elseif ($own) { $text = (Read-Text $own.FullName) }
    if ($license -and $license.type -eq 'expression') {
        $kind = $license.'#text'
        if (-not $text) {
            $template = Join-Path $templates "$kind.txt"
            if (-not (Test-Path $template)) { throw "$key is $kind, and tools\licenses has no $kind.txt" }
            $text = ((Read-Text $template)) -replace 'Copyright \(c\) <year> <[^>]+>', $copyright
            [void]$expressionsUsed.Add($kind)
        }
    }
    if (-not $text) { throw "no license found for $key" }
    if (-not $kind -or $kind -eq 'see the file') {
        # A license given as a file: named by its first line where that is a known one.
        $kind = if ($text -match '^\W*MIT License') { 'MIT' } elseif ($text -match 'GNU LESSER GENERAL PUBLIC LICENSE') { 'LGPL' } else { 'see the file' }
    }
    Set-Content (Join-Path $licensesDst "$id.txt") $text -Encoding UTF8
    $notices.Add(('{0,-45} {1,-26} {2,-14} {3,-16} {4}' -f $id, $packageVersion, $kind, ($packages[$key] -join ','), $source))
}

# What is not a NuGet package: the runtime, the native hook library, the disassembler's engine.
$minhook = Join-Path $nativeOut '_deps\minhook-src\LICENSE.txt'
$others = @(
    @{ Id = 'Microsoft.NETCore.App'; Version = $netcore.Name; Kind = 'MIT'; Where = 'dotnet'; Source = 'https://github.com/dotnet/runtime'; Files = @((Join-Path $dotnetSrc 'LICENSE.txt'), (Join-Path $dotnetSrc 'ThirdPartyNotices.txt')) },
    @{ Id = 'MinHook'; Version = '1.3.4'; Kind = 'BSD-2-Clause'; Where = 'version.dll'; Source = 'https://github.com/TsudaKageyu/minhook'; Files = @($minhook) },
    @{ Id = 'Capstone'; Version = 'engine'; Kind = 'BSD-3-Clause'; Where = 'interopgen'; Source = 'https://github.com/capstone-engine/capstone'; Files = @((Join-Path $templates 'Capstone.txt')) }
)
foreach ($other in $others) {
    $parts = foreach ($file in $other.Files) {
        if (-not (Test-Path $file)) { throw "the license of $($other.Id) is missing: $file" }
        (Read-Text $file)
    }
    Set-Content (Join-Path $licensesDst "$($other.Id).txt") ($parts -join "`r`n`r`n") -Encoding UTF8
    $notices.Add(('{0,-45} {1,-26} {2,-14} {3,-16} {4}' -f $other.Id, $other.Version, $other.Kind, $other.Where, $other.Source))
}
# The LGPL is the GPL's terms with additional permissions: its text comes with the GPL's.
if ($expressionsUsed.Contains('LGPL-3.0-only')) { Copy-Item (Join-Path $templates 'GPL-3.0-only.txt') (Join-Path $licensesDst 'GPL-3.0.txt') }

$header = @(
    "Mjolnir $version ships these third-party components. Each one's license is in <name>.txt next to this file.",
    'Mjolnir itself is MIT (Mjolnir\LICENSE.txt).',
    '',
    'Il2CppInterop (LGPL-3.0, GPL-3.0.txt holds the GPL it builds on) ships unmodified as its own assemblies in',
    'Mjolnir\core and Mjolnir\interopgen; they can be replaced by a build of its source at the address given.',
    '',
    ('{0,-45} {1,-26} {2,-14} {3,-16} {4}' -f 'Component', 'Version', 'License', 'In', 'Source'),
    ('-' * 140)
)
Set-Content (Join-Path $licensesDst 'THIRD-PARTY-NOTICES.txt') ($header + $notices) -Encoding UTF8
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $loader 'LICENSE.txt')

# The README's text is its own UTF-8 file: Windows PowerShell reads a script without a BOM in the system's code page,
# so Russian written here would reach the archive garbled. The script itself stays ASCII.
$utf8 = New-Object System.Text.UTF8Encoding $false
$readme = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'package\README.txt'), $utf8).Replace('{version}', $version)
[IO.File]::WriteAllText((Join-Path $loader 'README.txt'), ($readme -replace "`r?`n", "`r`n"), $utf8)

# ---- The archive ----
New-Item -ItemType Directory -Force $OutDir | Out-Null
$zip = Join-Path $OutDir "$name.zip"
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
# Entry by entry: Windows PowerShell's ZipFile.CreateFromDirectory names entries with backslashes, which unzip tools
# outside Windows (and some on it) take as part of a file name; the zip format's separator is '/'.
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    $root = (Resolve-Path $stage).Path.TrimEnd('\') + '\'
    foreach ($item in Get-ChildItem $stage -Recurse | Sort-Object FullName) {
        $entry = $item.FullName.Substring($root.Length).Replace('\', '/')
        if ($item.PSIsContainer) {
            # Only an empty folder needs an entry of its own (Mjolnir/Mods/); the others come with their files.
            if (-not (Get-ChildItem $item.FullName -Force | Select-Object -First 1)) { [void]$archive.CreateEntry("$entry/") }
        } else {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $item.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
        }
    }
} finally {
    $archive.Dispose()
}
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content "$zip.sha256" "$hash  $name.zip" -Encoding ASCII

$unpacked = (Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host "Packaged $zip"
Write-Host ("  {0:N1} MB, {1:N1} MB unpacked; {2} third-party licenses" -f ((Get-Item $zip).Length / 1MB), ($unpacked / 1MB), $notices.Count)
Write-Host "  SHA-256 $hash"
