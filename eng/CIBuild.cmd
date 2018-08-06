@echo off
call %~dp0Build.cmd -restore -build -test -sign -pack -publish -ci %*
exit /b %ErrorLevel%
