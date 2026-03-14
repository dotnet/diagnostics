@if not defined _echo echo off

call :run_command dotnet.exe run -c Debug --no-restore --no-build -- %*
exit /b %ERRORLEVEL%

:run_command
    echo/%USERNAME%@%COMPUTERNAME% "%CD%"
    echo/[%DATE% %TIME%] $ %*
    echo/
    call %*
    exit /b %ERRORLEVEL%
