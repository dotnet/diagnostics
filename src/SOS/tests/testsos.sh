#!/usr/bin/env bash
__ProjectRoot="$1"
__NativeBinDir="$2"
__ManagedBinDir="$3"
__LogFileDir="$4"
__BuildArch="$5"

if [[ "$__ProjectRoot" = "" || "$__NativeBinDir" = "" || "$__ManagedBinDir" = "" || "$__LogFileDir" = "" ]]; then
    echo "Project root, bin or log directory required"
    exit 1
fi

__Plugin=$__NativeBinDir/libsosplugin.so
__Host=$__ProjectRoot/.dotnet/dotnet
__TestProgram=$__ManagedBinDir/TestDebuggee/netcoreapp2.0/TestDebuggee.dll

cd $__ProjectRoot/src/SOS/tests/
rm StressLog.txt
python2 $__ProjectRoot/src/SOS/tests/test_libsosplugin.py --lldb $LLDB_PATH --host $__Host --plugin $__Plugin --logfiledir $__LogFileDir --assembly $__TestProgram

