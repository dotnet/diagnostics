#!/usr/bin/env bash
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

dotnet_dir=
temp_dir=
build_arch=
daily_test=0
branch="master"
uncached_feed="https://dotnetcli.blob.core.windows.net/dotnet"

runtime_version_11="1.1.11"
runtime_version_21="2.1.10"
runtime_version_22="2.2.4"

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

daily_test_text="true"

# Install the other versions of .NET Core runtime we are going to test. 1.1.x, 2.1.x (installed with the CLI), 2.2.x
# and latest. Only install the latest master for daily jobs and leave the RuntimeVersion* config properties blank.
if [ $daily_test == 0 ]; then
    daily_test_text="false"
    bash "$dotnet_dir/dotnet-install.sh" --version "$runtime_version_11" --architecture "$build_arch" --skip-non-versioned-files --runtime dotnet --install-dir "$dotnet_dir"
    bash "$dotnet_dir/dotnet-install.sh" --version "$runtime_version_21" --architecture "$build_arch" --skip-non-versioned-files --runtime dotnet --install-dir "$dotnet_dir"
    bash "$dotnet_dir/dotnet-install.sh" --version "$runtime_version_22" --architecture "$build_arch" --skip-non-versioned-files --runtime dotnet --install-dir "$dotnet_dir"
fi

bash "$dotnet_dir/dotnet-install.sh" --channel $branch --version latest --architecture "$build_arch" --skip-non-versioned-files --runtime dotnet --install-dir "$dotnet_dir"

# Now download the latest runtime version and create a config file containing it
version_file_url="$uncached_feed/Runtime/$branch/latest.version"
version_file="$temp_dir/latest.version"
config_file="$dotnet_dir/Debugger.Tests.Versions.txt"

# Use curl if available, otherwise use wget
if command -v curl > /dev/null; then
    curl "$version_file_url" -sSL --retry 10 --create-dirs -o "$version_file"
else
    wget -q -O "$version_file" "$version_file_url"
fi

if [ -f "$version_file" ]; then
    runtime_version_latest=$(cat $version_file | tail -n 1 | sed 's/\r$//')

    echo "Latest version: $runtime_version_latest"

    echo "<Configuration>
<DailyTest>$daily_test_text</DailyTest>
<RuntimeVersion11>$runtime_version_11</RuntimeVersion11>
<RuntimeVersion21>$runtime_version_21</RuntimeVersion21>
<RuntimeVersion22>$runtime_version_22</RuntimeVersion22>
<RuntimeVersionLatest>$runtime_version_latest</RuntimeVersionLatest>
</Configuration>" > $config_file

else
    echo "Could not download latest runtime version file"
fi
