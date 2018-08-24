@echo off
call %~dp0eng\Build.cmd -restore -build -build-native %*
exit /b %ErrorLevel%
