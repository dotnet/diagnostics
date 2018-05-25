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

# ReadJson [filename] [json key]
# Result: Sets 'readjsonvalue' to the value of the provided json key
# Note: this method may return unexpected results if there are duplicate
# keys in the json
function ReadJson {
  local file=$1
  local key=$2

  local unamestr="$(uname)"
  local sedextended='-r'
  if [[ "$unamestr" == 'Darwin' ]]; then
    sedextended='-E'
  fi;

  readjsonvalue="$(grep -m 1 "\"$key\"" $file | sed $sedextended 's/^ *//;s/.*: *"//;s/",?//')"
  if [[ ! "$readjsonvalue" ]]; then
    echo "Error: Cannot find \"$key\" in $file" >&2;
    ExitWithExitCode 1
  fi;
}

# install .NET Core
ReadJson "$scriptroot/../global.json" "version"

# setting DOTNET_INSTALL_DIR prevents build.sh from installing it
export DOTNET_INSTALL_DIR=$scriptroot/../.dotnet
"$scriptroot/install-dotnet.sh" $DOTNET_INSTALL_DIR $readjsonvalue
if [[ $? != 0 ]]; then
    exit 1
fi

# build/test managed components
"$scriptroot/common/build.sh" $@
if [[ $? != 0 ]]; then
    exit 1
fi

# build/test native components
"$scriptroot/build-native.sh" $@

