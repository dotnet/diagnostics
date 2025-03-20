set SOS_TEST_CDAC=true
%~dp0..\.dotnet\dotnet.exe test --no-build --logger "console;verbosity=detailed" %~dp0..\src\SOS\SOS.UnitTests\SOS.UnitTests.csproj --filter "Category=CDACCompatible"
