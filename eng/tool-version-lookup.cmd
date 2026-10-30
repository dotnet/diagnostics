@echo off
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0tool-version-lookup.ps1""" %*"
exit /b %ErrorLevel%
