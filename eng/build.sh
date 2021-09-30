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
__NumProc=1
__ManagedBuild=true
__NativeBuild=true
__CrossBuild=false
__Test=false
__PrivateBuildPath=""
__CI=false
__Verbosity=minimal
__ManagedBuildArgs=
__TestArgs=
__UnprocessedBuildArgs=
__DotnetRuntimeVersion='default'
__DotnetRuntimeDownloadVersion='default'
__RuntimeSourceFeed=''
__RuntimeSourceFeedKey=''

usage()
{
    echo "Usage: $0 [options]"
    echo "--skipmanaged- Skip building managed components"
    echo "--skipnative - Skip building native components"
    echo "--test - run xunit tests"
    echo "--privatebuildpath - path to local private runtime build to test"
    echo "--architecture <x64|x86|arm|armel|arm64>"
    echo "--configuration <debug|release>"
    echo "--rootfs <ROOTFS_DIR>"
    echo "--stripsymbols - strip symbols into .dbg files"
    echo "--clangx.y - optional argument to build using clang version x.y"
    echo "--ci - CI lab build"
    echo "--verbosity <q[uiet]|m[inimal]|n[ormal]|d[etailed]|diag[nostic]>"
    echo "--help - this help message"
    exit 1
}

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

OSName=$(uname -s)
if [ "$OSName" = "Darwin" ]; then
    # On OSX universal binaries make uname -m unreliable.  The uname -m response changes
    # based on what hardware is being emulated.
    # Use sysctl instead
    if [ "$(sysctl -q -n hw.optional.arm64)" = "1" ]; then
        CPUName=arm64
    elif [ "$(sysctl -q -n hw.optional.x86_64)" = "1" ]; then
        CPUName=x86_64
    else
        CPUName=$(uname -m)
    fi
else
    # Use uname to determine what the CPU is.
    CPUName=$(uname -p)
    # Some Linux platforms report unknown for platform, but the arch for machine.
    if [ "$CPUName" == "unknown" ]; then
        CPUName=$(uname -m)
    fi
fi

case $CPUName in
    i686|i386)
        echo "Unsupported CPU $CPUName detected, build might not succeed!"
        __BuildArch=x86
        __HostArch=x86
        ;;

    x86_64|amd64)
        __BuildArch=x64
        __HostArch=x64
        ;;

    armv7l)
        echo "Unsupported CPU $CPUName detected, build might not succeed!"
        __BuildArch=arm
        __HostArch=arm
        ;;

    aarch64|arm64)
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
    # support both "--" and "-" options
    opt="$(echo "${1/#--/-}" | awk '{print tolower($0)}')"
    case $opt in
        -\?|-h|-help)
            usage
            exit 1
            ;;

        -skipmanaged)
            __ManagedBuild=false
            ;;

        -skipnative)
            __NativeBuild=false
            ;;

        -build|-b)
            __ManagedBuild=true
            ;;

        -test|-t)
            __Test=true
            ;;

        -privatebuildpath)
            __PrivateBuildPath="$2"
            shift
            ;;

        -dotnetruntimeversion)
            __DotnetRuntimeVersion="$2"
            shift
            ;;

        -dotnetruntimedownloadversion)
            __DotnetRuntimeDownloadVersion="$2"
            shift
            ;;

        -runtimesourcefeed)
            __RuntimeSourceFeed="$2"
            shift
            ;;

        -runtimesourcefeedkey)
            __RuntimeSourceFeedKey="$2"
            shift
            ;;

        -ci)
            __CI=true
            __ManagedBuildArgs="$__ManagedBuildArgs $1"
            __TestArgs="$__TestArgs $1"
            ;;

        -projects)
            __ManagedBuildArgs="$__ManagedBuildArgs $1 $2"
            __TestArgs="$__TestArgs $1 $2"
            shift
            ;;

        -verbosity)
            __Verbosity=$2
            shift
            ;;

        -configuration|-c)
            __BuildType="$(to_lowercase "$2")"
            shift
            ;;

        -architecture|-a|-platform)
            __BuildArch="$(to_lowercase "$2")"
            shift
            ;;

        -rootfs)
            export ROOTFS_DIR="$2"
            shift
            ;;

        -portablebuild=false)
            __PortableBuild=0
            ;;

        -stripsymbols)
            __ExtraCmakeArgs="$__ExtraCmakeArgs -DSTRIP_SYMBOLS=true"
            ;;

        -clang*)
            __Compiler=clang
            # clangx.y or clang-x.y
            version="$(echo "$1" | tr -d '[:alpha:]-=')"
            parts=(${version//./ })
            __ClangMajorVersion="${parts[0]}"
            __ClangMinorVersion="${parts[1]}"
            if [[ -z "$__ClangMinorVersion" && "$__ClangMajorVersion" -le 6 ]]; then
                __ClangMinorVersion=0;
            fi
            ;;

        -clean|-binarylog|-bl|-pipelineslog|-pl|-restore|-r|-rebuild|-pack|-integrationtest|-performancetest|-sign|-publish|-preparemachine)
            __ManagedBuildArgs="$__ManagedBuildArgs $1"
            ;;

        -warnaserror|-nodereuse)
            __ManagedBuildArgs="$__ManagedBuildArgs $1 $2"
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

