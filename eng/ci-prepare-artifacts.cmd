@echo off
setlocal

set "_commonArgs=-restore -ci -prepareMachine -verbosity minimal -configuration Release"
set "_logDir=%~dp0..\artifacts\log\Release\"

echo Creating packages
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0common\Build.ps1""" %_commonArgs% -pack -noBl /bl:'%_logDir%Pack.binlog' %*"
if NOT '%ERRORLEVEL%' == '0' goto ExitWithCode

echo Creating bundles
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0Build.ps1""" %_commonArgs% -build -bundletools %*"
if NOT '%ERRORLEVEL%' == '0' goto ExitWithCode

echo Creating dbgshim packages
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0common\Build.ps1""" %_commonArgs% -pack -noBl /bl:'%_logDir%PackDbgShim.binlog' -projects %~dp0..\src\dbgshim\pkg\Microsoft.Diagnostics.DbgShim.proj %*"
if NOT '%ERRORLEVEL%' == '0' goto ExitWithCode

echo Signing and publishing manifest
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0common\Build.ps1""" %_commonArgs% -sign -publish -noBl /bl:'%_logDir%SignPublish.binlog' %*"
if NOT '%ERRORLEVEL%' == '0' goto ExitWithCode

exit /b 0

:ExitWithCode
exit /b !__exitCode!
