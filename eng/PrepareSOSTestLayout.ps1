# Licensed to the .NET Foundation under one or more agreements.
# The .NET Foundation licenses this file to you under the MIT license.

#requires -Version 7.0

<#
.SYNOPSIS
Prepares an isolated NuGet or SingleFile tool layout for local SOS harness testing.
.EXAMPLE
./eng/PrepareSOSTestLayout.ps1 -Distribution NuGet -PackageDirectory ./artifacts/packages/Release/Shipping -PackageVersion 10.0.0 -OutputDirectory ./artifacts/tmp/sos-nuget
.EXAMPLE
./eng/PrepareSOSTestLayout.ps1 -Distribution SingleFile -BundleArchive ./diagnostic-tools-win-x64.zip -OutputDirectory ./artifacts/tmp/sos-singlefile
.NOTES
Run on the target OS/architecture. The input tools must support dotnet-sos install
--install-directory and --no-lldbinit. The output directory must not already exist.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('NuGet', 'SingleFile')]
    [string] $Distribution,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [string] $PackageDirectory,
    [string] $PackageVersion,
    [string] $BundleArchive,
    [string] $DotNet
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Distribution -eq 'NuGet') {
    $Distribution = 'NuGet'
    if (!$PackageDirectory -or !$PackageVersion -or $BundleArchive) {
        throw 'NuGet requires -PackageDirectory and -PackageVersion, without -BundleArchive.'
    }
    if ($PackageVersion.Contains('*') -or $PackageVersion.Contains('[') -or $PackageVersion.Contains(']')) {
        throw '-PackageVersion must be an exact version, not a range or wildcard.'
    }
    $source = (Resolve-Path -LiteralPath $PackageDirectory).Path
} else {
    $Distribution = 'SingleFile'
    if (!$BundleArchive -or $PackageDirectory -or $PackageVersion) {
        throw 'SingleFile requires -BundleArchive, without NuGet package arguments.'
    }
    $source = (Resolve-Path -LiteralPath $BundleArchive).Path
}

$suffix = if ($IsWindows) { '.exe' } else { '' }
if (!$DotNet) {
    $DotNet = Join-Path $PSScriptRoot "../.dotnet/dotnet$suffix"
}
$DotNet = (Resolve-Path -LiteralPath $DotNet).Path
$root = [IO.Path]::GetFullPath($OutputDirectory, $PWD.ProviderPath)
if (Test-Path -LiteralPath $root) {
    throw "Output directory '$root' already exists. Choose a new directory."
}

$tools = Join-Path $root 'tools'
$sos = Join-Path $root 'sos'
$extraction = Join-Path $root 'bundle-extract'
New-Item -ItemType Directory -Path $tools, $extraction | Out-Null

$environment = @{
    NUGET_PACKAGES = (Join-Path $root 'packages')
    DOTNET_CLI_HOME = (Join-Path $root 'dotnet-home')
    DOTNET_BUNDLE_EXTRACT_BASE_DIR = $extraction
    DOTNET_ROOT = (Split-Path $DotNet)
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_NOLOGO = 'true'
}
$architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToUpperInvariant()
$environment["DOTNET_ROOT_$architecture"] = Split-Path $DotNet
$previousEnvironment = @{}
try {
    foreach ($name in $environment.Keys) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $environment[$name])
    }

    if ($Distribution -eq 'NuGet') {
        $config = Join-Path $root 'tool-install.config'
        $escapedSource = [Security.SecurityElement]::Escape($source)
        "<configuration><packageSources><clear/><add key=`"local`" value=`"$escapedSource`"/></packageSources></configuration>" |
            Set-Content -LiteralPath $config -Encoding utf8

        foreach ($tool in @('dotnet-sos', 'dotnet-dump')) {
            & $DotNet tool install $tool --version $PackageVersion --tool-path $tools --configfile $config
            if ($LASTEXITCODE -ne 0) {
                throw "Installing $tool from '$source' failed (exit $LASTEXITCODE)."
            }
        }
    } else {
        Expand-Archive -LiteralPath $source -DestinationPath $tools
    }

    $sosTool = Join-Path $tools "dotnet-sos$suffix"
    $dumpTool = Join-Path $tools "dotnet-dump$suffix"
    foreach ($tool in @($sosTool, $dumpTool)) {
        if (!(Test-Path -LiteralPath $tool -PathType Leaf)) {
            throw "Required tool '$tool' was not found."
        }
        if (!$IsWindows) {
            & chmod +x $tool
            if ($LASTEXITCODE -ne 0) {
                throw "Setting executable permission on '$tool' failed (exit $LASTEXITCODE)."
            }
        }
    }

    & $sosTool install --install-directory $sos --no-lldbinit
    if ($LASTEXITCODE -ne 0) {
        throw "Installing packaged SOS failed (exit $LASTEXITCODE)."
    }

    $nativeSos = if ($IsWindows) { 'sos.dll' } elseif ($IsMacOS) { 'libsosplugin.dylib' } else { 'libsosplugin.so' }
    foreach ($file in @((Join-Path $sos $nativeSos), (Join-Path $sos 'net8.0/SOS.Extensions.dll'))) {
        if (!(Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "Installed SOS payload is missing '$file'."
        }
    }

    @{
        Distribution = $Distribution
        Source = $source
        PackageVersion = $PackageVersion
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'layout.json') -Encoding utf8
} finally {
    foreach ($name in $previousEnvironment.Keys) {
        if ($null -eq $previousEnvironment[$name]) {
            # PowerShell otherwise converts null to an empty string instead of removing the variable.
            [Environment]::SetEnvironmentVariable($name, [NullString]::Value)
        } else {
            [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name])
        }
    }
}

Write-Host "Prepared $Distribution SOS test layout at '$root'."
Write-Host "`$env:SOSHARNESS_TOOL_LAYOUT = '$Distribution'"
Write-Host "`$env:SOSHARNESS_TOOL_ROOT = '$($root.Replace("'", "''"))'"
