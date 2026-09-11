@echo off
setlocal EnableExtensions

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
set "SIGNATURE_SCRIPT=%ROOT%\eng\DisableSignatureCheck.ps1"
set "SIGNATURE_REPO=%HELIX_WORKITEM_ROOT%\sos-signature-repo"
set "SIGNATURE_RUNTIME=%SIGNATURE_REPO%\artifacts\dotnet-test"
set "POWERSHELL_EXE=powershell.exe"
if /I "%TARGET_ARCH%"=="x86" set "POWERSHELL_EXE=%SystemRoot%\SysWOW64\WindowsPowerShell\v1.0\powershell.exe"

if not defined TEST_DLL (
  echo SOS.Tests.dll was not found for %CONFIGURATION%.
  exit /b 3
)

if not exist "%SIGNATURE_SCRIPT%" (
  echo DisableSignatureCheck.ps1 was not found at "%SIGNATURE_SCRIPT%".
  exit /b 3
)

if not exist "%DEBUGGER_HOST%" (
  echo cdb.exe was not found at "%DEBUGGER_HOST%".
  exit /b 3
)

if not exist "%SIGNATURE_REPO%\artifacts" mkdir "%SIGNATURE_REPO%\artifacts"
if not exist "%SIGNATURE_RUNTIME%\." (
  mklink /J "%SIGNATURE_RUNTIME%" "%PAYLOAD_DOTNET_ROOT%"
  if errorlevel 1 exit /b 3
)

set "DOTNET_ROOT=%SIGNATURE_RUNTIME%"
set "DOTNET_ROOT_X86=%DOTNET_ROOT%"

set "IDENTITY=all"
set "LOG=%UPLOAD%\SOS.Tests-%RID%-%CONFIGURATION%-%IDENTITY%.log"

"%POWERSHELL_EXE%" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass ^
  -File "%SIGNATURE_SCRIPT%" -RepoRoot "%SIGNATURE_REPO%"
set "SIGNATURE_EXIT_CODE=%ERRORLEVEL%"
if not "%SIGNATURE_EXIT_CODE%"=="0" (
  "%POWERSHELL_EXE%" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass ^
    -File "%SIGNATURE_SCRIPT%" -Restore -RepoRoot "%SIGNATURE_REPO%"
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

"%POWERSHELL_EXE%" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass ^
  -File "%SIGNATURE_SCRIPT%" -Restore -RepoRoot "%SIGNATURE_REPO%"
set "RESTORE_EXIT_CODE=%ERRORLEVEL%"

type "%LOG%"
if not "%RESTORE_EXIT_CODE%"=="0" exit /b %RESTORE_EXIT_CODE%
exit /b %EXIT_CODE%
