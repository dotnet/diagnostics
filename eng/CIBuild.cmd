@echo off
call %~dp0Build.cmd -restore -build -build-native -test -publish -ci %*
exit /b %ErrorLevel%
