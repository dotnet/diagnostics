# Licensed to the .NET Foundation under one or more agreements.
# The .NET Foundation licenses this file to you under the MIT license.

#requires -Version 7.0

<#
.SYNOPSIS
Prepares an isolated Packaged tool layout from an exact NuGet tool version.
.EXAMPLE
.\eng\PrepareSOSTestLayout.ps1 -PackageVersion 10.0.755902 -OutputDirectory .\artifacts\tmp\sos-packaged
.NOTES
Uses the installer shipped in the downloaded package, without changing user SOS/LLDB configuration.
The output directory must not already exist.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [string] $PackageSource = 'https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-tools/nuget/v3/index.json',

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $PackageVersion,

    [string] $DotNet
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$sourceUri = $null
if ([Uri]::TryCreate($PackageSource, [UriKind]::Absolute, [ref] $sourceUri) -and $sourceUri.Scheme -eq 'https') {
    $source = $sourceUri.AbsoluteUri
} else {
    $source = (Resolve-Path -LiteralPath $PackageSource).Path
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
New-Item -ItemType Directory -Path $tools | Out-Null

$environment = @{
    NUGET_PACKAGES = (Join-Path $root 'packages')
    DOTNET_CLI_HOME = (Join-Path $root 'dotnet-home')
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

    $config = Join-Path $root 'tool-install.config'
    $escapedSource = [Security.SecurityElement]::Escape($source)
    "<configuration><packageSources><clear/><add key=`"candidate`" value=`"$escapedSource`"/></packageSources></configuration>" |
        Set-Content -LiteralPath $config -Encoding utf8

    foreach ($tool in @('dotnet-sos', 'dotnet-dump')) {
        & $DotNet tool install $tool --version $PackageVersion --tool-path $tools --configfile $config
        if ($LASTEXITCODE -ne 0) {
            throw "Installing $tool from '$source' failed (exit $LASTEXITCODE)."
        }
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

    & $sosTool install --help
    if ($LASTEXITCODE -ne 0) {
        throw "Running the installed dotnet-sos failed (exit $LASTEXITCODE)."
    }

    # Published versions predate the isolated-install CLI options. Use their own installer, not repo binaries.
    $helpers = @(Get-ChildItem -LiteralPath (Join-Path $tools '.store\dotnet-sos') -Filter SOS.InstallHelper.dll -Recurse -File)
    if ($helpers.Count -ne 1) {
        throw "Expected one shipped SOS.InstallHelper.dll, found $($helpers.Count)."
    }
    $context = [Runtime.Loader.AssemblyLoadContext]::new('SOS package installer', $true)
    try {
        $installerAssembly = $context.LoadFromAssemblyPath($helpers[0].FullName)
        $installerType = $installerAssembly.GetType('SOS.InstallHelper', $true)
        $writeLine = [Action[string]] { param($message) Write-Host $message }
        $installer = [Activator]::CreateInstance($installerType, @($writeLine, $null))
        $packageRoot = $helpers[0].DirectoryName
        $installer.SOSNativeSourcePath = Join-Path $packageRoot (Split-Path $installer.SOSNativeSourcePath -Leaf)
        $installer.SOSManagedSourcePath = Join-Path $packageRoot 'lib'
        $installer.InstallLocation = $sos
        $installer.LLDBInitFile = [NullString]::Value
        $installer.Install()
    } finally {
        $context.Unload()
    }

    $nativeSos = if ($IsWindows) { 'sos.dll' } elseif ($IsMacOS) { 'libsosplugin.dylib' } else { 'libsosplugin.so' }
    foreach ($file in @((Join-Path $sos $nativeSos), (Join-Path $sos 'net8.0/SOS.Extensions.dll'))) {
        if (!(Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "Installed SOS payload is missing '$file'."
        }
    }

    @{
        Layout = 'Packaged'
        Source = $source
        PackageVersion = $PackageVersion
        Installer = $helpers[0].FullName
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

Write-Host "Prepared Packaged SOS test layout at '$root'."
Write-Host "`$env:SOSHARNESS_TOOL_LAYOUT = 'Packaged'"
Write-Host "`$env:SOSHARNESS_TOOL_ROOT = '$($root.Replace("'", "''"))'"
