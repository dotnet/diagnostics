@echo off
call %~dp0eng\Build.cmd -restore %*
exit /b %ErrorLevel%
