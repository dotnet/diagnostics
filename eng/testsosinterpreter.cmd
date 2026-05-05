set SOS_TEST_INTERPRETER=true
%~dp0..\.dotnet\dotnet.exe test --no-build --logger "console;verbosity=detailed" %~dp0..\src\tests\SOS.UnitTests\SOS.UnitTests.csproj --filter "FullyQualifiedName~SOSInterpreterTests"
