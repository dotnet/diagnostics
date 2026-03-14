@echo off
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0build.ps1""" -installruntimes -skipmanaged -skipnative %*"
exit /b %ErrorLevel%
