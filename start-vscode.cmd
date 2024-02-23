@echo off
setlocal enabledelayedexpansion

set "SDK_LOC=%~dp0.dotnet"

set "DOTNET_ROOT=%SDK_LOC%"
set "DOTNET_ROOT(x86)=%SDK_LOC%\x86"
set "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR=%DOTNET_ROOT%"

set "PATH=%DOTNET_ROOT%;%PATH%"

:: Restore before doing this

SET folder=%~1

IF NOT EXIST "%DOTNET_ROOT%\dotnet.exe" (
    echo [ERROR] .NET Core has not yet been installed. Run `%~dp0dotnet.cmd` to install tools
    exit /b 1
)

IF "%folder%"=="" (
    code .
) else (
    code "%folder%"
)

exit /b 1
