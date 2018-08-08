@echo off
powershell -ExecutionPolicy ByPass -NoProfile -command "& """%~dp0common\Build.ps1""" -restore -sign -pack %*"
exit /b %ErrorLevel%
