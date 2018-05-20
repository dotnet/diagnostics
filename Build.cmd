@echo off
call %~dp0eng\Build.cmd -restore -build %*
exit /b %ErrorLevel%
