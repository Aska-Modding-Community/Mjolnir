<#
.SYNOPSIS
    Builds the mods' AssetBundles with the Mjolnir kit (unity\Mjolnir.ModKit) and puts each example's bundles into its
    bundles\ folder.

.DESCRIPTION
    The kit is a Unity project of the game's own version, 6000.3.12f1, with HDRP: a bundle built with another Unity does not
    load in the game. This script runs that Unity in batch mode (the kit's examples' models made again, then every bundle of
    Assets\Mods\<mod>\<bundle> built into Build\Bundles\<mod>) and copies Build\Bundles\<mod>\<bundle> into
    examples\<mod>\bundles\ for every example the kit has bundles for (src\<mod>\bundles\ for an SDK module's, tests\<mod>\
    for a test mod's). The editor must not have the kit open meanwhile
    (Unity locks a project); with the editor open, use its menu Mjolnir > Examples > Make the Models and Build Bundles, then
    run this script with -SkipBuild.

.PARAMETER Unity
    The Unity 6000.3.12f1 editor; the environment variable MJOLNIR_UNITY, else the one Unity Hub installed (tools\locations.ps1).

.PARAMETER SkipBuild
    Only copies bundles built already.
#>
param(
    [string]$Unity,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$kit = Join-Path $repo 'unity\Mjolnir.ModKit'
$output = Join-Path $kit 'Build\Bundles'

if (-not $SkipBuild) {
    . (Join-Path $PSScriptRoot 'locations.ps1')
    $Unity = Find-UnityEditor $Unity
    $log = Join-Path $repo 'build\unity\bundles.log'
    New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
    Write-Host "Building the bundles with $Unity (log: $log)"
    $process = Start-Process -FilePath $Unity -Wait -PassThru -NoNewWindow -ArgumentList @(
        '-batchmode', '-nographics', '-quit',
        '-projectPath', "`"$kit`"",
        '-executeMethod', 'Mjolnir.ModKit.Examples.ExampleBundles.BuildFromCommandLine',
        '-logFile', "`"$log`"")
    if ($process.ExitCode -ne 0) { throw "Unity failed ($($process.ExitCode)): see $log" }
    Select-String -Path $log -Pattern 'Mjolnir kit:' | ForEach-Object { Write-Host $_.Line }
}

foreach ($mod in Get-ChildItem $output -Directory) {
    # An example's bundles go to its folder, an SDK module's (Mjolnir.SDK.Animation's masks) to its project, a test mod's to its.
    $project = @("examples\$($mod.Name)", "src\$($mod.Name)", "tests\$($mod.Name)") | ForEach-Object { Join-Path $repo $_ } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $project) { continue }
    $bundles = Join-Path $project 'bundles'
    New-Item -ItemType Directory -Force $bundles | Out-Null
    # The bundles only: the folder's own manifest bundle and the .manifest texts are the build's records.
    foreach ($file in Get-ChildItem $mod.FullName -File | Where-Object { $_.Extension -eq '' -and $_.Name -ne $mod.Name }) {
        Copy-Item $file.FullName (Join-Path $bundles $file.Name) -Force
        Write-Host "$($mod.Name): bundles\$($file.Name) ($([math]::Round($file.Length / 1KB)) KB)"
    }
}
