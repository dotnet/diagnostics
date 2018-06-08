@echo off
call %~dp0Build.cmd -restore -build -test -ci %*
exit /b %ErrorLevel%
