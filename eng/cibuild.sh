#!/usr/bin/env bash
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

source="${BASH_SOURCE[0]}"

# resolve $SOURCE until the file is no longer a symlink
while [[ -h $source ]]; do
  scriptroot="$( cd -P "$( dirname "$source" )" && pwd )"
  source="$(readlink "$source")"

  # if $source was a relative symlink, we need to resolve it relative to the path where 
  # the symlink file was located
  [[ $source != /* ]] && source="$scriptroot/$source"
done

scriptroot="$( cd -P "$( dirname "$source" )" && pwd )"

# Fix any CI lab docker image problems

__osname=$(uname -s)
if [ "$__osname" == "Linux" ]; then
    if [ -e /etc/os-release ]; then
        source /etc/os-release
        if [[ $ID == "ubuntu" ]]; then
            if [[ $VERSION_ID == "18.04" ]]; then
                # Fix the CI lab's ubuntu 18.04 docker image: install curl.
                sudo apt-get update
                sudo apt-get install -y curl
            fi
        fi
    elif [ -e /etc/redhat-release ]; then
        __redhatRelease=$(</etc/redhat-release)
        if [[ $__redhatRelease == "CentOS release 6."* || $__redhatRelease == "Red Hat Enterprise Linux Server release 6."* ]]; then
            source scl_source enable python27 devtoolset-2
        fi
    fi
fi

"$scriptroot/build.sh" --restore --ci --stripsymbols $@
if [[ $? != 0 ]]; then
    exit 1
fi