# Needs to be set for generate version source file/msbuild
if [[ -z $NUGET_PACKAGES ]]; then
    if [[ $__CI == true ]]; then
        export NUGET_PACKAGES="$__ProjectRoot/.packages"
    else
        export NUGET_PACKAGES="$HOME/.nuget/packages"
    fi
fi

echo $NUGET_PACKAGES

__RootBinDir=$__ProjectRoot/artifacts
__BinDir=$__RootBinDir/bin/$__BuildOS.$__BuildArch.$__BuildType
__LogDir=$__RootBinDir/log/$__BuildOS.$__BuildArch.$__BuildType
__IntermediatesDir=$__RootBinDir/obj/$__BuildOS.$__BuildArch.$__BuildType
__ExtraCmakeArgs="$__ExtraCmakeArgs -DCLR_MANAGED_BINARY_DIR=$__RootBinDir/bin -DCLR_BUILD_TYPE=$__BuildType"
__DotNetCli=$__ProjectRoot/.dotnet/dotnet

# Specify path to be set for CMAKE_INSTALL_PREFIX.
# This is where all built native libraries will copied to.
export __CMakeBinDir="$__BinDir"


if [[ "$__BuildArch" == "armel" ]]; then
    # Armel cross build is Tizen specific and does not support Portable RID build
    __PortableBuild=0
fi

# Configure environment if we are doing a cross compile.
if [ "${__BuildArch}" != "${__HostArch}" ]; then
    __CrossBuild=true
    export CROSSCOMPILE=1
    if [ "${__BuildOS}" != "OSX" ]; then
        if ! [[ -n "$ROOTFS_DIR" ]]; then
            echo "ERROR: ROOTFS_DIR not set for cross build"
            exit 1
        fi
        echo "ROOTFS_DIR: $ROOTFS_DIR"
    fi
fi

mkdir -p "$__IntermediatesDir"
mkdir -p "$__LogDir"
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
    scriptDir="$__ProjectRoot/eng"

    pushd "$intermediatesForBuild"
    echo "Invoking \"$scriptDir/gen-buildsys-clang.sh\" \"$__ProjectRoot\" $__ClangMajorVersion \"$__ClangMinorVersion\" $platformArch "$scriptDir" $__BuildType $generator $extraCmakeArguments"
    "$scriptDir/gen-buildsys-clang.sh" "$__ProjectRoot" $__ClangMajorVersion "$__ClangMinorVersion" $platformArch "$scriptDir" $__BuildType $generator "$extraCmakeArguments"
    popd

    if [ ! -f "$intermediatesForBuild/$buildFile" ]; then
        echo "Failed to generate build project!"
        exit 1
    fi

    # Check that the makefiles were created.
    pushd "$intermediatesForBuild"

    echo "Executing $buildTool install -j $__NumProc"

    $buildTool install -j $__NumProc | tee $__LogDir/make.log
    if [ $? != 0 ]; then
        echo "Failed to build."
        exit 1
    fi

    popd
}

initTargetDistroRid()
{
    source "$__ProjectRoot/eng/init-distro-rid.sh"

    local passedRootfsDir=""

    # Only pass ROOTFS_DIR if cross is specified and the current platform is not OSX that doesn't use rootfs
    if [ $__CrossBuild == true  -a "$__HostOS" != "OSX" ]; then
        passedRootfsDir=${ROOTFS_DIR}
    fi

    initDistroRidGlobal ${__BuildOS} ${__BuildArch} ${__PortableBuild} ${passedRootfsDir}
}

#
# Managed build
#

if [ $__ManagedBuild == true ]; then
    echo "Commencing managed build for $__BuildType in $__RootBinDir/bin"
    "$__ProjectRoot/eng/common/build.sh" --build --configuration "$__BuildType" --verbosity "$__Verbosity" $__ManagedBuildArgs $__UnprocessedBuildArgs
    if [ $? != 0 ]; then
        exit 1
    fi
fi

#
# Initialize the target distro name
#

initTargetDistroRid

echo "RID: $__DistroRid"

# Set default clang version
if [[ $__ClangMajorVersion == 0 && $__ClangMinorVersion == 0 ]]; then
   if [[ "$__BuildArch" == "arm" || "$__BuildArch" == "armel" ]]; then
       __ClangMajorVersion=5
       __ClangMinorVersion=0
   elif [[ "$__BuildArch" == "arm64" && "$__DistroRid" == "linux-musl-arm64" ]]; then
       __ClangMajorVersion=9
       __ClangMinorVersion=
   else
       __ClangMajorVersion=3
       __ClangMinorVersion=9
   fi
fi

#
# Setup LLDB paths for native build
#

