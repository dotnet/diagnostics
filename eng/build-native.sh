#!/usr/bin/env bash
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

# Obtain the location of the bash script to figure out where the root of the repo is.
source="${BASH_SOURCE[0]}"

# Resolve $source until the file is no longer a symlink
while [[ -h "$source" ]]; do
  scriptroot="$( cd -P "$( dirname "$source" )" && pwd )"
  source="$(readlink "$source")"
  # if $source was a relative symlink, we need to resolve it relative to the path where the
  # symlink file was located
  [[ $source != /* ]] && source="$scriptroot/$source"
done
__ProjectRoot="$( cd -P "$( dirname "$source" )/.." && pwd )"

__BuildOS=Linux
__HostOS=Linux
__BuildArch=x64
__HostArch=x64
__BuildType=Debug
__PortableBuild=1
__ExtraCmakeArgs=""
__ClangMajorVersion=0
__ClangMinorVersion=0
__CrossBuild=0
__NumProc=1
__Build=0
__Test=0
__TestArgs=
__UnprocessedBuildArgs=

# resolve python-version to use
if [ "$PYTHON" == "" ] ; then
    if ! PYTHON=$(command -v python2.7 || command -v python2 || command -v python)
    then
       echo "Unable to locate build-dependency python2.x!" 1>&2
       exit 1
    fi
fi

# validate python-dependency
# useful in case of explicitly set option.
if ! command -v $PYTHON > /dev/null
then
   echo "Unable to locate build-dependency python2.x ($PYTHON)!" 1>&2
   exit 1
fi

echo $PYTHON

usage()
{
    echo "Usage: $0 [options]"
    echo "--build - build native components"
    echo "--test - test native components"
    echo "--architecture <x64|x86|arm|armel|arm64>"
    echo "--configuration <debug|release>"
    echo "--clangx.y - optional argument to build using clang version x.y"
    echo "--verbosity <q[uiet]|m[inimal]|n[ormal]|d[etailed]|diag[nostic]>"
    echo "--help - this help message"
    exit 1
}


# args:
# input - $1
to_lowercase() {
    #eval $invocation

    echo "$1" | tr '[:upper:]' '[:lower:]'
    return 0
}

# Argument types supported by this script:
#
# Build architecture - valid values are: x64, x86, arm, armel, arm64
# Build Type         - valid values are: debug, release
#
# Set the default arguments for build

# Use uname to determine what the CPU is.
CPUName=$(uname -p)
# Some Linux platforms report unknown for platform, but the arch for machine.
if [ "$CPUName" == "unknown" ]; then
    CPUName=$(uname -m)
fi

case $CPUName in
    i686)
        echo "Unsupported CPU $CPUName detected, build might not succeed!"
        __BuildArch=x86
        __HostArch=x86
        ;;

    x86_64)
        __BuildArch=x64
        __HostArch=x64
        ;;

    armv7l)
        echo "Unsupported CPU $CPUName detected, build might not succeed!"
        __BuildArch=arm
        __HostArch=arm
        ;;

    aarch64)
        __BuildArch=arm64
        __HostArch=arm64
        ;;

    *)
        echo "Unknown CPU $CPUName detected, configuring as if for x64"
        __BuildArch=x64
        __HostArch=x64
        ;;
esac

# Use uname to determine what the OS is.
OSName=$(uname -s)
case $OSName in
    Linux)
        __BuildOS=Linux
        __HostOS=Linux
        ;;

    Darwin)
        __BuildOS=OSX
        __HostOS=OSX
        ;;

    FreeBSD)
        __BuildOS=FreeBSD
        __HostOS=FreeBSD
        ;;

    OpenBSD)
        __BuildOS=OpenBSD
        __HostOS=OpenBSD
        ;;

    NetBSD)
        __BuildOS=NetBSD
        __HostOS=NetBSD
        ;;

    SunOS)
        __BuildOS=SunOS
        __HostOS=SunOS
        ;;

    *)
        echo "Unsupported OS $OSName detected, configuring as if for Linux"
        __BuildOS=Linux
        __HostOS=Linux
        ;;
esac

while :; do
    if [ $# -le 0 ]; then
        break
    fi

    lowerI="$(to_lowercase "$1")"
    case $lowerI in
        -\?|-h|--help)
            usage
            exit 1
            ;;

        --build)
            __Build=1
            ;;

        --test)
            __Test=1
            ;;

        # Passed to common build script when testing
        --ci)
            __TestArgs="$__TestArgs $1"
            ;;

        --solution)
            __TestArgs="$__TestArgs $1 $2"
            ;;

        --verbosity)
            __TestArgs="$__TestArgs $1 $2"
            shift
            ;;

        # Ignored for a native build
        --rebuild|--sign|--restore|--pack|--preparemachine)
            ;;

        --configuration)
            __BuildType="$(to_lowercase "$2")"
            shift
            ;;

        --architecture)
            __BuildArch="$(to_lowercase "$2")"
            shift
            ;;

        --clang3.5)
            __ClangMajorVersion=3
            __ClangMinorVersion=5
            ;;

        --clang3.6)
            __ClangMajorVersion=3
            __ClangMinorVersion=6
            ;;

        --clang3.7)
            __ClangMajorVersion=3
            __ClangMinorVersion=7
            ;;

        --clang3.8)
            __ClangMajorVersion=3
            __ClangMinorVersion=8
            ;;

        --clang3.9)
            __ClangMajorVersion=3
            __ClangMinorVersion=9
            ;;

        --clang4.0)
            __ClangMajorVersion=4
            __ClangMinorVersion=0
            ;;

        --clang5.0)
            __ClangMajorVersion=5
            __ClangMinorVersion=0
            ;;

        --verbosity)
            __TestArgs="$__TestArgs --verbosity $2"
            shift
            ;;

        *)
            __UnprocessedBuildArgs="$__UnprocessedBuildArgs $1"
            ;;
    esac

    shift
done

if [ "$__BuildType" == "release" ]; then
    __BuildType=Release
fi
if [ "$__BuildType" == "debug" ]; then
    __BuildType=Debug
fi

__RootBinDir=$__ProjectRoot/artifacts
__IntermediatesDir="$__RootBinDir/obj/$__BuildOS.$__BuildArch.$__BuildType"
__LogFileDir="$__RootBinDir/log/$__BuildOS.$__BuildArch.$__BuildType"
__ExtraCmakeArgs="-DCLR_MANAGED_BINARY_DIR=$__RootBinDir/$__BuildType/bin"

# Specify path to be set for CMAKE_INSTALL_PREFIX.
# This is where all built native libraries will copied to.
export __CMakeBinDir="$__RootBinDir/bin/$__BuildOS.$__BuildArch.$__BuildType"

# Set default clang version
if [[ $__ClangMajorVersion == 0 && $__ClangMinorVersion == 0 ]]; then
   if [[ "$__BuildArch" == "arm" || "$__BuildArch" == "armel" ]]; then
       __ClangMajorVersion=5
       __ClangMinorVersion=0
   else
       __ClangMajorVersion=3
       __ClangMinorVersion=9
   fi
fi

if [[ "$__BuildArch" == "armel" ]]; then
    # Armel cross build is Tizen specific and does not support Portable RID build
    __PortableBuild=0
fi

mkdir -p "$__IntermediatesDir"
mkdir -p "$__LogFileDir"
mkdir -p "$__CMakeBinDir"

build_native()
{
    platformArch="$1"
    intermediatesForBuild="$2"
    extraCmakeArguments="$3"

    # All set to commence the build
    echo "Commencing $__DistroRid build for $__BuildOS.$__BuildArch.$__BuildType in $intermediatesForBuild"

    generator=""
    buildFile="Makefile"
    buildTool="make"

    pushd "$intermediatesForBuild"
    echo "Invoking \"$__ProjectRoot/eng/gen-buildsys-clang.sh\" \"$__ProjectRoot\" $__ClangMajorVersion $__ClangMinorVersion $platformArch $__BuildType $generator $extraCmakeArguments $__cmakeargs"
    "$__ProjectRoot/eng/gen-buildsys-clang.sh" "$__ProjectRoot" $__ClangMajorVersion $__ClangMinorVersion $platformArch $__BuildType $generator "$extraCmakeArguments" "$__cmakeargs"
    popd

    if [ ! -f "$intermediatesForBuild/$buildFile" ]; then
        echo "Failed to generate build project!"
        exit 1
    fi

    # Check that the makefiles were created.
    pushd "$intermediatesForBuild"

    echo "Executing $buildTool install -j $__NumProc"

    $buildTool install -j $__NumProc
    if [ $? != 0 ]; then
        echo "Failed to build."
        exit 1
    fi

    popd
}

initHostDistroRid()
{
    __HostDistroRid=""
    if [ "$__HostOS" == "Linux" ]; then
        if [ -e /etc/os-release ]; then
            source /etc/os-release
            if [[ $ID == "rhel" ]]; then
                # remove the last version digit
                VERSION_ID=${VERSION_ID%.*}
            fi
            __HostDistroRid="$ID.$VERSION_ID-$__HostArch"
            if [[ $ID == "alpine" ]]; then
                __HostDistroRid="linux-musl-$__HostArch"
            fi
        elif [ -e /etc/redhat-release ]; then
            local redhatRelease=$(</etc/redhat-release)
            if [[ $redhatRelease == "CentOS release 6."* || $redhatRelease == "Red Hat Enterprise Linux Server release 6."* ]]; then
               __HostDistroRid="rhel.6-$__HostArch"
            fi
        fi
    fi
    if [ "$__HostOS" == "FreeBSD" ]; then
        __freebsd_version=`sysctl -n kern.osrelease | cut -f1 -d'.'`
        __HostDistroRid="freebsd.$__freebsd_version-$__HostArch"
    fi

    if [ "$__HostDistroRid" == "" ]; then
        echo "WARNING: Can not determine runtime id for current distro."
    fi
}

initTargetDistroRid()
{
    if [ $__CrossBuild == 1 ]; then
        if [ "$__BuildOS" == "Linux" ]; then
            if [ ! -e $ROOTFS_DIR/etc/os-release ]; then
                if [ -e $ROOTFS_DIR/android_platform ]; then
                    source $ROOTFS_DIR/android_platform
                    export __DistroRid="$RID"
                else
                    echo "WARNING: Can not determine runtime id for current distro."
                    export __DistroRid=""
                fi
            else
                source $ROOTFS_DIR/etc/os-release
                export __DistroRid="$ID.$VERSION_ID-$__BuildArch"
            fi
        fi
    else
        export __DistroRid="$__HostDistroRid"
    fi

    if [ "$__BuildOS" == "OSX" ]; then
        __PortableBuild=1
    fi

    # Portable builds target the base RID
    if [ $__PortableBuild == 1 ]; then
        if [ "$__BuildOS" == "Linux" ]; then
            export __DistroRid="linux-$__BuildArch"
        elif [ "$__BuildOS" == "OSX" ]; then
            export __DistroRid="osx-$__BuildArch"
        elif [ "$__BuildOS" == "FreeBSD" ]; then
            export __DistroRid="freebsd-$__BuildArch"
        fi
    fi
}


# Init the host distro name
initHostDistroRid

# Init the target distro name
initTargetDistroRid

if [ "$__HostOS" == "OSX" ]; then
    export LLDB_H=$__ProjectRoot/src/SOS/lldbplugin/swift-4.0
    export LLDB_LIB=/Applications/Xcode.app/Contents/SharedFrameworks/LLDB.framework/LLDB
    export LLDB_PATH=/Applications/Xcode.app/Contents/Developer/usr/bin/lldb

    # If Xcode 9.2 exists (like on the CI/build machines), use that. Xcode 9.3 or 
    # greater (swift 4.1 lldb) doesn't work that well (seg faults on exit).
    if [ -f "/Applications/Xcode_9.2.app/Contents/Developer/usr/bin/lldb" ]; then
        if [ -f "/Applications/Xcode_9.2.app/Contents/SharedFrameworks/LLDB.framework/LLDB" ]; then
            export LLDB_PATH=/Applications/Xcode_9.2.app/Contents/Developer/usr/bin/lldb
            export LLDB_LIB=/Applications/Xcode_9.2.app/Contents/SharedFrameworks/LLDB.framework/LLDB
        fi
    fi
fi

# Build native components
if [ $__Build == 1 ]; then
    build_native "$__BuildArch" "$__IntermediatesDir" "$__ExtraCmakeArgs"
fi

# Run SOS/lldbplugin tests
if [ $__Test == 1 ]; then

    if [ "$LLDB_PATH" = "" ]; then
        export LLDB_PATH="$(which lldb-3.9.1 2> /dev/null)"
        if [ "$LLDB_PATH" = "" ]; then
            export LLDB_PATH="$(which lldb-3.9 2> /dev/null)"
            if [ "$LLDB_PATH" = "" ]; then
                export LLDB_PATH="$(which lldb-4.0 2> /dev/null)"
                if [ "$LLDB_PATH" = "" ]; then
                    export LLDB_PATH="$(which lldb-5.0 2> /dev/null)"
                    if [ "$LLDB_PATH" = "" ]; then
                        export LLDB_PATH="$(which lldb 2> /dev/null)"
                    fi
                fi
            fi
        fi
    fi

    if [ "$GDB_PATH" = "" ]; then
        export GDB_PATH="$(which gdb 2> /dev/null)"
    fi

    echo "lldb: '$LLDB_PATH' gdb: '$GDB_PATH'"

    # Run xunit SOS tests
    "$__ProjectRoot/eng/common/build.sh" --test --configuration "$__BuildType" "$__TestArgs"
    if [ $? != 0 ]; then
        exit 1
    fi

    if [ "$__BuildOS" == "OSX" ]; then
        __Plugin=$__CMakeBinDir/libsosplugin.dylib
    else
        __Plugin=$__CMakeBinDir/libsosplugin.so
    fi

    # Run lldb python tests
    "$__ProjectRoot/src/SOS/tests/testsos.sh" "$__ProjectRoot" "$__Plugin" "$__RootBinDir/$__BuildType/bin" "$__LogFileDir"
    if [ $? != 0 ]; then
        exit 1
    fi
fi

echo "BUILD: Repo sucessfully built."
echo "BUILD: Product binaries are available at $__CMakeBinDir"
