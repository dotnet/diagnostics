#!/usr/bin/env bash

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
export LLDB_PATH=/usr/bin/lldb
export SOS_TEST_CDAC=true
$scriptroot/../.dotnet/dotnet test --no-build --logger "console;verbosity=detailed" $scriptroot/../src/SOS/SOS.UnitTests/SOS.UnitTests.csproj --filter "Category=CDACCompatible"
