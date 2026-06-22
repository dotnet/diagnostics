@echo off
setlocal

set engroot=%~dp0
set reporoot=%engroot%..

powershell -NoProfile -ExecutionPolicy ByPass -File "%engroot%common\msbuild.ps1" "%engroot%InstallRuntimes.proj" /t:InstallTestRuntimes %*
exit /b %ErrorLevel%
