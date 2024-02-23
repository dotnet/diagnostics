@echo off
setlocal enabledelayedexpansion

set "SDK_LOC=%~dp0.dotnet"
set "SLN_OR_PROJ=%~dp0diagnostics.sln"

set "DOTNET_ROOT=%SDK_LOC%"
set "DOTNET_ROOT(x86)=%SDK_LOC%\x86"
set DOTNET_MULTILEVEL_LOOKUP=0
set "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR=%DOTNET_ROOT%"

set PATH=%DOTNET_ROOT%;%PATH%

:: Restore before doing this

if not exist "%DOTNET_ROOT%\dotnet.exe" (
    echo [ERROR] SDK has not yet been installed.
    exit /b 1
)

set "DEVENV=%DevEnvDir%devenv.exe"

if exist "%DEVENV%" (
    :: Fully qualified works
    set "COMMAND=start "" /B "%ComSpec%" /S /C ""%DEVENV%" "%SLN_OR_PROJ%"""
) else (
    where devenv.exe /Q
    if !errorlevel! equ 0 (
        :: On the PATH, use that.
        set "COMMAND=start "" /B "%ComSpec%" /S /C "devenv.exe "%SLN_OR_PROJ%"""
    ) else (
        :: Can't find VS, let file associations take care of it
        set "COMMAND=start /B %SLN_OR_PROJ%"
    )
)

%COMMAND%
