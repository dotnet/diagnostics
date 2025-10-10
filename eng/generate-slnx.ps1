#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Generates and migrates Visual Studio solution files for the diagnostics repository.

.DESCRIPTION
    This script generates solution files using SlnGen tool and migrates the legacy
    build.sln file to the new .slnx format, then cleans up the old file.

.EXAMPLE
    .\generate-slnx.ps1
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Determine local dotnet path (must exist)
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$localDotNet = Join-Path $scriptRoot "../.dotnet"
$dotnetExe = Join-Path $localDotNet "dotnet.exe"
if (-not (Test-Path $dotnetExe)) {
    Write-Host "[ERROR] Local dotnet not found at $dotnetExe" -ForegroundColor Red
    Write-Host "Ensure .dotnet is bootstrapped (e.g. run build/restore scripts) before running this generator." -ForegroundColor Yellow
    exit 1
}

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

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

    # Step 1: Generate solution files with SlnGen
    Write-Step "Generating solution files with SlnGen..."
    try {
        # Use local dotnet to execute SlnGen tool
        & $dotnetExe tool exec Microsoft.VisualStudio.SlnGen.Tool --collapsefolders true --folders true --launch false
        $slngenExit = $LASTEXITCODE
        if ($slngenExit -ne 0) {
            throw "SlnGen tool exited with non-zero exit code $slngenExit"
        }
        Write-Success "Solution files generated successfully"
    }
    catch {
        Write-ErrorMessage "Failed to generate solution files with SlnGen"
        Write-Host "Error details: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }

    # Step 2: Check if build.sln exists before migration
    if (Test-Path "build.sln") {
        Write-Step "Migrating build.sln to new format..."
        try {
            & $dotnetExe sln build.sln migrate
            $migrateExit = $LASTEXITCODE
            if ($migrateExit -ne 0) { throw "dotnet sln migrate exited with $migrateExit" }
            Write-Success "Migration completed successfully"
        }
        catch {
            Write-ErrorMessage "Failed to migrate build.sln"
            Write-Host "Error details: $($_.Exception.Message)" -ForegroundColor Red
            Write-Warning "Continuing with cleanup..."
        }

        # Step 3: Clean up old solution file
        Write-Step "Cleaning up old build.sln file..."
        try {
            Remove-Item "build.sln" -Force
            Write-Success "Old solution file removed"
        }
        catch {
            Write-Warning "Could not remove build.sln: $($_.Exception.Message)"
            Write-Host "You may need to remove it manually" -ForegroundColor Yellow
        }
    }
    else {
        Write-Warning "build.sln not found, skipping migration step"
    }

    Write-Host ""
    Write-Success "Solution generation and migration completed successfully!"
    Write-Host "You can now open the generated build.slnx files in Visual Studio." -ForegroundColor Gray
}
catch {
    Write-ErrorMessage "Script execution failed with unexpected error"
    Write-Host "Error details: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Stack trace:" -ForegroundColor Gray
    Write-Host $_.ScriptStackTrace -ForegroundColor Gray
    exit 1
}
