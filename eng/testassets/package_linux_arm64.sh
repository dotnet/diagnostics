_REPOROOT_=../..
$_REPOROOT_/.dotnet/dotnet pack pack.csproj -p:NuspecFile=TestAssets.Linux.arm64.3.1.nuspec 
$_REPOROOT_/.dotnet/dotnet pack pack.csproj -p:NuspecFile=TestAssets.Linux.arm64.5.0.nuspec 
$_REPOROOT_/.dotnet/dotnet pack pack.csproj -p:NuspecFile=TestAssets.Linux.arm64.6.0.nuspec 
