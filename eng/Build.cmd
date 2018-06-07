@echo off
setlocal

:: remove the -test option and pass it to build-native.cmd
set "__args=%*"
set "__args=%__args:-test=%"

:: build managed components
powershell -ExecutionPolicy ByPass -command "& """%~dp0common\Build.ps1""" %__args%"
if NOT '%ERRORLEVEL%' == '0' exit /b %ERRORLEVEL%

:: build native componments and test managed/native
call %~dp0build-native.cmd %*
exit /b %ERRORLEVEL%
