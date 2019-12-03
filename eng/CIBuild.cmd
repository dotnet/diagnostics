@echo off
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0build.ps1""" -restore -build -native -test -ci %*"
exit /b %ErrorLevel%
