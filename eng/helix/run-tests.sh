#!/usr/bin/env bash

set -u

architecture="$1"
configuration="$2"
target_os="$3"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
artifacts_dir="$repo_root/artifacts"
bootstrap_root="$artifacts_dir/dotnet-bootstrap"
dotnet_root="$artifacts_dir/dotnet-test"
upload_root="${HELIX_WORKITEM_UPLOAD_ROOT:-$artifacts_dir/helix-results}"

export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export NUGET_PACKAGES="$repo_root/.packages"

source "$repo_root/eng/init-debugger-paths.sh"
initialize_debugger_paths

if [[ -z "${PYTHONPATH:-}" ]] && command -v python3 > /dev/null; then
    export PYTHONPATH
    PYTHONPATH="$(command -v python3)"
fi

source "$repo_root/eng/common/native/init-distro-rid.sh"
initDistroRidGlobal "$target_os" "$architecture"
target_rid="$__PortableTargetOS-$architecture"

mkdir -p "$artifacts_dir/log/$configuration" "$upload_root"
ulimit -c 0

bootstrap_sdk_version="$(sed -n 's/^[[:space:]]*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$repo_root/global.json" | head -n 1)"
if [[ -z "$bootstrap_sdk_version" ]]; then
    echo "Unable to determine the bootstrap SDK version from global.json."
    exit 1
fi

if ! bash "$repo_root/eng/dotnet-install.sh" \
    -NoPath \
    -Version "$bootstrap_sdk_version" \
    -InstallDir "$bootstrap_root"; then
    exit 1
fi

if ! "$bootstrap_root/dotnet" msbuild "$repo_root/eng/InstallRuntimes.proj" \
    /restore \
    /t:InstallTestRuntimes \
    /p:Configuration="$configuration" \
    /p:ContinuousIntegrationBuild=true \
    /p:SkipBuildSdkInstall=true \
    /p:TargetArch="$architecture" \
    /p:TargetOS="$target_os" \
    /p:TargetRid="$target_rid" \
    /bl:"$artifacts_dir/log/$configuration/InstallRuntimes.binlog"; then
    exit 1
fi

export DOTNET_ROOT="$dotnet_root"
export DOTNET_HOST_PATH="$dotnet_root/dotnet"
export PATH="$dotnet_root:$PATH"

find "$artifacts_dir/bin" "$dotnet_root" -type f -exec chmod u+x {} +

set +e
"$dotnet_root/dotnet" msbuild "$repo_root/build.proj" \
    /restore \
    /t:Test \
    /p:Configuration="$configuration" \
    /p:ContinuousIntegrationBuild=true \
    /p:SkipTestArtifactsBuild=true \
    /p:TargetArch="$architecture" \
    /p:TargetOS="$target_os" \
    /p:TargetRid="$target_rid" \
    /p:TestArchitectures="$architecture" \
    /bl:"$artifacts_dir/log/$configuration/Test.binlog"
exit_code=$?
set -e

if [[ -d "$artifacts_dir/TestResults" ]]; then
    find "$artifacts_dir/TestResults" -type f -name '*.xml' -print0 |
        while IFS= read -r -d '' result; do
            result_name="$(basename "${result%.xml}")"
            cp "$result" "$upload_root/$result_name.testResults.xml"
        done
    cp -R "$artifacts_dir/TestResults" "$upload_root/"
fi
cp -R "$artifacts_dir/log" "$upload_root/"

if [[ "$exit_code" -ne 0 ]]; then
    diagnostic_paths=()
    for path in "tmp/$configuration/dumps" "tmp/$configuration/streams"; do
        if [[ -d "$artifacts_dir/$path" ]]; then
            diagnostic_paths+=("$path")
        fi
    done
    if [[ "${#diagnostic_paths[@]}" -gt 0 ]]; then
        tar -czf "$upload_root/diagnostics-dumps.tar.gz" -C "$artifacts_dir" "${diagnostic_paths[@]}"
    fi
fi

exit "$exit_code"
