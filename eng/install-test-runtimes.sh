#!/usr/bin/env bash
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

install_dir=
build_arch=
daily_test=0

while [ $# -ne 0 ]; do
    name=$1
    case $name in
        --install-dir)
            shift
            install_dir=$1
            ;;
        --architecture)
            shift
            build_arch=$1
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

read_dom() {
    local IFS=\>
    read -d \< ENTITY CONTENT
    local ret=$?
    return $ret
}

parse_dom () {
    case $ENTITY in
        RuntimeVersion21)
            runtime_version_21=$CONTENT
            ;;
        AspNetCoreVersion21)
            aspnetcore_version_21=$CONTENT
            ;;
        RuntimeVersion30)
            runtime_version_30=$CONTENT
            ;;
        AspNetCoreVersion30)
            aspnetcore_version_30=$CONTENT
            ;;
        RuntimeVersion31)
            runtime_version_31=$CONTENT
            ;;
        AspNetCoreVersion31)
            aspnetcore_version_31=$CONTENT
            ;;
        RuntimeVersionLatest)
            runtime_version_latest=$CONTENT
            ;;
        AspNetCoreVersionLatest)
            aspnetcore_version_latest=$CONTENT
            ;;
    esac
}

config_file="$install_dir/Debugger.Tests.Versions.txt"

while read_dom; do
    parse_dom
done < $config_file

daily_test_text="true"
if [ $daily_test == 0 ]; then
    daily_test_text="false"
fi

# Install the other versions of .NET Core runtime we are going to test. 2.1.x, 3.0.x, 3.1.x and latest.
bash "$install_dir/dotnet-install.sh" --version "$runtime_version_21" --architecture "$build_arch" --skip-non-versioned-files --runtime dotnet --install-dir "$install_dir"
bash "$install_dir/dotnet-install.sh" --version "$aspnetcore_version_21" --architecture "$build_arch" --skip-non-versioned-files --runtime aspnetcore --install-dir "$install_dir"

bash "$install_dir/dotnet-install.sh" --version "$runtime_version_31" --architecture "$build_arch" --skip-non-versioned-files --runtime dotnet --install-dir "$install_dir"
bash "$install_dir/dotnet-install.sh" --version "$aspnetcore_version_31" --architecture "$build_arch" --skip-non-versioned-files --runtime aspnetcore --install-dir "$install_dir"

bash "$install_dir/dotnet-install.sh" --version "$runtime_version_latest" --architecture "$build_arch" --skip-non-versioned-files --runtime dotnet --install-dir "$install_dir"
bash "$install_dir/dotnet-install.sh" --version "$aspnetcore_version_latest" --architecture "$build_arch" --skip-non-versioned-files --runtime aspnetcore --install-dir "$install_dir"

echo "<Configuration>
  <DailyTest>$daily_test_text</DailyTest>
</Configuration>" > $install_dir/Debugger.Tests.Options.txt
