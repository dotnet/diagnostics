@if not defined _echo @echo off
setlocal EnableDelayedExpansion EnableExtensions

set "__RepoRootDir=%~dp0..\.."
set "__VisualStudioVersion=%~1"
set "__HostArch=%~2"
set "__TargetOS=%~3"
set "__TargetArch=%~4"
set "__BuildType=%~5"
set "__ArtifactsIntermediatesDir=%~6"
set "__BinDir=%~7"
set "__SkipConfigure=0"
set "__ConfigureOnly=0"

shift
shift
shift
shift
shift
shift
shift

:Arg_Loop
if "%~1" == "" goto ArgsDone
if /i "%~1" == "-skipconfigure" (
    set "__SkipConfigure=1"
    shift
    goto Arg_Loop
)
if /i "%~1" == "-configureonly" (
    set "__ConfigureOnly=1"
    shift
    goto Arg_Loop
)

echo Error: unrecognized universal DBI build argument "%~1".
exit /b 1

:ArgsDone
if "%__VisualStudioVersion%" == "" goto Usage
if "%__HostArch%" == "" goto Usage
if "%__TargetOS%" == "" goto Usage
if "%__TargetArch%" == "" goto Usage
if "%__BuildType%" == "" goto Usage
if "%__ArtifactsIntermediatesDir%" == "" goto Usage
if "%__BinDir%" == "" goto Usage

set "__DbiIntermediatesDir=%__ArtifactsIntermediatesDir%\dbi\%__TargetOS%.%__TargetArch%.%__BuildType%"
if not exist "%__DbiIntermediatesDir%" md "%__DbiIntermediatesDir%"
if not exist "%__BinDir%" md "%__BinDir%"

if "%__SkipConfigure%" == "0" (
    echo Configuring source-built universal DBI
    call "%__RepoRootDir%\eng\native\gen-buildsys.cmd" "%__RepoRootDir%\src\dbi" "%__DbiIntermediatesDir%" %__VisualStudioVersion% %__HostArch% %__TargetOS% "-DCLR_BUILD_TYPE=%__BuildType%" "-DCLR_CMAKE_TARGET_ARCH=%__TargetArch%" "-DRUNTIME_GENERATED_INCLUDE_DIR=%__ArtifactsIntermediatesDir%"
    @if defined _echo @echo on
    if not !errorlevel! == 0 (
        echo Error: failed to configure source-built universal DBI.
        exit /b 1
    )
)

if "%__ConfigureOnly%" == "1" exit /b 0

echo Building source-built universal DBI
"%CMakePath%" --build "%__DbiIntermediatesDir%" --target mscordbi_universal --config %__BuildType%
if not !errorlevel! == 0 (
    echo Error: source-built universal DBI build failed.
    exit /b 1
)

set "__DbiOutputDir=%__DbiIntermediatesDir%\dlls\mscordbi_universal"
if not exist "%__DbiOutputDir%\mscordbi_universal.dll" (
    echo Error: source-built universal DBI was not produced in "%__DbiOutputDir%".
    exit /b 1
)

copy /y "%__DbiOutputDir%\mscordbi_universal.dll" "%__BinDir%\mscordbi_universal.dll" >nul
if exist "%__DbiOutputDir%\mscordbi_universal.pdb" copy /y "%__DbiOutputDir%\mscordbi_universal.pdb" "%__BinDir%\mscordbi_universal.pdb" >nul
exit /b 0

:Usage
echo Usage: build.cmd ^<VisualStudioVersion^> ^<hostArch^> ^<targetOS^> ^<targetArch^> ^<configuration^> ^<artifactsObjDir^> ^<outputDir^> [-skipconfigure] [-configureonly]
exit /b 1
