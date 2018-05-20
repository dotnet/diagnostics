@echo off

rem build/test managed components
powershell -ExecutionPolicy ByPass -command "& """%~dp0common\Build.ps1""" %*"
if NOT '%ERRORLEVEL%' == '0' exit /b %ERRORLEVEL%

rem build/test native componments
call %~dp0build-native.cmd %*
exit /b %ERRORLEVEL%
