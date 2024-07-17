@echo off
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0build.ps1""" -privatebuild -skipmanaged -skipnative %*"
exit /b %ErrorLevel%
