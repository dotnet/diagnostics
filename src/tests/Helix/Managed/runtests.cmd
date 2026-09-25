@echo off
setlocal EnableExtensions DisableDelayedExpansion

if "%HELIX_WORKITEM_UPLOAD_ROOT%"=="" (
  echo HELIX_WORKITEM_UPLOAD_ROOT is required.
  exit /b 2
)

set "ROOT=%~dp0"
set "WORK_ITEM="
set "TEST_DLL="
set "REPORT_NAME="
set "TEST_DOTNET_ROOT="
set "RUNTIME_VERSION="
set "TEST_ARGUMENTS="

:parse_arguments
if "%~1"=="" goto arguments_parsed
if "%~1"=="--" (
  shift
  goto test_arguments
)
set "ARGUMENT_VARIABLE="
if /I "%~1"=="--helix-work-item" set "ARGUMENT_VARIABLE=WORK_ITEM"
if /I "%~1"=="--test-dll" set "ARGUMENT_VARIABLE=TEST_DLL"
if /I "%~1"=="--report-name" set "ARGUMENT_VARIABLE=REPORT_NAME"
if /I "%~1"=="--dotnet-root" set "ARGUMENT_VARIABLE=TEST_DOTNET_ROOT"
if /I "%~1"=="--runtime-version" set "ARGUMENT_VARIABLE=RUNTIME_VERSION"
if not defined ARGUMENT_VARIABLE (
  echo Unknown launcher argument "%~1". Pass test arguments after --. 1>&2
  exit /b 2
)
if "%~2"=="" (
  echo Missing value for %~1. 1>&2
  exit /b 2
)
set "%ARGUMENT_VARIABLE%=%~2"
shift
shift
goto parse_arguments

:test_arguments
if "%~1"=="" goto arguments_parsed
set "TEST_ARGUMENTS=%TEST_ARGUMENTS% %1"
shift
goto test_arguments

:arguments_parsed
if not defined TEST_DOTNET_ROOT (
  if "%HELIX_CORRELATION_PAYLOAD%"=="" (
    echo HELIX_CORRELATION_PAYLOAD is required. 1>&2
    exit /b 2
  )
  set "TEST_DOTNET_ROOT=%HELIX_CORRELATION_PAYLOAD%\dotnet-cli"
)
if not defined TEST_DLL if defined WORK_ITEM set "TEST_DLL=%ROOT%tests\%WORK_ITEM%\%WORK_ITEM%.dll"
if not defined REPORT_NAME set "REPORT_NAME=%WORK_ITEM%"
if not defined TEST_DLL (
  echo Specify --helix-work-item or both --test-dll and --report-name. 1>&2
  exit /b 2
)
if not defined REPORT_NAME (
  echo Specify --helix-work-item or both --test-dll and --report-name. 1>&2
  exit /b 2
)
set "DOTNET_ROOT=%TEST_DOTNET_ROOT%"
set "LOG=%HELIX_WORKITEM_UPLOAD_ROOT%\%REPORT_NAME%.log"

if not exist "%DOTNET_ROOT%\dotnet.exe" (
  echo The Helix-provisioned dotnet host was not found at "%DOTNET_ROOT%\dotnet.exe".
  exit /b 3
)

if not exist "%TEST_DLL%" (
  echo The test assembly was not found at "%TEST_DLL%".
  exit /b 3
)

if not exist "%HELIX_WORKITEM_UPLOAD_ROOT%" mkdir "%HELIX_WORKITEM_UPLOAD_ROOT%"
if not exist "%HELIX_WORKITEM_UPLOAD_ROOT%" exit /b 3

set "DOTNET_ARGUMENTS="
if defined RUNTIME_VERSION set DOTNET_ARGUMENTS=--fx-version "%RUNTIME_VERSION%"
"%DOTNET_ROOT%\dotnet.exe" %DOTNET_ARGUMENTS% "%TEST_DLL%" ^
  --results-directory "%HELIX_WORKITEM_UPLOAD_ROOT%" ^
  --report-xunit ^
  --report-xunit-filename "%REPORT_NAME%.testResults.xml" ^
  --auto-reporters off %TEST_ARGUMENTS% > "%LOG%" 2>&1
set "TEST_EXIT_CODE=%ERRORLEVEL%"

type "%LOG%"
exit /b %TEST_EXIT_CODE%
