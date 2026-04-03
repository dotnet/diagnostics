@echo off
setlocal

set engroot=%~dp0
set reporoot=%engroot%..

powershell -ExecutionPolicy ByPass -NoProfile -command "& ""%engroot%common\msbuild.ps1"" %engroot%InstallRuntimes.proj /t:InstallTestRuntimes %*"
exit /b %ErrorLevel%
