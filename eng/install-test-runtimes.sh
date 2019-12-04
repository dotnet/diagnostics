#!/usr/bin/env bash
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

dotnet_dir=
temp_dir=
build_arch=
daily_test=0
branch="master"
uncached_feed="https://dotnetcli.blob.core.windows.net/dotnet"

# args:
# channel - $1
# runtime - $2 (dotnet, aspnetcore, "")
# coherent - $3
get_latest_version_info() {
    eval $invocation

    local channel="$1"
    local runtime="$2"
    local coherent="$3"

    local version_file="$temp_dir/latest.version"
    local version_file_url=null
    local version_latest=""

    if [[ "$runtime" == "dotnet" ]]; then
        version_file_url="$uncached_feed/Runtime/$channel/latest.version"
    elif [[ "$runtime" == "aspnetcore" ]]; then
        version_file_url="$uncached_feed/aspnetcore/Runtime/$channel/latest.version"
    elif [ -z "$runtime" ]; then
        if [ "$coherent" = true ]; then
            version_file_url="$uncached_feed/Sdk/$channel/latest.coherent.version"
        else
            version_file_url="$uncached_feed/Sdk/$channel/latest.version"
        fi
    else
        echo "Invalid value for $runtime"
        return 1
    fi

    # Use curl if available, otherwise use wget
    if command -v curl > /dev/null; then
        curl "$version_file_url" -sSL --retry 10 --create-dirs -o "$version_file"
    else
        wget -q -O "$version_file" "$version_file_url"
    fi

    if [ -f "$version_file" ]; then
        version_latest="$(cat $version_file | tail -n 1 | tr -d "\r")"
    else
        echo "Could not download latest runtime version file"
        return 1
    fi

    echo "$version_latest"
    return 0
}

while [ $# -ne 0 ]; do
    name=$1
    case $name in
        --dotnet-directory)
            shift
            dotnet_dir=$1
            ;;
        --temp-directory)
            shift
            temp_dir=$1
            ;;
        --architecture)
            shift
            build_arch=$1
            ;;
        --branch)
            shift
            branch=$1
            ;;
        --daily-test)
            daily_test=1
            ;;
        *)
            args="$args $1"
            ;;
    esac
    shift
done

runtime_version_21="2.1.14"
aspnetcore_version_21="2.1.14"
runtime_version_30="3.0.0"
aspnetcore_version_30="3.0.0"
runtime_version_31="3.1.0"
aspnetcore_version_31="3.1.0"
runtime_version_latest=""
aspnetcore_version_latest=""

config_file="$dotnet_dir/Debugger.Tests.Versions.txt"

daily_test_text="true"
if [ $daily_test == 0 ]; then
    daily_test_text="false"
fi

runtime_version_latest="$(get_latest_version_info $branch dotnet false)"
aspnetcore_version_latest="$(get_latest_version_info $branch aspnetcore false)"
echo "Latest $branch runtime: $runtime_version_latest aspnetcore: $aspnetcore_version_latest"

# Install the other versions of .NET Core runtime we are going to test. 2.1.x, 3.0.x, 3.1.x and latest.
bash "$dotnet_dir/dotnet-install.sh" --version "$runtime_version_21" --architecture "$build_arch" --skip-non-versioned-files --runtime dotnet --install-dir "$dotnet_dir"
bash "$dotnet_dir/dotnet-install.sh" --version "$aspnetcore_version_21" --architecture "$build_arch" --skip-non-versioned-files --runtime aspnetcore --install-dir "$dotnet_dir"

bash "$dotnet_dir/dotnet-install.sh" --version "$runtime_version_31" --architecture "$build_arch" --skip-non-versioned-files --runtime dotnet --install-dir "$dotnet_dir"
bash "$dotnet_dir/dotnet-install.sh" --version "$aspnetcore_version_31" --architecture "$build_arch" --skip-non-versioned-files --runtime aspnetcore --install-dir "$dotnet_dir"

bash "$dotnet_dir/dotnet-install.sh" --version "$runtime_version_latest" --architecture "$build_arch" --skip-non-versioned-files --runtime dotnet --install-dir "$dotnet_dir"
bash "$dotnet_dir/dotnet-install.sh" --version "$aspnetcore_version_latest" --architecture "$build_arch" --skip-non-versioned-files --runtime aspnetcore --install-dir "$dotnet_dir"

echo "<Configuration>
  <DailyTest>$daily_test_text</DailyTest>
  <RuntimeVersion21>$runtime_version_21</RuntimeVersion21>
  <AspNetCoreVersion21>$aspnetcore_version_21</AspNetCoreVersion21>
  <RuntimeVersion30>$runtime_version_30</RuntimeVersion30>
  <AspNetCoreVersion30>$aspnetcore_version_30</AspNetCoreVersion30>
  <RuntimeVersion31>$runtime_version_31</RuntimeVersion31>
  <AspNetCoreVersion31>$aspnetcore_version_31</AspNetCoreVersion31>
  <RuntimeVersionLatest>$runtime_version_latest</RuntimeVersionLatest>
  <AspNetCoreVersionLatest>$aspnetcore_version_latest</AspNetCoreVersionLatest>
</Configuration>" > $config_file
