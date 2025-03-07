@if not defined _echo @echo off
setlocal EnableDelayedExpansion EnableExtensions

:: Define a prefix for most output progress messages that come from this script. That makes
:: it easier to see where these are coming from. Note that there is a trailing space here.
set "__MsgPrefix=BUILD: "

echo %__MsgPrefix%Starting Build at %TIME%
set __ThisScriptFull="%~f0"
set __ThisScriptDir="%~dp0"

call "%__ThisScriptDir%"\native\init-vs-env.cmd
if NOT '%ERRORLEVEL%' == '0' goto ExitWithError

if defined VS170COMNTOOLS (
    set "__VSToolsRoot=%VS170COMNTOOLS%"
    set "__VCToolsRoot=%VS170COMNTOOLS%\..\..\VC\Auxiliary\Build"
    set __VSVersion=vs2022
)
if defined VS160COMNTOOLS (
    set "__VSToolsRoot=%VS160COMNTOOLS%"
    set "__VCToolsRoot=%VS160COMNTOOLS%\..\..\VC\Auxiliary\Build"
    set __VSVersion=vs2019
) else if defined VS150COMNTOOLS (
    set "__VSToolsRoot=%VS150COMNTOOLS%"
    set "__VCToolsRoot=%VS150COMNTOOLS%\..\..\VC\Auxiliary\Build"
    set __VSVersion=vs2017
)

:: Set the default arguments for build

set __TargetArch=x64
if /i "%PROCESSOR_ARCHITECTURE%" == "amd64" set __TargetArch=x64
if /i "%PROCESSOR_ARCHITECTURE%" == "arm64" set __TargetArch=arm64
if /i "%PROCESSOR_ARCHITECTURE%" == "x86" set __TargetArch=x86
set __HostArch=
set __BuildType=Debug
set __TargetOS=Windows_NT
set __BuildNative=1
set __CI=0
set __Verbosity=minimal
set __Ninja=0

:: Set the various build properties here so that CMake and MSBuild can pick them up
set "__ProjectDir=%~dp0"
:: remove trailing slash 
if %__ProjectDir:~-1%==\ set "__ProjectDir=%__ProjectDir:~0,-1%"
set "__ProjectDir=%__ProjectDir%\.."
set "__SourceDir=%__ProjectDir%\src"

:: __UnprocessedBuildArgs are args that we pass to msbuild (e.g. /p:OfficialBuildId=xxxxxx)
set "__args=%*"
set processedArgs=
set __UnprocessedBuildArgs=

:Arg_Loop
if "%1" == "" goto ArgsDone

if /i "%1" == "-?"    goto Usage
if /i "%1" == "-h"    goto Usage
if /i "%1" == "-help" goto Usage
if /i "%1" == "--help" goto Usage

if /i "%1" == "-configuration"       (set __BuildType=%2&set processedArgs=!processedArgs! %1 %2&shift&shift&goto Arg_Loop)
if /i "%1" == "-architecture"        (set __TargetArch=%2&set processedArgs=!processedArgs! %1 %2&shift&shift&goto Arg_Loop)
if /i "%1" == "-verbosity"           (set __Verbosity=%2&set processedArgs=!processedArgs! %1 %2&shift&shift&goto Arg_Loop)
if /i "%1" == "-ci"                  (set __CI=1&set processedArgs=!processedArgs! %1&shift&goto Arg_Loop)

:: These options are ignored for a native build
if /i "%1" == "-clean"               (set processedArgs=!processedArgs! %1&shift&goto Arg_Loop)
if /i "%1" == "-build"               (set processedArgs=!processedArgs! %1&shift&goto Arg_Loop)
if /i "%1" == "-rebuild"             (set processedArgs=!processedArgs! %1&shift&goto Arg_Loop)
if /i "%1" == "-test"                (set processedArgs=!processedArgs! %1&shift&goto Arg_Loop)
if /i "%1" == "-sign"                (set processedArgs=!processedArgs! %1&shift&goto Arg_Loop)
if /i "%1" == "-restore"             (set processedArgs=!processedArgs! %1&shift&goto Arg_Loop)
if /i "%1" == "-pack"                (set processedArgs=!processedArgs! %1&shift&goto Arg_Loop)
if /i "%1" == "-publish"             (set processedArgs=!processedArgs! %1&shift&goto Arg_Loop)
if /i "%1" == "-preparemachine"      (set processedArgs=!processedArgs! %1&shift&goto Arg_Loop)
if /i "%1" == "-projects"            (set processedArgs=!processedArgs! %1 %2&shift&shift&goto Arg_Loop)