if [ "$__HostOS" == "OSX" ]; then
    export LLDB_H=$__ProjectRoot/src/SOS/lldbplugin/swift-4.0
    export LLDB_LIB=$(xcode-select -p)/../SharedFrameworks/LLDB.framework/LLDB
    export LLDB_PATH=$(xcode-select -p)/usr/bin/lldb

    export MACOSX_DEPLOYMENT_TARGET=10.12

    if [ ! -f $LLDB_LIB ]; then
        echo "Cannot find the lldb library. Try installing Xcode."
        exit 1
    fi

    # Workaround bad python version in /usr/local/bin/python2.7 on lab machines
    export PATH=/usr/bin:$PATH
    which python
    python --version

    if [[ "$__BuildArch" == x64 ]]; then
        __ExtraCmakeArgs="-DCMAKE_OSX_ARCHITECTURES=\"x86_64\" $__ExtraCmakeArgs"
    elif [[ "$__BuildArch" == arm64 ]]; then
        __ExtraCmakeArgs="-DCMAKE_OSX_ARCHITECTURES=\"arm64\" $__ExtraCmakeArgs"
    else
        echo "Error: Unknown OSX architecture $__BuildArch."
        exit 1
    fi
fi

#
# Build native components
#

if [ ! -e $__DotNetCli ]; then
   echo "dotnet cli not installed $__DotNetCli"
   exit 1
fi

if [ $__NativeBuild == true ]; then
    echo "Generating Version Source File"
    __GenerateVersionLog="$__LogDir/GenerateVersion.binlog"

    "$__ProjectRoot/eng/common/msbuild.sh" \
        $__ProjectRoot/eng/CreateVersionFile.csproj \
        /v:$__Verbosity \
        /bl:$__GenerateVersionLog \
        /t:GenerateVersionFiles \
        /restore \
        /p:GenerateVersionSourceFile=true \
        /p:NativeVersionSourceFile="$__IntermediatesDir/version.cpp" \
        /p:Configuration="$__BuildType" \
        /p:Platform="$__BuildArch" \
        $__UnprocessedBuildArgs

    if [ $? != 0 ]; then
        echo "Generating Version Source File FAILED"
        exit 1
    fi

    build_native "$__BuildArch" "$__IntermediatesDir" "$__ExtraCmakeArgs"
fi

#
# Copy the native SOS binaries to where these tools expect for testing
#

if [[ $__NativeBuild == true || $__Test == true ]]; then
    __dotnet_sos=$__RootBinDir/bin/dotnet-sos/$__BuildType/netcoreapp3.1/publish/$__DistroRid
    __dotnet_dump=$__RootBinDir/bin/dotnet-dump/$__BuildType/netcoreapp3.1/publish/$__DistroRid

    mkdir -p "$__dotnet_sos"
    mkdir -p "$__dotnet_dump"

    cp "$__BinDir"/* "$__dotnet_sos"
    echo "Copied SOS to $__dotnet_sos"

    cp "$__BinDir"/* "$__dotnet_dump"
    echo "Copied SOS to $__dotnet_dump"
fi

#
# Run xunit tests
#

if [ $__Test == true ]; then
   if [ $__CrossBuild != true ]; then
      if [ "$LLDB_PATH" == "" ]; then
          export LLDB_PATH="$(which lldb-3.9.1 2> /dev/null)"
          if [ "$LLDB_PATH" == "" ]; then
              export LLDB_PATH="$(which lldb-3.9 2> /dev/null)"
              if [ "$LLDB_PATH" == "" ]; then
                  export LLDB_PATH="$(which lldb-4.0 2> /dev/null)"
                  if [ "$LLDB_PATH" == "" ]; then
                      export LLDB_PATH="$(which lldb-5.0 2> /dev/null)"
                      if [ "$LLDB_PATH" == "" ]; then
                          export LLDB_PATH="$(which lldb 2> /dev/null)"
                      fi
                  fi
              fi
          fi
      fi

      if [ "$GDB_PATH" == "" ]; then
          export GDB_PATH="$(which gdb 2> /dev/null)"
      fi

      echo "lldb: '$LLDB_PATH' gdb: '$GDB_PATH'"

      "$__ProjectRoot/eng/common/build.sh" \
        --test \
        --configuration "$__BuildType" \
        --verbosity "$__Verbosity" \
        /bl:$__LogDir/Test.binlog \
        /p:BuildArch="$__BuildArch" \
        /p:PrivateBuildPath="$__PrivateBuildPath" \
        /p:DotnetRuntimeVersion="$__DotnetRuntimeVersion" \
        /p:DotnetRuntimeDownloadVersion="$__DotnetRuntimeDownloadVersion" \
        /p:RuntimeSourceFeed="$__RuntimeSourceFeed" \
        /p:RuntimeSourceFeedKey="$__RuntimeSourceFeedKey" \
        $__TestArgs

      if [ $? != 0 ]; then
          exit 1
      fi
   fi
fi

echo "BUILD: Repo sucessfully built."
echo "BUILD: Product binaries are available at $__CMakeBinDir"
