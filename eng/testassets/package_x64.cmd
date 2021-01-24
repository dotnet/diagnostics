setlocal
set _REPOROOT_=%~dp0..\..
%_REPOROOT_%\.dotnet\dotnet.exe pack pack.csproj -p:NuspecFile=TestAssets.Windows.x64.3.1.nuspec
%_REPOROOT_%\.dotnet\dotnet.exe pack pack.csproj -p:NuspecFile=TestAssets.Windows.x64.5.0.nuspec 
%_REPOROOT_%\.dotnet\dotnet.exe pack pack.csproj -p:NuspecFile=TestAssets.Windows.x64.6.0.nuspec
