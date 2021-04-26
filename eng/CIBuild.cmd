@echo off
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0build.ps1""" -restore -ci %*"
exit /b %ErrorLevel%
