# Licensed to the .NET Foundation under one or more agreements.
# The .NET Foundation licenses this file to you under the MIT license.

#requires -Version 7.0

<#
.SYNOPSIS
Runs Windows x64 SOS smoke tests against finalized NuGet packages and a shipped single-file archive.
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
    [string] $Configuration = 'Release'
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

$payload = Join-Path $root 'payload'
& $dotnet msbuild (Join-Path $repo 'src\tests\SOS.Tests\SOS.Tests.csproj') -nologo -restore `
    -t:StageSOSHelixPayload "-p:Configuration=$Configuration" -p:TargetArch=x64 -p:TargetRid=win-x64 `
    -p:SOSTestToolLayout=Packaged "-p:SOSHelixPayloadDir=$payload"
if ($LASTEXITCODE -ne 0) {
    throw "Staging the prebuilt SOS harness failed (exit $LASTEXITCODE)."
}

New-Item -ItemType Junction -Path (Join-Path $root 'dotnet-cli') -Target $runtimeRoot | Out-Null
$testDll = Join-Path $payload "artifacts\bin\SOS.Tests\$Configuration\net10.0\SOS.Tests.dll"
$environment = @{
    HELIX_WORKITEM_ROOT = $payload
    HELIX_CORRELATION_PAYLOAD = $root
    DOTNET_ROOT = $runtimeRoot
    DOTNET_ROOT_X64 = $runtimeRoot
    DOTNET_MULTILEVEL_LOOKUP = '0'
    SOSHARNESS_ONLY_HOSTS = 'Cdb,DotnetDump'
    SOSHARNESS_ONLY_FLAVORS = 'Core,SingleFile'
    SOSHARNESS_ONLY_COREVERSIONS = 'Net10'
    SOSHARNESS_ONLY_DAC = 'Legacy'
    SOSHARNESS_ONLY_HOSTRUNTIME = 'Managed'
    SOSHARNESS_ONLY_LIVENESS = 'Dump,Live'
    SOSHARNESS_ONLY_GCTYPE = 'Workstation'
    SOSHARNESS_ONLY_DUMPKIND = 'Heap'
    SOSHARNESS_TOOL_LAYOUT = ''
    SOSHARNESS_TOOL_ROOT = ''
    HELIX_WORKITEM_UPLOAD_ROOT = ''
}
$previousEnvironment = @{}
$failures = [Collections.Generic.List[string]]::new()
$testClasses = [ordered]@{
    RuntimeInfoTests = 18
    DumpHeapObjectsTests = 6
    LiveBpmdTests = 4
}
try {
    foreach ($name in $environment.Keys) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $environment[$name])
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

        $results = Join-Path $root "results\$distribution"
        New-Item -ItemType Directory -Path $results | Out-Null
        $env:SOSHARNESS_TOOL_LAYOUT = $distribution
        $env:SOSHARNESS_TOOL_ROOT = $layout
        $env:HELIX_WORKITEM_UPLOAD_ROOT = $results
        foreach ($entry in $testClasses.GetEnumerator()) {
            $class = $entry.Key
            $identity = "SOS.Tests-$distribution-win-x64-$Configuration-Net10-$class"
            & $testDotnet $testDll --filter-class "SOS.Tests.$class" --parallel none `
                --minimum-expected-tests $entry.Value --fail-skips on --no-progress --auto-reporters off `
                --results-directory $results --report-xunit `
                --report-xunit-filename "$identity.testResults.xml" 2>&1 |
                Tee-Object -FilePath (Join-Path $results "$identity.log")
            if ($LASTEXITCODE -ne 0) {
                $failures.Add("$identity (exit $LASTEXITCODE)")
            }
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
