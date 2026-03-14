@echo off
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0eng\build.ps1""" -restore -skipmanaged -skipnative %*"
exit /b %ErrorLevel%
