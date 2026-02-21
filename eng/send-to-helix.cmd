@echo off
REM Licensed to the .NET Foundation under one or more agreements.
REM The .NET Foundation licenses this file to you under the MIT license.

REM Send diagnostics tests to Helix for remote execution.
REM
REM Usage: send-to-helix.cmd [options]
REM
REM Options:
REM   -configuration <Debug|Release>   Build configuration (default: Debug)
REM   -architecture <x64|x86|arm64>    Target architecture (default: x64)
REM   -queue <queue-name>              Helix queue to target (default: Windows.10.Amd64.Open)
REM   -accesstoken <token>             Helix access token for internal queues (optional)
REM   -creator <name>                  Creator name for public builds (optional)
REM   -bl                              Generate binary log
REM   -help                            Show this help message

setlocal enabledelayedexpansion

set "EngRoot=%~dp0"
set "EngRoot=%EngRoot:~0,-1%"
for %%i in ("%EngRoot%\..") do set "RepoRoot=%%~fi"
set "Configuration=Debug"
set "Architecture=x64"
set "TargetOS=Windows_NT"
set "HelixQueue=windows.amd64.server2022"
set "HelixAccessToken=AAAE1Eq55IR3-s8xjJvkFcHtmI0"
set "Creator="
set "BinaryLog="

REM Parse arguments
:parse_args
if "%~1"=="" goto :done_parsing
if /i "%~1"=="-configuration" (
    set "Configuration=%~2"
    shift
    shift
    goto :parse_args
)
if /i "%~1"=="-c" (
    set "Configuration=%~2"
    shift
    shift
    goto :parse_args
)
if /i "%~1"=="-architecture" (
    set "Architecture=%~2"
    shift
    shift
    goto :parse_args
)
if /i "%~1"=="-a" (
    set "Architecture=%~2"
    shift
    shift
    goto :parse_args
)
if /i "%~1"=="-queue" (
    set "HelixQueue=%~2"
    shift
    shift
    goto :parse_args
)
if /i "%~1"=="-accesstoken" (
    set "HelixAccessToken=%~2"
    shift
    shift
    goto :parse_args
)
if /i "%~1"=="-creator" (
    set "Creator=%~2"
    shift
    shift
    goto :parse_args
)
if /i "%~1"=="-bl" (
    if not exist "%RepoRoot%\artifacts\helix-log" mkdir "%RepoRoot%\artifacts\helix-log"
    set "BinaryLog=/bl:%RepoRoot%\artifacts\helix-log\SendToHelix.binlog"
    shift
    goto :parse_args
)
if /i "%~1"=="-help" goto :show_help
if /i "%~1"=="-h" goto :show_help
if /i "%~1"=="/?" goto :show_help

echo Unknown argument: %~1
goto :show_help

:done_parsing

REM Set required environment variables for local Helix execution
if "%BUILD_SOURCEBRANCH%"=="" set "BUILD_SOURCEBRANCH=local"
if "%BUILD_REPOSITORY_NAME%"=="" set "BUILD_REPOSITORY_NAME=diagnostics"
if "%SYSTEM_TEAMPROJECT%"=="" set "SYSTEM_TEAMPROJECT=dnceng"
if "%BUILD_REASON%"=="" set "BUILD_REASON=Manual"

REM Build creator argument if specified
set "CreatorArg="
if not "%Creator%"=="" set "CreatorArg=/p:Creator=%Creator%"

REM Build access token argument if specified  
set "AccessTokenArg="
if not "%HelixAccessToken%"=="" set "AccessTokenArg=/p:HelixAccessToken=%HelixAccessToken%"

echo.
echo ===========================
echo Sending tests to Helix
echo ===========================
echo Configuration:    %Configuration%
echo Architecture:     %Architecture%
echo Target OS:        %TargetOS%
echo Helix Queue:      %HelixQueue%
echo Artifacts Dir:    %RepoRoot%\artifacts\
echo.

REM Verify artifacts exist
if not exist "%RepoRoot%\artifacts\bin" (
    echo ERROR: Build artifacts not found at %RepoRoot%\artifacts\bin
    echo Please run Build.cmd first to build the tests.
    exit /b 1
)

REM Send to Helix using PowerShell (use /tl:off to disable terminal logger for better output)
REM The PrepareCorrelationPayload target runs automatically before Test
REM Set environment variables in PowerShell scope for the Helix SDK to read
powershell -ExecutionPolicy ByPass -NoProfile -command "$env:BUILD_SOURCEBRANCH='%BUILD_SOURCEBRANCH%'; $env:BUILD_REPOSITORY_NAME='%BUILD_REPOSITORY_NAME%'; $env:SYSTEM_TEAMPROJECT='%SYSTEM_TEAMPROJECT%'; $env:BUILD_REASON='%BUILD_REASON%'; & '%RepoRoot%\eng\common\msbuild.ps1' '%RepoRoot%\eng\helix.proj' /restore /t:Test /tl:off /p:Configuration=%Configuration% /p:TargetArchitecture=%Architecture% /p:TargetOS=%TargetOS% /p:HelixTargetQueues=%HelixQueue% /p:TestArtifactsDir='%RepoRoot%\artifacts\' /p:EnableAzurePipelinesReporter=false %AccessTokenArg% %CreatorArg% %BinaryLog%"

if %ERRORLEVEL% neq 0 (
    echo.
    echo ERROR: Failed to send tests to Helix
    exit /b %ERRORLEVEL%
)

echo.
echo Tests submitted to Helix successfully!
echo View results at: https://helix.dot.net/
exit /b 0

:show_help
echo.
echo Send diagnostics tests to Helix for remote execution.
echo.
echo Usage: send-to-helix.cmd [options]
echo.
echo Options:
echo   -configuration ^<Debug^|Release^>   Build configuration (default: Debug)
echo   -architecture ^<x64^|x86^|arm64^>    Target architecture (default: x64)
echo   -queue ^<queue-name^>              Helix queue to target (default: Windows.10.Amd64.Open)
echo   -accesstoken ^<token^>             Helix access token for internal queues
echo   -creator ^<name^>                  Creator name for public builds
echo   -bl                              Generate binary log
echo   -help                            Show this help message
echo.
echo Examples:
echo   send-to-helix.cmd
echo   send-to-helix.cmd -configuration Release -queue Windows.11.Amd64.Open
echo   send-to-helix.cmd -architecture arm64 -queue Windows.11.Arm64.Open
echo   send-to-helix.cmd -creator "MyName" -bl
echo.
echo Notes:
echo   - Run Build.cmd first to build the test artifacts
echo   - For internal queues (not ending in .Open), provide -accesstoken
echo   - Public queues require -creator to be set
echo.
echo Available public Windows queues:
echo   Windows.10.Amd64.Open
echo   Windows.11.Amd64.Open  
echo   Windows.11.Arm64.Open
echo.
exit /b 0
