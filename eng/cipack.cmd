@echo off
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0common\Build.ps1""" -restore -sign -pack -publish -ci %*"
exit /b %ErrorLevel%
