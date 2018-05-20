#!/usr/bin/env bash
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

# The "https://dot.net/v1/dotnet-install.sh" script doesn't work on Fedora or OpenSuse

source="${BASH_SOURCE[0]}"

# resolve $SOURCE until the file is no longer a symlink
while [[ -h $source ]]; do
  scriptroot="$( cd -P "$( dirname "$source" )" && pwd )"
  source="$(readlink "$source")"

  # if $source was a relative symlink, we need to resolve it relative to the path where the
  # symlink file was located
  [[ $source != /* ]] && source="$scriptroot/$source"
done

scriptroot="$( cd -P "$( dirname "$source" )" && pwd )"

dotnet_root=$1
dotnet_sdk_version=$2

if [ ! -e "$dotnet_root" ]; then
    if [ "$(uname -m | grep "i[3456]86")" = "i686" ]; then
	echo "Warning: build not supported on 32 bit Unix"
    fi

    pkg_arch=x64
    OSName=$(uname -s)
    case $OSName in
	Darwin)
	    OS=OSX
	    pkg_rid=osx
	    ulimit -n 2048
	    # Format x.y.z as single integer with three digits for each part
	    VERSION=`sw_vers -productVersion| sed -e 's/\./ /g' | xargs printf "%03d%03d%03d"`
	    if [ "$VERSION" -lt 010012000 ]; then
		echo error: macOS version `sw_vers -productVersion` is too old. 10.12 is needed as minimum.
		exit 1
	    fi
	    ;;

	Linux)
	    pkg_rid=linux
	    OS=Linux

	    if [ -e /etc/os-release ]; then
		source /etc/os-release
		if [[ $ID == "alpine" ]]; then
		    pkg_rid=linux-musl
		fi
	    elif [ -e /etc/redhat-release ]; then
		redhatRelease=$(</etc/redhat-release)
		if [[ $redhatRelease == "CentOS release 6."* || $redhatRelease == "Red Hat Enterprise Linux Server release 6."* ]]; then
		    pkg_rid=rhel.6
		fi
	    fi

	    ;;

	*)
	echo "Unsupported OS '$OSName' detected. Downloading linux-$pkg_arch tools."
	    OS=Linux
	    pkg_rid=linux
	    ;;
    esac

    dotnet_pkg=dotnet-sdk-${dotnet_sdk_version}-$pkg_rid-$pkg_arch

    mkdir -p "$dotnet_root"

    echo "Installing dotnet cli..."
    dotnet_location="https://dotnetcli.azureedge.net/dotnet/Sdk/${dotnet_sdk_version}/${dotnet_pkg}.tar.gz"

    echo "Installing '${dotnet_location}' to '$dotnet_root/dotnet.tar'"
    rm -rf -- "$dotnet_root/*"
    # curl has HTTPS CA trust-issues less often than wget, so lets try that first.
    if command -v curl > /dev/null; then
	curl --retry 10 -sSL --create-dirs -o $dotnet_root/dotnet.tar ${dotnet_location}
    else
	wget -q -O $dotnet_root/dotnet.tar ${dotnet_location}
    fi

    cd "$dotnet_root"
    tar -xf "$dotnet_root/dotnet.tar"
fi

echo "Adding to current process PATH: \`$dotnet_root\`. Note: This change will be visible only when sourcing script."
export PATH="$dotnet_root":"$PATH"