if [!processedArgs!] == [] (
  set __UnprocessedBuildArgs=%__args%
) else (
  set __UnprocessedBuildArgs=%__args%
  for %%t in (!processedArgs!) do (
    set __UnprocessedBuildArgs=!__UnprocessedBuildArgs:*%%t=!
  )
)

:ArgsDone

if "%__HostArch%" == "" set __HostArch=%__TargetArch%
if /i "%__BuildType%" == "debug" set __BuildType=Debug
if /i "%__BuildType%" == "release" set __BuildType=Release

if "%NUGET_PACKAGES%" == "" (
    if %__CI% EQU 1 (
        set "NUGET_PACKAGES=%__ProjectDir%\.packages"
    ) else (
        set "NUGET_PACKAGES=%UserProfile%\.nuget\packages"
    )
)

echo %NUGET_PACKAGES%

:: Set the remaining variables based upon the determined build configuration
set "__RootBinDir=%__ProjectDir%\artifacts"
set "__BinDir=%__RootBinDir%\bin\%__TargetOS%.%__TargetArch%.%__BuildType%"
set "__LogDir=%__RootBinDir%\log\%__TargetOS%.%__TargetArch%.%__BuildType%"
set "__ArtifactsIntermediatesDir=%__RootBinDir%\obj"
set "__IntermediatesDir=%__ArtifactsIntermediatesDir%\%__TargetOS%.%__TargetArch%.%__BuildType%"
set "__PackagesBinDir=%__RootBinDir%\packages\%__BuildType%\Shipping"

:: Generate path to be set for CMAKE_INSTALL_PREFIX to contain forward slash
set "__CMakeBinDir=%__BinDir%"
set "__CMakeBinDir=%__CMakeBinDir:\=/%"

:: Common msbuild arguments
set "__CommonBuildArgs=/v:!__Verbosity! /p:Configuration=%__BuildType% /p:BuildArch=%__TargetArch% %__UnprocessedBuildArgs%"

if not exist "%__BinDir%"           md "%__BinDir%"
if not exist "%__IntermediatesDir%" md "%__IntermediatesDir%"
if not exist "%__LogDir%"           md "%__LogDir%"

echo %__MsgPrefix%Commencing diagnostics repo build

:: Set the remaining variables based upon the determined build configuration

echo %__MsgPrefix%Checking prerequisites
:: Eval the output from probe-win1.ps1
for /f "delims=" %%a in ('powershell -NoProfile -ExecutionPolicy ByPass "& ""%__ProjectDir%\eng\native\set-cmake-path.ps1"""') do %%a

REM =========================================================================================
REM ===
REM === Start the build steps
REM ===
REM =========================================================================================

@if defined _echo @echo on

:: Parse the optdata package versions out of msbuild so that we can pass them on to CMake
set __DotNetCli=%__ProjectDir%\dotnet.cmd

REM =========================================================================================
REM ===
REM === Build the native code
REM ===
REM =========================================================================================

