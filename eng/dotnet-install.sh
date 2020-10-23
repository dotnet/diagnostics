#!/usr/bin/env bash

source="${BASH_SOURCE[0]}"
# resolve $source until the file is no longer a symlink
while [[ -h "$source" ]]; do
  scriptroot="$( cd -P "$( dirname "$source" )" && pwd )"
  source="$(readlink "$source")"
  # if $source was a relative symlink, we need to resolve it relative to the path where the
  # symlink file was located
  [[ $source != /* ]] && source="$scriptroot/$source"
done
scriptroot="$( cd -P "$( dirname "$source" )" && pwd )"

. "$scriptroot/common/tools.sh"

install_dir="<auto>"
architecture="<auto>"
version="Latest"
runtime=""
runtimeSourceFeed=""
runtimeSourceFeedKey=""
skip_non_versioned_files=false

while [[ $# > 0 ]]; do
  opt="$(echo "$1" | awk '{print tolower($0)}')"
  case "$opt" in
    -i|--install-dir|-[Ii]nstall[Dd]ir)
      shift
      install_dir="$1"
      ;;
    -v|--version|-[Vv]ersion)
      shift
      version="$1"
      ;;
    --arch|--architecture|-[Aa]rch|-[Aa]rchitecture)
      shift
      architecture="$1"
      ;;
    --runtime|-[Rr]untime)
      shift
      runtime="$1"
      ;;
    -runtimesourcefeed)
      shift
      runtimeSourceFeed="$1"
      ;;
    -runtimesourcefeedkey)
      shift
      runtimeSourceFeedKey="$1"
      ;;
    --skip-non-versioned-files|-[Ss]kip[Nn]on[Vv]ersioned[Ff]iles)
      skip_non_versioned_files=true
      ;;
    --no-path|-[Nn]o[Pp]ath)
      ;;
    *)
      Write-PipelineTelemetryError -Category 'Build' -Message "Invalid argument: $1"
      exit 1
      ;;
  esac
  shift
done

if [[ "$runtime" != "" ]]; then
  InstallDotNet "$install_dir" "$version" "$architecture" $runtime $skip_non_versioned_files $runtimeSourceFeed $runtimeSourceFeedKey
else
  InstallDotNetSdk "$install_dir" "$version" "$architecture"
fi

if [[ $exit_code != 0 ]]; then
  Write-PipelineTelemetryError -Category 'InitializeToolset' -Message "dotnet-install.sh failed (exit code '$exit_code')." >&2
  ExitWithExitCode $exit_code
fi

ExitWithExitCode 0
