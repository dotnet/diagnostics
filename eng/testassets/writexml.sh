_REPOROOT_=../..
_DUMPPATH_=$1
_XMLPATH_=$1.xml
_DEBUGGEEPATH_=$2
export DOTNET_DIAGNOSTIC_EXTENSIONS=$_REPOROOT_/artifacts/bin/Microsoft.Diagnostics.DebugServices.UnitTests/Debug/netcoreapp3.1/Microsoft.Diagnostics.DebugServices.UnitTests.dll
$_REPOROOT_/.dotnet/dotnet $_REPOROOT_/artifacts/bin/dotnet-dump/Debug/netcoreapp3.1/publish/dotnet-dump.dll analyze $_DUMPPATH_ -c "setsymbolserver -directory $_DEBUGGEEPATH_" -c "writetestdata $_XMLPATH_" -c "exit"

