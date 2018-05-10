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

if [[ ! "$LLDB_PATH" = "" ]]; then
    if [[ ! -x "$LLDB_PATH" ]]; then
	echo "LLDB_PATH not executable"
	exit 1
    fi
    __LLDB_Path=$LLDB_PATH
else
    __LLDB_Path=$(which lldb-3.9.1 2> /dev/null)
    if [ $? != 0 ]; then
	__LLDB_Path=$(which lldb-3.9 2> /dev/null)
	if [ $? != 0 ]; then
	    __LLDB_Path=$(which lldb 2> /dev/null)
	    if [ $? != 0 ]; then
		echo "Can not find lldb"
		exit 1
	    fi
	fi
    fi
fi

echo $__LLDB_Path

cd $__ProjectRoot/src/SOS/tests/
rm -f StressLog.txt
python2 $__ProjectRoot/src/SOS/tests/test_libsosplugin.py --lldb $__LLDB_Path --host $__Host --plugin $__Plugin --logfiledir $__LogFileDir --assembly $__TestProgram

