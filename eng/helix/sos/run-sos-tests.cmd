@echo off
setlocal EnableExtensions

if "%~6"=="" (
  echo usage: %~nx0 ^<configuration^> ^<rid^> ^<shard-index^> ^<shard-count^> ^<Dump^|Live^> ^<test-tfm^>
  exit /b 2
)

if "%HELIX_CORRELATION_PAYLOAD%"=="" (
  echo HELIX_CORRELATION_PAYLOAD is required.
  exit /b 2
)

if "%HELIX_WORKITEM_UPLOAD_ROOT%"=="" (
  echo HELIX_WORKITEM_UPLOAD_ROOT is required.
  exit /b 2
)

if "%HELIX_WORKITEM_ROOT%"=="" (
  echo HELIX_WORKITEM_ROOT is required.
  exit /b 2
)

set "CONFIGURATION=%~1"
set "RID=%~2"
set "SHARD_INDEX=%~3"
set "SHARD_COUNT=%~4"
set "LIVENESS=%~5"
set "TEST_TFM=%~6"
set "ROOT=%HELIX_CORRELATION_PAYLOAD%"
set "UPLOAD=%HELIX_WORKITEM_UPLOAD_ROOT%"
set "TARGET_ARCH=%RID:win-=%"
set "DOTNET_ROOT=%ROOT%\artifacts\dotnet-test"
set "DOTNET_ROOT_X86=%DOTNET_ROOT%"
set "DOTNET_MULTILEVEL_LOOKUP=0"
set "NUGET_PACKAGES=%ROOT%\.packages"
set "SOSHARNESS_REPO_ROOT=%ROOT%"
set "SOSHARNESS_DOTNET_ROOT=%DOTNET_ROOT%"
set "SOSHARNESS_ARTIFACTS_CONFIG=%CONFIGURATION%"
set "SOSHARNESS_DBGENG_ROOT=%ROOT%\artifacts\cdb-sos"
set "SOSHARNESS_USE_PREBUILT_TARGETS=1"
set "SOSHARNESS_SHARD_INDEX=%SHARD_INDEX%"
set "SOSHARNESS_SHARD_COUNT=%SHARD_COUNT%"
set "SOSHARNESS_ONLY_LIVENESS=%LIVENESS%"

if not exist "%UPLOAD%" mkdir "%UPLOAD%"

set "TEST_DLL=%ROOT%\artifacts\bin\SOS.Tests\%CONFIGURATION%\%TEST_TFM%\SOS.Tests.dll"
set "SIGNATURE_SCRIPT=%ROOT%\eng\DisableSignatureCheck.ps1"
set "SIGNATURE_STATE=%HELIX_WORKITEM_ROOT%\sos-signature-state"

if not exist "%TEST_DLL%" (
  echo SOS.Tests.dll was not found at "%TEST_DLL%".
  exit /b 3
)

if not exist "%SIGNATURE_SCRIPT%" (
  echo DisableSignatureCheck.ps1 was not found at "%SIGNATURE_SCRIPT%".
  exit /b 3
)

set "IDENTITY=%LIVENESS%-%SHARD_INDEX%-of-%SHARD_COUNT%"
set "LOG=%UPLOAD%\SOS.Tests-%RID%-%CONFIGURATION%-%IDENTITY%.log"

powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass ^
  -File "%SIGNATURE_SCRIPT%" -RepoRoot "%ROOT%" -StateDirectory "%SIGNATURE_STATE%" -TargetArchitecture "%TARGET_ARCH%"
set "SIGNATURE_EXIT_CODE=%ERRORLEVEL%"
if not "%SIGNATURE_EXIT_CODE%"=="0" (
  powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass ^
    -File "%SIGNATURE_SCRIPT%" -Restore -RepoRoot "%ROOT%" -StateDirectory "%SIGNATURE_STATE%" -TargetArchitecture "%TARGET_ARCH%"
  exit /b %SIGNATURE_EXIT_CODE%
)

"%DOTNET_ROOT%\dotnet.exe" "%TEST_DLL%" ^
  --results-directory "%UPLOAD%" ^
  --report-xunit ^
  --report-xunit-filename "SOS.Tests-%RID%-%CONFIGURATION%-%IDENTITY%.xml" ^
  --report-xunit-html ^
  --report-xunit-html-filename "SOS.Tests-%RID%-%CONFIGURATION%-%IDENTITY%.html" ^
  --report-trx ^
  --report-trx-filename "SOS.Tests-%RID%-%CONFIGURATION%-%IDENTITY%.trx" ^
  --auto-reporters off > "%LOG%" 2>&1
set "EXIT_CODE=%ERRORLEVEL%"

powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass ^
  -File "%SIGNATURE_SCRIPT%" -Restore -RepoRoot "%ROOT%" -StateDirectory "%SIGNATURE_STATE%" -TargetArchitecture "%TARGET_ARCH%"
set "RESTORE_EXIT_CODE=%ERRORLEVEL%"

type "%LOG%"
if not "%RESTORE_EXIT_CODE%"=="0" exit /b %RESTORE_EXIT_CODE%
exit /b %EXIT_CODE%
