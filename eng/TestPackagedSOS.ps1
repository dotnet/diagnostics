# Licensed to the .NET Foundation under one or more agreements.
# The .NET Foundation licenses this file to you under the MIT license.

#requires -Version 7.0

<#
.SYNOPSIS
Prepares a NuGet-installed SOS layout and optionally runs its full Windows x64 test matrix locally.
.NOTES
Requires prebuilt Release (or Debug) harnesses and debuggees, and eng/InstallRuntimes.proj runtimes.
The staged payload excludes Bin-layout tools and disallows on-demand builds.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $PrepareOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (!$IsWindows -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'The packaged SOS tests currently support Windows x64 only.'
}

$repo = Split-Path $PSScriptRoot
$dotnet = Join-Path $repo '.dotnet\dotnet.exe'
$runtimeRoot = Join-Path $repo 'artifacts\dotnet-test'
$testDotnet = Join-Path $runtimeRoot 'dotnet.exe'
$packages = (Resolve-Path -LiteralPath $PackageDirectory).Path
$root = [IO.Path]::GetFullPath($OutputDirectory, $PWD.ProviderPath)
if (Test-Path -LiteralPath $root) {
    throw "Output directory '$root' already exists. Choose a new directory."
}

$sosPackages = @(Get-ChildItem -LiteralPath $packages -Filter 'dotnet-sos.*.nupkg' -File)
if ($sosPackages.Count -ne 1) {
    throw "Expected exactly one dotnet-sos package in '$packages', found $($sosPackages.Count)."
}
$version = $sosPackages[0].BaseName.Substring('dotnet-sos.'.Length)
foreach ($file in @($dotnet, $testDotnet, (Join-Path $packages "dotnet-dump.$version.nupkg"))) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Required packaged SOS test input '$file' was not found."
    }
}

if (!$PrepareOnly) {
    New-Item -ItemType Directory -Path $root | Out-Null
    New-Item -ItemType Junction -Path (Join-Path $root 'dotnet-cli') -Target $runtimeRoot | Out-Null
}

$environment = @{
    HELIX_WORKITEM_ROOT = ''
    HELIX_CORRELATION_PAYLOAD = $root
    DOTNET_ROOT = $runtimeRoot
    DOTNET_ROOT_X64 = $runtimeRoot
    DOTNET_MULTILEVEL_LOOKUP = '0'
    SOSHARNESS_ONLY_HOSTS = ''
    SOSHARNESS_ONLY_FLAVORS = ''
    SOSHARNESS_ONLY_COREVERSIONS = ''
    SOSHARNESS_ONLY_DAC = ''
    SOSHARNESS_ONLY_HOSTRUNTIME = ''
    SOSHARNESS_ONLY_LIVENESS = ''
    SOSHARNESS_ONLY_GCTYPE = ''
    SOSHARNESS_ONLY_DUMPKIND = ''
    SOSHARNESS_TOOL_LAYOUT = ''
    SOSHARNESS_TOOL_ROOT = ''
    SOSHARNESS_CDAC_DIR = ''
    SOSHARNESS_USECDAC = ''
    HELIX_WORKITEM_UPLOAD_ROOT = ''
}
$previousEnvironment = @{}
try {
    if (!$PrepareOnly) {
        foreach ($name in $environment.Keys) {
            $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
            [Environment]::SetEnvironmentVariable($name, $environment[$name])
        }
    }

    $layout = Join-Path $root 'Packaged'
    & (Join-Path $PSScriptRoot 'PrepareSOSTestLayout.ps1') `
        -PackageDirectory $packages -PackageVersion $version -OutputDirectory $layout -DotNet $dotnet

    if (!$PrepareOnly) {
        $project = Join-Path $repo 'src\tests\SOS.Tests\SOS.Tests.csproj'
        $runtimeMetadata = & $dotnet msbuild $project -nologo "-p:Configuration=$Configuration" -getItem:RuntimeTestVersions
        if ($LASTEXITCODE -ne 0) {
            throw "Reading the SOS runtime matrix failed (exit $LASTEXITCODE)."
        }
        $runtimeVersions = @(($runtimeMetadata | ConvertFrom-Json).Items.RuntimeTestVersions)
        if ($runtimeVersions.Count -eq 0) {
            throw 'RuntimeTestVersions must contain at least one runtime for packaged SOS testing.'
        }
        $shards = @(
            foreach ($runtime in $runtimeVersions) {
                if ($runtime.TargetFramework -notmatch '^net\d+\.0$') {
                    throw "Invalid runtime TargetFramework '$($runtime.TargetFramework)' for packaged SOS testing."
                }
                $runtime.TargetFramework.Replace('net', 'Net').Replace('.0', '')
            }
            'Framework'
        )

        $payload = Join-Path $root 'payloads\Packaged'
        & $dotnet msbuild $project -nologo -restore `
            -t:StageSOSHelixPayload "-p:Configuration=$Configuration" -p:TargetArch=x64 -p:TargetRid=win-x64 `
            -p:SOSTestToolLayout=Packaged "-p:SOSTestToolRoot=$layout" "-p:SOSHelixPayloadDir=$payload"
        if ($LASTEXITCODE -ne 0) {
            throw "Staging the Packaged SOS harness failed (exit $LASTEXITCODE)."
        }

        $results = Join-Path $root 'results\Packaged'
        New-Item -ItemType Directory -Path $results | Out-Null
        $env:HELIX_WORKITEM_ROOT = $payload
        $env:HELIX_WORKITEM_UPLOAD_ROOT = $results
        $failures = @()
        foreach ($shard in $shards) {
            $identity = "SOS_win-x64_$Configuration-Packaged-$shard"
            & (Join-Path $payload 'runtests.cmd') --helix-work-item $identity
            if ($LASTEXITCODE -ne 0) {
                $failures += "$identity (exit $LASTEXITCODE)"
            }
        }
        if ($failures.Count -ne 0) {
            throw "Packaged SOS tests failed: $($failures -join ', '). Results: '$results'."
        }
    }
} finally {
    foreach ($name in $previousEnvironment.Keys) {
        $value = $previousEnvironment[$name]
        if ($null -eq $value) {
            [Environment]::SetEnvironmentVariable($name, [NullString]::Value)
        } else {
            [Environment]::SetEnvironmentVariable($name, $value)
        }
    }
}
