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

# install .NET Core - setting DOTNET_INSTALL_DIR prevents build.sh from installing it
export DOTNET_INSTALL_DIR=$scriptroot/../.dotnet
"$scriptroot/install-dotnet.sh" $DOTNET_INSTALL_DIR 2.1.300-rc1-008673

# build/test managed components
"$scriptroot/common/build.sh" $@

# build/test native components
"$scriptroot/build-native.sh" $@

