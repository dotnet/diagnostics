# $1 is architecture arm64 or x64
_REPOROOT_=../..
$_REPOROOT_/.dotnet/dotnet pack pack.csproj -p:NuspecFile=TestAssets.Linux.$1.3.1.nuspec 
$_REPOROOT_/.dotnet/dotnet pack pack.csproj -p:NuspecFile=TestAssets.Linux.$1.5.0.nuspec 
$_REPOROOT_/.dotnet/dotnet pack pack.csproj -p:NuspecFile=TestAssets.Linux.$1.6.0.nuspec 
