set SOS_TEST_DAC_MODE=cdacfallback
%~dp0..\.dotnet\dotnet.exe test --no-build --logger "console;verbosity=detailed" %~dp0..\src\tests\SOS.UnitTests\SOS.UnitTests.csproj --filter "Category=CDACCompatible"
