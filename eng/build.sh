#!/usr/bin/env bash
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

# Obtain the location of the bash script to figure out where the root of the repo is.
__RepoRootDir="$(cd "$(dirname "$0")"/..; pwd -P)"

__BuildType=Debug
__CMakeArgs=
__CommonMSBuildArgs=
__Compiler=clang
__CompilerMajorVersion=
__CompilerMinorVersion=
__CrossBuild=0
__DotnetRuntimeDownloadVersion="default"
__DotnetRuntimeVersion="default"
__ExtraCmakeArgs=
__IsMSBuildOnNETCoreSupported=0
__ManagedBuild=1
__ManagedBuildArgs=
__NativeBuild=1
__NumProc=1
__PortableBuild=1
__RootBinDir="$__RepoRootDir"/artifacts
__RuntimeSourceFeed=
__RuntimeSourceFeedKey=
__SkipConfigure=0
__SkipGenerateVersion=0
__InstallRuntimes=0
__PrivateBuild=0
__Test=0
__UnprocessedBuildArgs=

usage_list+=("-skipmanaged: do not build managed components.")
usage_list+=("-skipnative: do not build native components.")
usage_list+=("-test: run xunit tests")

handle_arguments() {

    lowerI="$(echo "${1/--/-}" | tr "[:upper:]" "[:lower:]")"
    case "$lowerI" in
        architecture|-architecture|-a)
            __TargetArch="$(echo "$2" | tr "[:upper:]" "[:lower:]")"
            __ShiftArgs=1
            ;;

        -binarylog|-bl|-clean|-integrationtest|-pack|-performancetest|-pipelineslog|-pl|-preparemachine|-publish|-r|-rebuild|-build|-restore|-sign|-sb)
            __ManagedBuildArgs="$__ManagedBuildArgs $1"
            ;;

        configuration|-configuration|-c)
            _type="$(echo "$2" | tr "[:upper:]" "[:lower:]")"
            if [[ "$_type" == "release" ]]; then
                __BuildType=Release
            elif [[ "$_type" = "checked" ]]; then
                __BuildType=Checked
            fi

            __ShiftArgs=1
            ;;

        -dotnetruntimeversion)
            __DotnetRuntimeVersion="$2"
            __ShiftArgs=1
            ;;

        -dotnetruntimedownloadversion)
            __DotnetRuntimeDownloadVersion="$2"
            __ShiftArgs=1
            ;;

        -runtimesourcefeed)
            __RuntimeSourceFeed="$2"
            __ShiftArgs=1
            ;;

        -runtimesourcefeedkey)
            __RuntimeSourceFeedKey="$2"
            __ShiftArgs=1
             ;;

        skipmanaged|-skipmanaged)
            __ManagedBuild=0
            ;;

        skipnative|-skipnative)
            __NativeBuild=0
            ;;

        installruntimes|-installruntimes)
            __InstallRuntimes=1
            ;;

        privatebuild|-privatebuild)
            __PrivateBuild=1
            ;;

        test|-test)
            __Test=1
            ;;

        -warnaserror|-nodereuse)
            __ManagedBuildArgs="$__ManagedBuildArgs $1 $2"
            __ShiftArgs=1
            ;;

        *)
            __UnprocessedBuildArgs="$__UnprocessedBuildArgs $1"
            ;;
    esac
}

source "$__RepoRootDir"/eng/native/build-commons.sh

__LogsDir="$__RootBinDir/log/$__BuildType"
__ConfigTriplet="$__TargetOS.$__TargetArch.$__BuildType"
__BinDir="$__RootBinDir/bin/$__ConfigTriplet"
__ArtifactsIntermediatesDir="$__RootBinDir/obj"
__IntermediatesDir="$__ArtifactsIntermediatesDir/$__ConfigTriplet"

# Specify path to be set for CMAKE_INSTALL_PREFIX.
# This is where all built libraries will copied to.
__CMakeBinDir="$__BinDir"
export __CMakeBinDir

mkdir -p "$__IntermediatesDir"
mkdir -p "$__LogsDir"
mkdir -p "$__CMakeBinDir"

__ExtraCmakeArgs="$__CMakeArgs $__ExtraCmakeArgs -DCLR_MANAGED_BINARY_DIR=$__RootBinDir/bin -DCLR_BUILD_TYPE=$__BuildType"

# Specify path to be set for CMAKE_INSTALL_PREFIX.
# This is where all built native libraries will copied to.
export __CMakeBinDir="$__BinDir"


if [[ "$__TargetArch" == "armel" ]]; then
    # Armel cross build is Tizen specific and does not support Portable RID build
    __PortableBuild=0
fi

#
# Managed build
#

if [[ "$__ManagedBuild" == 1 ]]; then

    echo "Commencing managed build for $__BuildType in $__RootBinDir/bin"
    "$__RepoRootDir/eng/common/build.sh" --configuration "$__BuildType" $__CommonMSBuildArgs $__ManagedBuildArgs $__UnprocessedBuildArgs

    if [ "$?" != 0 ]; then
        exit 1
    fi

    echo "Generating Version Source File"
    __GenerateVersionLog="$__LogsDir/GenerateVersion.binlog"

    "$__RepoRootDir/eng/common/msbuild.sh" \
        $__RepoRootDir/eng/native-prereqs.proj \
        /bl:$__GenerateVersionLog \
        /t:BuildPrereqs \
        /restore \
        /p:Configuration="$__BuildType" \
        /p:Platform="$__TargetArch" \
        $__UnprocessedBuildArgs

    if [ $? != 0 ]; then
        echo "Generating Version Source File FAILED"
        exit 1
    fi
