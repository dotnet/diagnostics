@echo off
setlocal

if "%DIAGNOSTICS_PACKAGE_CONFIGURATION%" == "" set "DIAGNOSTICS_PACKAGE_CONFIGURATION=Release"

set "_commonArgs=-restore -ci -prepareMachine -verbosity minimal -configuration %DIAGNOSTICS_PACKAGE_CONFIGURATION%"
set "_logDir=%~dp0..\artifacts\log\%DIAGNOSTICS_PACKAGE_CONFIGURATION%\"

echo Creating packages
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0common\Build.ps1""" %_commonArgs% -pack -noBl /bl:'%_logDir%Pack.binlog' %*"
if NOT '%ERRORLEVEL%' == '0' goto ExitWithCode

echo Creating bundles
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0Build.ps1""" %_commonArgs% -build -bundletools %*"
if NOT '%ERRORLEVEL%' == '0' goto ExitWithCode

echo Creating dbgshim packages
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0common\Build.ps1""" %_commonArgs% -pack -noBl /bl:'%_logDir%PackDbgShim.binlog' -projects %~dp0..\src\dbgshim\pkg\Microsoft.Diagnostics.DbgShim.proj %*"
if NOT '%ERRORLEVEL%' == '0' goto ExitWithCode

echo Creating internal diagnostics assets packages
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0common\Build.ps1""" %_commonArgs% -pack -noBl /bl:'%_logDir%PackInternalAssets.binlog' -projects %~dp0..\src\pkg\InternalAssets\packages.proj %*"
if NOT '%ERRORLEVEL%' == '0' goto ExitWithCode

echo Signing and publishing manifest
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0common\Build.ps1""" %_commonArgs% -sign -publish -noBl /bl:'%_logDir%SignPublish.binlog' %*"
if NOT '%ERRORLEVEL%' == '0' goto ExitWithCode

exit /b 0

:ExitWithCode
exit /b !__exitCode!
