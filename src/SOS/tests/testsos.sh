#!/usr/bin/env bash
__ProjectRoot="$1"
__Plugin="$2"
__ManagedBinDir="$3"
__LogFileDir="$4"

if [[ "$__ProjectRoot" = "" || "$__Plugin" = "" || "$__ManagedBinDir" = "" || "$__LogFileDir" = "" ]]; then
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

cd $__ProjectRoot/src/SOS/tests/
rm -f StressLog.txt
python $__ProjectRoot/src/SOS/tests/test_libsosplugin.py --lldb $LLDB_PATH --host $__Host --plugin $__Plugin --logfiledir $__LogFileDir --assembly $__TestProgram
if [ $? != 0 ]; then
    cat $__LogFileDir/*.log
    echo "LLDB python tests FAILED"
    exit 1
fi
echo "LLDB python tests PASSED"
