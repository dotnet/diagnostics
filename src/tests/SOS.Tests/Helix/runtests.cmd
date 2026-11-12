@echo off
setlocal EnableExtensions EnableDelayedExpansion

if "%HELIX_WORKITEM_UPLOAD_ROOT%"=="" (
  echo HELIX_WORKITEM_UPLOAD_ROOT is required.
  exit /b 2
)

if "%HELIX_WORKITEM_ROOT%"=="" (
  echo HELIX_WORKITEM_ROOT is required.
  exit /b 2
)

set "ROOT=%~dp0"
set "UPLOAD=%HELIX_WORKITEM_UPLOAD_ROOT%"
set "HELIX_WORK_ITEM="
set "RUNTIME_OVERRIDE="
set "RUNTIME_VERSION="

:parse_args
if "%~1"=="" goto args_parsed
if /I "%~1"=="--helix-work-item" goto parse_helix_work_item
if /I "%~1"=="--runtime-override" goto parse_runtime_override
if /I "%~1"=="--runtime-version" goto parse_runtime_version
echo Unknown argument "%~1".
exit /b 3

:parse_helix_work_item
if "%~2"=="" goto missing_arg_value
set "HELIX_WORK_ITEM=%~2"
shift
shift
goto parse_args

:parse_runtime_override
if "%~2"=="" goto missing_arg_value
set "RUNTIME_OVERRIDE=%~2"
shift
shift
goto parse_args

:parse_runtime_version
if "%~2"=="" goto missing_arg_value
set "RUNTIME_VERSION=%~2"
shift
shift
goto parse_args

:missing_arg_value
echo Argument "%~1" requires a value.
exit /b 3

:args_parsed
if defined RUNTIME_OVERRIDE if not defined RUNTIME_VERSION (
  echo --runtime-version is required with --runtime-override.
  exit /b 3
)
set /p RID=<"%ROOT%\.sos-test-payload"
for /f "usebackq skip=1 delims=" %%M in ("%ROOT%\.sos-test-payload") do if not defined CONFIGURATION set "CONFIGURATION=%%M"
if "%RID%"=="" (
  echo The payload marker does not contain a RID.
  exit /b 3
)
if "%CONFIGURATION%"=="" (
  echo The payload marker does not contain a configuration.
  exit /b 3
)

set "IDENTITY=all"
if defined HELIX_WORK_ITEM (
  set "IDENTITY="
  set "SHARD_TOKENS=!HELIX_WORK_ITEM:-= !"
  for %%S in (!SHARD_TOKENS!) do set "IDENTITY=%%S"
  if /I "!IDENTITY!"=="Framework" (
    set "SOSHARNESS_ONLY_FLAVORS=Framework"
  ) else if /I "!IDENTITY:~0,3!"=="Net" (
    set "SOSHARNESS_ONLY_COREVERSIONS=!IDENTITY!"
    if defined RUNTIME_OVERRIDE (
      set "SOSHARNESS_ONLY_FLAVORS=Core"
    ) else (
      set "SOSHARNESS_ONLY_FLAVORS=Core,SingleFile"
    )
  ) else (
    echo The Helix work item "!HELIX_WORK_ITEM!" does not identify a runtime or Framework shard.
    exit /b 3
  )
  echo Running SOS shard !IDENTITY!.
)

set "TARGET_ARCH=%RID:win-=%"
set "PAYLOAD_DOTNET_ROOT=%HELIX_CORRELATION_PAYLOAD%\dotnet-cli"
set "DEBUGGER_HOST=%ROOT%\debugger\cdb.exe"
set "DOTNET_MULTILEVEL_LOOKUP=0"

if not exist "%UPLOAD%" mkdir "%UPLOAD%"

