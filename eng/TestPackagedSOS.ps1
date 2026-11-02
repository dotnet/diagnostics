# Licensed to the .NET Foundation under one or more agreements.
# The .NET Foundation licenses this file to you under the MIT license.

#requires -Version 7.0

<#
.SYNOPSIS
Prepares installed SOS layouts and optionally runs their Windows x64 smoke shards locally.
.NOTES
Requires prebuilt Release (or Debug) harnesses and debuggees, and eng/InstallRuntimes.proj runtimes.
The staged payload excludes Bin-layout tools and disallows on-demand builds.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    [Parameter(Mandatory)]
    [string] $BundleArchive,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $PrepareOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (!$IsWindows -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'The packaged SOS smoke leg currently supports Windows x64 only.'
}

$repo = Split-Path $PSScriptRoot
$dotnet = Join-Path $repo '.dotnet\dotnet.exe'
$runtimeRoot = Join-Path $repo 'artifacts\dotnet-test'
$testDotnet = Join-Path $runtimeRoot 'dotnet.exe'
$packages = (Resolve-Path -LiteralPath $PackageDirectory).Path
$archive = (Resolve-Path -LiteralPath $BundleArchive).Path
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
    SOSHARNESS_DUMP_OUTPUT = ''
    SOSHARNESS_DUMP_INPUT = ''
    SOSHARNESS_CDAC_DIR = ''
    SOSHARNESS_USECDAC = ''
    HELIX_WORKITEM_UPLOAD_ROOT = ''
}
$previousEnvironment = @{}
$failures = [Collections.Generic.List[string]]::new()
try {
    if (!$PrepareOnly) {
        foreach ($name in $environment.Keys) {
            $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
            [Environment]::SetEnvironmentVariable($name, $environment[$name])
        }
    }

    foreach ($distribution in @('NuGet', 'SingleFile')) {
        $layout = Join-Path $root $distribution
        $prepare = @{
            Distribution = $distribution
            OutputDirectory = $layout
            DotNet = $dotnet
        }
        if ($distribution -eq 'NuGet') {
            $prepare.PackageDirectory = $packages
            $prepare.PackageVersion = $version
        } else {
            $prepare.BundleArchive = $archive
        }
        & (Join-Path $PSScriptRoot 'PrepareSOSTestLayout.ps1') @prepare

        if ($PrepareOnly) {
            continue
        }

        $payload = Join-Path $root "payloads\$distribution"
        & $dotnet msbuild (Join-Path $repo 'src\tests\SOS.Tests\SOS.Tests.csproj') -nologo -restore `
            -t:StageSOSHelixPayload "-p:Configuration=$Configuration" -p:TargetArch=x64 -p:TargetRid=win-x64 `
            "-p:SOSTestToolLayout=$distribution" "-p:SOSTestToolRoot=$layout" "-p:SOSHelixPayloadDir=$payload"
        if ($LASTEXITCODE -ne 0) {
            throw "Staging the $distribution SOS harness failed (exit $LASTEXITCODE)."
        }

        $results = Join-Path $root "results\$distribution"
        New-Item -ItemType Directory -Path $results | Out-Null
        $env:HELIX_WORKITEM_ROOT = $payload
        $env:HELIX_WORKITEM_UPLOAD_ROOT = $results
        $identity = "SOS_win-x64_$Configuration-$distribution-Net10"
        & (Join-Path $payload 'runtests.cmd') --helix-work-item $identity
        if ($LASTEXITCODE -ne 0) {
            $failures.Add("$identity (exit $LASTEXITCODE)")
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

if ($failures.Count -ne 0) {
    throw "Packaged SOS smoke tests failed: $($failures -join ', '). Results: '$root\results'."
}
