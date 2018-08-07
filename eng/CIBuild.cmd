@echo off
call %~dp0Build.cmd -restore -build -test -publish -sign -pack -ci %*
exit /b %ErrorLevel%
