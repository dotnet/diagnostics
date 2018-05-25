@echo off
call %~dp0Build.cmd -restore -build -test -sign -ci %*
exit /b %ErrorLevel%
