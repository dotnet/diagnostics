#!/usr/bin/env bash
# Licensed to the .NET Foundation under one or more agreements.
# The .NET Foundation licenses this file to you under the MIT license.

# Send diagnostics tests to Helix for remote execution.
#
# Usage: send-to-helix.sh [options]
#
# Options:
#   -configuration <Debug|Release>   Build configuration (default: Debug)
#   -architecture <x64|arm64>        Target architecture (default: x64)
#   -os <linux|osx>                  Target OS (default: auto-detected)
#   -queue <queue-name>              Helix queue to target (default: auto-selected based on OS)
#   -accesstoken <token>             Helix access token for internal queues (optional)
#   -creator <name>                  Creator name for public builds (optional)
#   -bl                              Generate binary log
#   -help                            Show this help message

set -e

# Get script directory
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

# Default values
configuration="Debug"
architecture="x64"
target_os=""
helix_queue=""
helix_access_token=""
creator=""
binary_log=""

# Detect OS
detect_os() {
    case "$(uname -s)" in
        Linux*)  echo "linux" ;;
        Darwin*) echo "osx" ;;
        *)       echo "unknown" ;;
    esac
}

# Get default Helix queue based on OS and architecture
get_default_queue() {
    local os=$1
    local arch=$2
    
    case "$os" in
        linux)
            case "$arch" in
                x64)   echo "Ubuntu.2204.Amd64.Open" ;;
                arm64) echo "Ubuntu.2204.Arm64.Open" ;;
                *)     echo "Ubuntu.2204.Amd64.Open" ;;
            esac
            ;;
        osx)
            case "$arch" in
                x64)   echo "OSX.1200.Amd64.Open" ;;
                arm64) echo "OSX.1200.Arm64.Open" ;;
                *)     echo "OSX.1200.Amd64.Open" ;;
            esac
            ;;
        *)
            echo "Ubuntu.2204.Amd64.Open"
            ;;
    esac
}

show_help() {
    cat << EOF

Send diagnostics tests to Helix for remote execution.

Usage: send-to-helix.sh [options]

Options:
  -configuration <Debug|Release>   Build configuration (default: Debug)
  -architecture <x64|arm64>        Target architecture (default: x64)
  -os <linux|osx>                  Target OS (default: auto-detected)
  -queue <queue-name>              Helix queue to target (default: auto-selected based on OS)
  -accesstoken <token>             Helix access token for internal queues
  -creator <name>                  Creator name for public builds
  -bl                              Generate binary log
  -help                            Show this help message

Examples:
  ./send-to-helix.sh
  ./send-to-helix.sh -configuration Release -queue Ubuntu.2404.Amd64.Open
  ./send-to-helix.sh -architecture arm64 -os linux
  ./send-to-helix.sh -creator "MyName" -bl

Notes:
  - Run build.sh first to build the test artifacts
  - For internal queues (not ending in .Open), provide -accesstoken
  - Public queues require -creator to be set

Available public Linux queues:
  Ubuntu.2204.Amd64.Open
  Ubuntu.2404.Amd64.Open
  Ubuntu.2204.Arm64.Open
  Debian.12.Amd64.Open

Available public macOS queues:
  OSX.1200.Amd64.Open
  OSX.1200.Arm64.Open
  OSX.1300.Amd64.Open

EOF
    exit 0
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    opt="$(echo "${1}" | tr '[:upper:]' '[:lower:]')"
    case "$opt" in
        -configuration|-c)
            configuration="$2"
            shift 2
            ;;
        -architecture|-a)
            architecture="$2"
            shift 2
            ;;
        -os)
            target_os="$2"
            shift 2
            ;;
        -queue)
            helix_queue="$2"
            shift 2
            ;;
        -accesstoken)
            helix_access_token="$2"
            shift 2
            ;;
        -creator)
            creator="$2"
            shift 2
            ;;
        -bl)
            binary_log="/bl:$repo_root/artifacts/log/$configuration/SendToHelix.binlog"
            shift
            ;;
        -help|-h|--help)
            show_help
            ;;
        *)
            echo "Unknown argument: $1"
            show_help
            ;;
    esac
done

# Auto-detect OS if not specified
if [[ -z "$target_os" ]]; then
    target_os=$(detect_os)
fi

# Auto-select queue if not specified
if [[ -z "$helix_queue" ]]; then
    helix_queue=$(get_default_queue "$target_os" "$architecture")
fi

# Set required environment variables for local Helix execution
export BUILD_SOURCEBRANCH="${BUILD_SOURCEBRANCH:-local}"
export BUILD_REPOSITORY_NAME="${BUILD_REPOSITORY_NAME:-diagnostics}"
export SYSTEM_TEAMPROJECT="${SYSTEM_TEAMPROJECT:-dnceng}"
export BUILD_REASON="${BUILD_REASON:-Manual}"

# Build optional arguments
creator_arg=""
if [[ -n "$creator" ]]; then
    creator_arg="/p:Creator=$creator"
fi

access_token_arg=""
if [[ -n "$helix_access_token" ]]; then
    access_token_arg="/p:HelixAccessToken=$helix_access_token"
fi

echo ""
echo "==========================="
echo "Sending tests to Helix"
echo "==========================="
echo "Configuration:    $configuration"
echo "Architecture:     $architecture"
echo "Target OS:        $target_os"
echo "Helix Queue:      $helix_queue"
echo "Artifacts Dir:    $repo_root/artifacts/"
echo ""

# Verify artifacts exist
if [[ ! -d "$repo_root/artifacts/bin" ]]; then
    echo "ERROR: Build artifacts not found at $repo_root/artifacts/bin"
    echo "Please run build.sh first to build the tests."
    exit 1
fi

# Send to Helix (use -tl:off to disable terminal logger for better output)
"$repo_root/eng/common/msbuild.sh" \
    "$repo_root/eng/helix.proj" \
    /restore \
    /t:Test \
    -tl:off \
    /p:Configuration="$configuration" \
    /p:TargetArchitecture="$architecture" \
    /p:TargetOS="$target_os" \
    /p:HelixTargetQueues="$helix_queue" \
    /p:TestArtifactsDir="$repo_root/artifacts/" \
    /p:EnableAzurePipelinesReporter=false \
    $access_token_arg \
    $creator_arg \
    $binary_log

echo ""
echo "Tests submitted to Helix successfully!"
echo "View results at: https://helix.dot.net/"
