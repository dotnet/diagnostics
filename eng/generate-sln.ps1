#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Generates and migrates Visual Studio solution files for the diagnostics repository.

.DESCRIPTION
    This script generates solution files using SlnGen tool.

.EXAMPLE
    .\generate-sln.ps1
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Determine script location and repo root
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$dotnet = Join-Path $repoRoot "dotnet.cmd"

function Write-Success {
    param([string]$Message)
    Write-Host "[SUCCESS] $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[WARNING] $Message" -ForegroundColor Yellow
}

function Write-ErrorMessage {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

try {
    Write-Host "Diagnostics Solution Generator" -ForegroundColor Magenta
    Write-Host "==============================" -ForegroundColor Magenta

    # Change to repo root directory to ensure all operations work correctly
    $originalLocation = Get-Location
    Set-Location $repoRoot

    # Generate solution files with SlnGen
    Write-Host "Generating solution files with SlnGen..." -ForegroundColor Cyan
    # Use local dotnet to execute SlnGen tool
    & $dotnet tool exec Microsoft.VisualStudio.SlnGen.Tool --collapsefolders true --folders true --launch false
    $slngenExit = $LASTEXITCODE
    if ($slngenExit -ne 0) {
        throw "SlnGen tool exited with non-zero exit code $slngenExit"
    }

    Write-Host ""
    Write-Success "Solution generation completed successfully!"
    Write-Host "You can now open the generated build.sln file in Visual Studio." -ForegroundColor Gray
}
catch {
    Write-ErrorMessage "Failed to generate solution files with SlnGen"
    Write-Host "Error details: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    # Restore original location
    Set-Location $originalLocation
}

