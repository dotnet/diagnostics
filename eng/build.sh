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

# remove the --test option and pass it to build-native.sh
__args="$(echo $@ | sed 's/--test//g;s/--clang[0-9]\.[0-9]//g')"

# build managed components
"$scriptroot/common/build.sh" $__args
if [[ $? != 0 ]]; then
    exit 1
fi

# build native components and test both
"$scriptroot/build-native.sh" $@
