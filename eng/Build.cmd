@echo off
setlocal

:: remove the -test and -build-native options and pass it to build-native.cmd
set __args="%*"
set __args=%__args:-test=%
set __args=%__args:-build-native=%
if %__args% == "" set __args=

:: build managed components
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0common\Build.ps1""" %__args%"
if NOT '%ERRORLEVEL%' == '0' (exit /b %ERRORLEVEL%)

:: build native componments and test managed/native
call %~dp0build-native.cmd %*
exit /b %ERRORLEVEL%
