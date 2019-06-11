#!/usr/bin/env bash
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

# args:
# input - $1
function ToLowerCase() {
    echo "$1" | tr '[:upper:]' '[:lower:]'
    return 0
}

buildargs=
buildnativeargs=$@

# Parse command line options
while :; do
    if [ $# -le 0 ]; then
        break
    fi
    lowerI="$(ToLowerCase "$1")"
    case $lowerI in
        --architecture)
            shift
            ;;
        --rootfs)
            shift
            ;;
        --build-native|--test|--daily-test)
            ;;
        --clang3.5|--clang3.6|--clang3.7|--clang3.8|--clang3.9|--clang4.0|--clang5.0)
            ;;
        *)
            buildargs="$buildargs $1"
            ;;
    esac

    shift
done

# build managed components
"$scriptroot/common/build.sh" $buildargs
if [[ $? != 0 ]]; then
    exit 1
fi

# build native components and test both
"$scriptroot/build-native.sh" $buildnativeargs
if [[ $? != 0 ]]; then
    exit 1
fi
