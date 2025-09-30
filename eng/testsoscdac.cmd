set SOS_TEST_CDAC=true
%~dp0..\.dotnet\dotnet.exe test --no-build --logger "console;verbosity=detailed" %~dp0..\src\tests\SOS.Tests\SOS.Tests.csproj --filter "Category=CDACCompatible"
