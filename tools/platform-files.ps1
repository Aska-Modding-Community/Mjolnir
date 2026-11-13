# What deploy.ps1 and package.ps1 both lay out in a Mjolnir folder: the SDK's assemblies and the private .NET runtime.
# Dot-sourced by both, after sdk-assemblies.ps1 (the list of the SDK's files). Keep this file ASCII: Windows PowerShell
# reads a script without a BOM in the system's code page.

# The newest <parent>\<x.y.z> folder of a major version, or $null.
function Get-LatestVersionDir([string] $parent, [int] $major) {
    if (-not (Test-Path $parent)) { return $null }
    Get-ChildItem $parent -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' -and ([version]$_.Name).Major -eq $major } |
        Sort-Object { [version]$_.Name } | Select-Object -Last 1
}

# The .NET install the private runtime is copied from: DOTNET_ROOT when it holds a runtime, else Program Files.
function Get-DotnetSource {
    foreach ($candidate in @($env:DOTNET_ROOT, (Join-Path $env:ProgramFiles 'dotnet'))) {
        if ($candidate -and (Test-Path (Join-Path $candidate 'host\fxr'))) { return $candidate }
    }
    return (Join-Path $env:ProgramFiles 'dotnet')
}

# The runtime Core runs on: host\fxr and Microsoft.NETCore.App of the major version its runtimeconfig.json names.
function Get-PrivateRuntime([string] $coreOut) {
    $config = [IO.File]::ReadAllText((Join-Path $coreOut 'Mjolnir.Core.runtimeconfig.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
    $major = ([version]$config.runtimeOptions.framework.version).Major
    $source = Get-DotnetSource
    $fxr = Get-LatestVersionDir (Join-Path $source 'host\fxr') $major
    $netcore = Get-LatestVersionDir (Join-Path $source 'shared\Microsoft.NETCore.App') $major
    if (-not $fxr -or -not $netcore) { throw ".NET $major runtime not found under $source (set DOTNET_ROOT to another install)" }
    return @{ Source = $source; Major = $major; Fxr = $fxr; NetCore = $netcore }
}

# Copies the runtime into <loader>\dotnet. With -OnlyIfMissing a version already there is left as it is.
function Copy-PrivateRuntime($runtime, [string] $loader, [switch] $OnlyIfMissing) {
    $fxrDst = Join-Path $loader "dotnet\host\fxr\$($runtime.Fxr.Name)"
    $netcoreDst = Join-Path $loader "dotnet\shared\Microsoft.NETCore.App\$($runtime.NetCore.Name)"
    New-Item -ItemType Directory -Force $fxrDst, $netcoreDst | Out-Null
    if (-not $OnlyIfMissing -or -not (Test-Path (Join-Path $fxrDst 'hostfxr.dll'))) {
        Copy-Item "$($runtime.Fxr.FullName)\*" $fxrDst -Recurse -Force
    }
    if (-not $OnlyIfMissing -or -not (Test-Path (Join-Path $netcoreDst 'coreclr.dll'))) {
        Copy-Item "$($runtime.NetCore.FullName)\*" $netcoreDst -Recurse -Force
    }
}

# Copies the SDK and its modules ($sdkAssemblies) from their projects' bin into core\. They sit next to Core: mods resolve
# them from the Default context, which probes Mjolnir\core. Without the SDK every mod would be treated as a library, and a
# module missing here would be loaded from a mod's folder as a second copy, so a missing file stops the script.
function Copy-SdkAssemblies($sdkAssemblies, [string] $repo, [string] $configuration, [string] $coreDst) {
    foreach ($sdk in $sdkAssemblies) {
        $out = Join-Path $repo "src\$($sdk.Project)\bin\$configuration\net10.0"
        foreach ($file in $sdk.Files) {
            $source = Join-Path $out $file
            if (-not (Test-Path $source)) { throw "$source is missing; build src\$($sdk.Project)" }
            # A file in a folder of the project's (a module's bundles\) goes into the same folder of core\.
            $target = Join-Path $coreDst $file
            New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
            Copy-Item $source $target -Force
        }
    }
}

# Removes Mjolnir.SDK*.dll, .pdb and .xml from core\ that no longer belong to the SDK (a module renamed or taken out):
# SdkModules loads every Mjolnir.SDK.<x>.dll it finds there. Nothing else in core\ is touched.
function Remove-StaleSdkFiles($sdkAssemblies, [string] $coreDst) {
    if (-not (Test-Path $coreDst)) { return }
    $known = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
    foreach ($sdk in $sdkAssemblies) { foreach ($file in $sdk.Files) { [void]$known.Add($file) } }
    Get-ChildItem $coreDst -File -Filter 'Mjolnir.SDK*' |
        Where-Object { $_.Extension -in '.dll', '.pdb', '.xml' -and -not $known.Contains($_.Name) } |
        ForEach-Object {
            Remove-Item $_.FullName -Force
            Write-Host "Removed $($_.Name) from ${coreDst}: no longer part of the SDK"
        }
}