fi

#
# Setup LLDB paths for native build
#

if [ "$__HostOS" == "osx" ]; then
    export LLDB_H="$__RepoRootDir"/src/SOS/lldbplugin/swift-4.0
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

    if [[ "$__TargetArch" == x64 ]]; then
        __ExtraCmakeArgs="-DCMAKE_OSX_ARCHITECTURES=\"x86_64\" $__ExtraCmakeArgs"
    elif [[ "$__TargetArch" == arm64 ]]; then
        __ExtraCmakeArgs="-DCMAKE_OSX_ARCHITECTURES=\"arm64\" $__ExtraCmakeArgs"
    else
        echo "Error: Unknown OSX architecture $__TargetArch."
        exit 1
    fi
fi

#
# Build native components
#
if [[ "$__NativeBuild" == 1 ]]; then
    build_native "$__TargetOS" "$__TargetArch" "$__RepoRootDir" "$__IntermediatesDir" "install" "$__ExtraCmakeArgs" "diagnostic component" | tee "$__LogsDir"/make.log

    if [ "$?" != 0 ]; then
        echo "Native build FAILED"
        exit 1
    fi
fi

#
# Copy the native SOS binaries to where these tools expect for testing
#

if [[ "$__NativeBuild" == 1 || "$__Test" == 1 ]]; then
    __targetRid=net8.0
    __dotnet_sos=$__RootBinDir/bin/dotnet-sos/$__BuildType/$__targetRid/publish/$__OutputRid
    __dotnet_dump=$__RootBinDir/bin/dotnet-dump/$__BuildType/$__targetRid/publish/$__OutputRid

    mkdir -p "$__dotnet_sos"
    mkdir -p "$__dotnet_dump"

    cp "$__BinDir"/* "$__dotnet_sos"
    echo "Copied SOS to $__dotnet_sos"

    cp "$__BinDir"/* "$__dotnet_dump"
    echo "Copied SOS to $__dotnet_dump"
fi

#
# Install test runtimes and set up for private runtime build
#

if [[ "$__InstallRuntimes" == 1 || "$__PrivateBuild" == 1 ]]; then
    __privateBuildTesting=false
    if [[ "$__PrivateBuild" == 1 ]]; then
        __privateBuildTesting=true
    fi
    rm -fr "$__RepoRootDir/.dotnet-test" || true
    "$__RepoRootDir/eng/common/msbuild.sh" \
        $__RepoRootDir/eng/InstallRuntimes.proj \
        /t:InstallTestRuntimes \
        /bl:"$__LogsDir/InstallRuntimes.binlog" \
        /p:PrivateBuildTesting="$__privateBuildTesting" \
        /p:BuildArch="$__TargetArch" \
        /p:TestArchitectures="$__TargetArch"
fi

#
# Run xunit tests
#

if [[ "$__Test" == 1 ]]; then
   if [[ "$__CrossBuild" == 0 ]]; then
      if [[ -z "$LLDB_PATH" ]]; then
        check_version_exists() {
          desired_version=-1

          # Set up the environment to be used for building with the desired debugger.
          if command -v "lldb-$1.$2" > /dev/null; then
            desired_version="-$1.$2"
          elif command -v "lldb$1$2" > /dev/null; then
            desired_version="$1$2"
          elif command -v "lldb-$1$2" > /dev/null; then
            desired_version="-$1$2"
          fi

          echo "$desired_version"
        }

        # note: clang versions higher than 6 do not have minor version in file name, if it is zero.
        versions="16 15 14 13 12 11 10 9 8 7 6.0 5.0 4.0 3.9"
        for version in $versions; do
          _major="${version%%.*}"
          [ -z "${version##*.*}" ] && _minor="${version#*.}"
          desired_version="$(check_version_exists "$_major" "$_minor")"
          if [ "$desired_version" != "-1" ]; then majorVersion="$_major"; break; fi
        done

        if [ -z "$majorVersion" ]; then
          export LLDB_PATH="$(command -v "lldb")"
        else
          export LLDB_PATH="$(command -v "lldb$desired_version")"
        fi
      fi

      if [[ -z "$GDB_PATH" ]]; then
          export GDB_PATH="$(which gdb 2> /dev/null)"
      fi

      echo "lldb: '$LLDB_PATH' gdb: '$GDB_PATH'"

      "$__RepoRootDir/eng/common/build.sh" \
        --test \
        --configuration "$__BuildType" \
        /bl:"$__LogsDir"/Test.binlog \
        /p:BuildArch="$__TargetArch" \
        /p:DotnetRuntimeVersion="$__DotnetRuntimeVersion" \
        /p:DotnetRuntimeDownloadVersion="$__DotnetRuntimeDownloadVersion" \
        /p:RuntimeSourceFeed="$__RuntimeSourceFeed" \
        /p:RuntimeSourceFeedKey="$__RuntimeSourceFeedKey" \
        $__CommonMSBuildArgs

      if [ $? != 0 ]; then
          exit 1
      fi
   fi
fi

echo "BUILD: Repo sucessfully built."
echo "BUILD: Product binaries are available at $__CMakeBinDir"