if %__BuildNative% EQU 1 (
    rem Scope environment changes start {
    setlocal

    echo %__MsgPrefix%Commencing build of native components for %__TargetOS%.%__TargetArch%.%__BuildType%

    REM Set the environment for the native build
    if /i "%PROCESSOR_ARCHITECTURE%" == "ARM64" (
        set __VCBuildArch=arm64
        if /i "%__HostArch%" == "x64" ( set __VCBuildArch=arm64_amd64 )
        if /i "%__HostArch%" == "x86" ( set __VCBuildArch=arm64_x86 )
    ) else (
        set __VCBuildArch=amd64
        if /i "%__HostArch%" == "x86" ( set __VCBuildArch=amd64_x86 )
        if /i "%__HostArch%" == "arm64" ( set __VCBuildArch=amd64_arm64 )
    )

    echo %__MsgPrefix%Using environment: "%__VCToolsRoot%\vcvarsall.bat" !__VCBuildArch!
    call                                 "%__VCToolsRoot%\vcvarsall.bat" !__VCBuildArch!
    @if defined _echo @echo on

    if not defined VSINSTALLDIR (
        echo %__MsgPrefix%Error: VSINSTALLDIR variable not defined.
        goto ExitWithError
    )

    if %__Ninja% EQU 1 (
        set __ExtraCmakeArgs="-DCMAKE_BUILD_TYPE=!__BuildType!"
    )

    echo Generating Version Header
    set __GenerateVersionLog="%__LogDir%\GenerateVersion.binlog"
    powershell -NoProfile -ExecutionPolicy ByPass -NoLogo -File "%__ProjectDir%\eng\common\msbuild.ps1" "%__ProjectDir%\eng\native-prereqs.proj" /bl:!__GenerateVersionLog! /t:BuildPrereqs /restore %__CommonBuildArgs%
    if not !errorlevel! == 0 (
        echo Generate Version Header FAILED
        goto ExitWithError
    )
    if defined __SkipConfigure goto SkipConfigure

    echo %__MsgPrefix%Regenerating the Visual Studio solution

    set "__ManagedBinaryDir=%__RootBinDir%\bin"
    set "__ManagedBinaryDir=!__ManagedBinaryDir:\=/!"
    set __ExtraCmakeArgs=!__ExtraCmakeArgs! "-DCMAKE_SYSTEM_VERSION=10.0" "-DCLR_MANAGED_BINARY_DIR=!__ManagedBinaryDir!" "-DCLR_BUILD_TYPE=%__BuildType%" "-DCLR_CMAKE_TARGET_ARCH=%__TargetArch%" "-DNUGET_PACKAGES=%NUGET_PACKAGES:\=/%"

    pushd "%__IntermediatesDir%"
    call "%__ProjectDir%\eng\native\gen-buildsys.cmd" "%__ProjectDir%" "%__IntermediatesDir%" %__VSVersion% %__HostArch% %__TargetOS% !__ExtraCmakeArgs!
    @if defined _echo @echo on
    popd

:SkipConfigure
    if defined __ConfigureOnly goto SkipNativeBuild

    if not exist "%__IntermediatesDir%\CMakeCache.txt" (
        echo %__MsgPrefix%Error: failed to generate native component build project!
        goto ExitWithError
    )
    set __BuildLog="%__LogDir%\Native.Build.binlog"

    echo running "%CMakePath%" --build %__IntermediatesDir% --target install --config %__BuildType% -- /bl:!__BuildLog! !__CommonBuildArgs!
    "%CMakePath%" --build %__IntermediatesDir% --target install --config %__BuildType% -- /bl:!__BuildLog! !__CommonBuildArgs!

    if not !ERRORLEVEL! == 0 (
        echo %__MsgPrefix%Error: native component build failed. Refer to the build log files for details:
        echo     !__BuildLog!
        goto ExitWithError
    )

:SkipNativeBuild
    rem } Scope environment changes end
    endlocal
)

REM Copy the native SOS binaries to where these tools expect for CI & VS testing

set "__targetRid=net8.0"
set "__dotnet_sos=%__RootBinDir%\bin\dotnet-sos\%__BuildType%\%__targetRid%"
set "__dotnet_dump=%__RootBinDir%\bin\dotnet-dump\%__BuildType%\%__targetRid%"
mkdir %__dotnet_sos%\win-%__TargetArch%
mkdir %__dotnet_sos%\publish\win-%__TargetArch%
mkdir %__dotnet_dump%\win-%__TargetArch%
mkdir %__dotnet_dump%\publish\win-%__TargetArch%
xcopy /y /q /i %__BinDir% %__dotnet_sos%\win-%__TargetArch%
xcopy /y /q /i %__BinDir% %__dotnet_sos%\publish\win-%__TargetArch%
xcopy /y /q /i %__BinDir% %__dotnet_dump%\win-%__TargetArch%
xcopy /y /q /i %__BinDir% %__dotnet_dump%\publish\win-%__TargetArch%

REM =========================================================================================
REM ===
REM === All builds complete!
REM ===
REM =========================================================================================

echo %__MsgPrefix%Repo successfully built. Finished at %TIME%
echo %__MsgPrefix%Product binaries are available at !__BinDir!

exit /b 0

REM =========================================================================================
REM === These two routines are intended for the exit code to propagate to the parent process
REM === Like MSBuild or Powershell. If we directly goto ExitWithError from within a if statement in
REM === any of the routines, the exit code is not propagated due to quirks of nested conditonals
REM === in delayed expansion scripts.
REM =========================================================================================
:ExitWithError
exit /b 1

:ExitWithCode
exit /b !__exitCode!

REM =========================================================================================
REM ===
REM === Helper routines
REM ===
REM =========================================================================================

:Usage
echo.
echo Build the Diagnostics repo.
echo.
echo Usage:
echo     build-native.cmd [option1] [option2]
echo.
echo All arguments are optional. The options are:
echo.
echo.-? -h -help --help: view this message.
echo -architecture ^<x64^|x86^|arm^|arm64^>.
echo -configuration ^<debug^|release^>
echo -verbosity ^<q[uiet]^|m[inimal]^|n[ormal]^|d[etailed]^|diag[nostic]^>
goto ExitWithError
