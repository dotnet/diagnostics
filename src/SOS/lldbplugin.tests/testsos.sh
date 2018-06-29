#!/usr/bin/env bash
__ProjectRoot="$1"
__Plugin="$2"
__ManagedBinDir="$3"
__ResultsDir="$4"

if [[ "$__ProjectRoot" = "" || "$__Plugin" = "" || "$__ManagedBinDir" = "" || "$__ResultsDir" = "" ]]; then
    echo "Project root, plugin or log directory required"
    exit 1
fi

__Host=$__ProjectRoot/.dotnet/dotnet
__TestProgram=$__ManagedBinDir/TestDebuggee/netcoreapp2.0/TestDebuggee.dll

# Turn on stress logging so the dumplog and histinit commands pass
export COMPlus_LogFacility=0xffffffbf
export COMPlus_LogLevel=6
export COMPlus_StressLog=1
export COMPlus_StressLogSize=65536

if [[ ! -x "$LLDB_PATH" ]]; then
    echo "LLDB_PATH doesn't exist or not executable"
    exit 1
fi

__LogFileDir=$__ResultsDir/lldbplugin.tests_$(date +%Y_%m_%d_%H_%M_%S)
mkdir -p $__LogFileDir

cd $__ProjectRoot/src/SOS/lldbplugin.tests/
rm -f StressLog.txt
python $__ProjectRoot/src/SOS/lldbplugin.tests/test_libsosplugin.py --lldb $LLDB_PATH --host $__Host --plugin $__Plugin --logfiledir $__LogFileDir --assembly $__TestProgram

