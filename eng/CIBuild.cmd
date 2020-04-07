@echo off
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0build.ps1""" -restore -test -ci %*"
exit /b %ErrorLevel%
