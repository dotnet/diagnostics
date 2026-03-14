@if not defined _echo echo off
cls

dotnet.exe restore "%~dp0dotnet-trace.csproj" --packages "%~dp0..\..\..\artifacts\packages" || (
    echo [ERROR] Failed to restore.
    exit /b 1
)

for %%c in (Debug Release) do (
    dotnet.exe build "%~dp0dotnet-trace.csproj" -c %%c --no-restore || (
        echo [ERROR] Failed to build %%c.
        exit /b 1
    )
)