if "%HELIX_CORRELATION_PAYLOAD%"=="" (
  echo HELIX_CORRELATION_PAYLOAD is required.
  exit /b 3
)
if not exist "%PAYLOAD_DOTNET_ROOT%\dotnet.exe" (
  echo The Helix-provisioned dotnet host was not found at "%PAYLOAD_DOTNET_ROOT%\dotnet.exe".
  exit /b 3
)
if defined RUNTIME_OVERRIDE (
  set "RUNTIME_OVERRIDE_ROOT=%ROOT%\%RUNTIME_OVERRIDE%"
  set "TARGET_RUNTIME_ROOT=%PAYLOAD_DOTNET_ROOT%\shared\Microsoft.NETCore.App\%RUNTIME_VERSION%"
  if not exist "!RUNTIME_OVERRIDE_ROOT!\System.Private.CoreLib.dll" (
    echo The private runtime override was not found at "!RUNTIME_OVERRIDE_ROOT!".
    exit /b 3
  )
  if not exist "!TARGET_RUNTIME_ROOT!\System.Private.CoreLib.dll" (
    echo The Helix-provisioned runtime was not found at "!TARGET_RUNTIME_ROOT!".
    exit /b 3
  )
  echo Overlaying private runtime from "!RUNTIME_OVERRIDE_ROOT!" onto "!TARGET_RUNTIME_ROOT!".
  powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass ^
    -Command "Copy-Item -Path '!RUNTIME_OVERRIDE_ROOT!\*' -Destination '!TARGET_RUNTIME_ROOT!' -Recurse -Force"
  if errorlevel 1 exit /b 3
)

set "TEST_DLL="
for /d %%D in ("%ROOT%\artifacts\bin\SOS.Tests\%CONFIGURATION%\*") do (
  if exist "%%~fD\SOS.Tests.dll" (
    if defined TEST_DLL (
      echo Multiple staged SOS.Tests.dll files were found for %CONFIGURATION%.
      exit /b 3
    )
    set "TEST_DLL=%%~fD\SOS.Tests.dll"
  )
)

if not defined TEST_DLL (
  echo SOS.Tests.dll was not found for %CONFIGURATION%.
  exit /b 3
)

if not exist "%DEBUGGER_HOST%" (
  echo cdb.exe was not found at "%DEBUGGER_HOST%".
  exit /b 3
)

set "DOTNET_ROOT=%PAYLOAD_DOTNET_ROOT%"
set "DOTNET_ROOT_X86=%DOTNET_ROOT%"

set "LOG=%UPLOAD%\SOS.Tests-%RID%-%CONFIGURATION%-%IDENTITY%.log"

reg.exe add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\MiniDumpSettings" ^
  /v DisableAuxProviderSignatureCheck /t REG_DWORD /d 1 /f
if errorlevel 1 exit /b 3
if /I "%TARGET_ARCH%"=="x86" (
  reg.exe add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\MiniDumpSettings" ^
    /v DisableAuxProviderSignatureCheck /t REG_DWORD /d 1 /f /reg:32
  if errorlevel 1 exit /b 3
)

"%DOTNET_ROOT%\dotnet.exe" "%TEST_DLL%" ^
  --results-directory "%UPLOAD%" ^
  --report-xunit ^
  --report-xunit-filename "SOS.Tests-%RID%-%CONFIGURATION%-%IDENTITY%.testResults.xml" ^
  --report-xunit-html ^
  --report-xunit-html-filename "SOS.Tests-%RID%-%CONFIGURATION%-%IDENTITY%.html" ^
  --auto-reporters off > "%LOG%" 2>&1
set "EXIT_CODE=%ERRORLEVEL%"

type "%LOG%"

if not "%EXIT_CODE%"=="0" (
  set "DUMP_ROOT=%ROOT%\artifacts\tmp\sos-harness\%CONFIGURATION%"
  if exist "!DUMP_ROOT!\dumps\." (
    set "DUMP_ARCHIVE=%UPLOAD%\SOS.Tests-dumps-%RID%-%CONFIGURATION%-%IDENTITY%.tar.gz"
    echo Archiving SOS dumps to "!DUMP_ARCHIVE!".
    tar.exe -czf "!DUMP_ARCHIVE!" -C "!DUMP_ROOT!" dumps
    if errorlevel 1 echo Failed to archive SOS dumps. 1>&2
  )
)

exit /b %EXIT_CODE%
