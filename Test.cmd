@echo off
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0eng\build.ps1""" -test -skipmanaged -skipnative %*"
exit /b %ErrorLevel%
