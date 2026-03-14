setlocal
set _REPOROOT_=%~dp0..\..
set _DUMPPATH_=%1
set _XMLPATH_=%1.xml
set _DEBUGGEEPATH_=%2
set DOTNET_DIAGNOSTIC_EXTENSIONS=%_REPOROOT_%\artifacts\bin\Microsoft.Diagnostics.DebugServices.UnitTests\Debug\net6.0\Microsoft.Diagnostics.DebugServices.UnitTests.dll
%_REPOROOT_%\.dotnet\dotnet.exe --fx-version 6.0.8 %_REPOROOT_%\artifacts\bin\dotnet-dump\Debug\netcoreapp3.1\publish\dotnet-dump.dll analyze %_DUMPPATH_% -c "setsymbolserver -directory %_DEBUGGEEPATH_%" -c "writetestdata %_XMLPATH_%" -c "exit"

