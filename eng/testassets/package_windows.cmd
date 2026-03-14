setlocal
set _REPOROOT_=%~dp0..\..
@rem %1 is the architecture x64, x86 or arm64
%_REPOROOT_%\.dotnet\dotnet.exe pack pack.csproj -p:NuspecFile=TestAssets.Windows.%1.3.1.nuspec
%_REPOROOT_%\.dotnet\dotnet.exe pack pack.csproj -p:NuspecFile=TestAssets.Windows.%1.5.0.nuspec 
%_REPOROOT_%\.dotnet\dotnet.exe pack pack.csproj -p:NuspecFile=TestAssets.Windows.%1.6.0.nuspec
