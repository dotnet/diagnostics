@echo off
call %~dp0eng\Build.cmd -test %*
exit /b %ErrorLevel%
