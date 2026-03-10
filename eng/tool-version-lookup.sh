#!/usr/bin/env bash
# Correlates diagnostics tool versions with git commits and build dates.
#
# Usage:
#   eng/tool-version-lookup.sh decode 10.0.715501
#   eng/tool-version-lookup.sh list [--tool dotnet-trace] [--last 10]

set -euo pipefail

# NuGet V3 flat container API — the simplest endpoint for listing all versions of a package.
FEED_FLAT2_BASE="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-tools/nuget/v3/flat2"
# Full feed URL used in the 'dotnet tool update --add-source' install command printed for users.
FEED_URL="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-tools/nuget/v3/index.json"
# Arcade SDK epoch constant from Version.BeforeCommonTargets.targets.
# If Arcade changes this value, it must be updated here as well.
VERSION_BASE_SHORT_DATE=19000
VALID_TOOLS="dotnet-trace dotnet-dump dotnet-counters dotnet-gcdump"

# Defaults
TOOL="dotnet-trace"
MAJOR_MINOR=""
LAST_COUNT=10

die() { echo "Error: $*" >&2; exit 1; }

validate_tool() {
    local tool="$1"
    for valid in $VALID_TOOLS; do
        if [ "$tool" = "$valid" ]; then return 0; fi
    done
    die "Invalid tool '$tool'. Valid options: $VALID_TOOLS"
}

# The Arcade SDK (Version.BeforeCommonTargets.targets) encodes the OfficialBuildId
# (format: yyyyMMdd.revision) into the patch component of the version number:
#   SHORT_DATE = YY*1000 + MM*50 + DD
#   PATCH = (SHORT_DATE - VersionBaseShortDate) * 100 + revision
# MM*50 is used instead of MM*100 because months max at 12 (12*50=600), leaving
# room for days 1-31 without overflow into the next month's range.
decode_patch() {
    local patch=$1
    PATCH_REV=$((patch % 100))
    local short_date=$((patch / 100 + VERSION_BASE_SHORT_DATE))
    PATCH_YY=$((short_date / 1000))
    local remainder=$((short_date - PATCH_YY * 1000))
    PATCH_MM=$((remainder / 50))
    PATCH_DD=$((remainder - PATCH_MM * 50))
}

encode_patch() {
    local yy=$1 mm=$2 dd=$3 rev=${4:-1}
    local short_date=$((yy * 1000 + mm * 50 + dd))
    echo $(( (short_date - VERSION_BASE_SHORT_DATE) * 100 + rev ))
}

format_build_date() {
    decode_patch "$1"
    printf "20%02d-%02d-%02d (rev %d)" "$PATCH_YY" "$PATCH_MM" "$PATCH_DD" "$PATCH_REV"
}

# Strips the "+commitsha" metadata suffix and splits "Major.Minor.Patch".
parse_version() {
    local ver="${1%%+*}"
    VER_MAJOR="" VER_MINOR="" VER_PATCH=""
    IFS='.' read -r VER_MAJOR VER_MINOR VER_PATCH <<< "$ver"
    if [ -z "$VER_MAJOR" ] || [ -z "$VER_MINOR" ] || [ -z "$VER_PATCH" ]; then
        return 1
    fi
    # Validate they're integers
    case "$VER_MAJOR$VER_MINOR$VER_PATCH" in
        *[!0-9]*) return 1 ;;
    esac
    return 0
}

get_feed_versions() {
    local tool="$1"
    local url="$FEED_FLAT2_BASE/$tool/index.json"
    if command -v jq >/dev/null 2>&1; then
        curl -sfL "$url" | jq -r '.versions[]'
    else
        # Fallback: the flat2 response is {"versions":["x.y.z",...]}, simple enough to parse.
        curl -sfL "$url" | tr ',' '\n' | grep -oE '"[0-9]+\.[0-9]+\.[0-9]+"' | tr -d '"'
    fi
}

# Auto-detects the active major.minor series by finding the version with the
# highest patch number (most recent build), since the feed contains versions
# from multiple release branches (e.g., 6.0.x, 9.0.x, 10.0.x).
detect_major_minor() {
    local best_patch=-1
    local best_prefix=""
    local v
    while IFS= read -r v; do
        if parse_version "$v"; then
            if [ "$VER_PATCH" -gt "$best_patch" ]; then
                best_patch=$VER_PATCH
                best_prefix="$VER_MAJOR.$VER_MINOR"
            fi
        fi
    done
    echo "$best_prefix"
}

resolve_major_minor() {
    if [ -n "$MAJOR_MINOR" ]; then
        echo "$MAJOR_MINOR"
        return
    fi
    local versions="$1"
    echo "$versions" | detect_major_minor
}

cmd_decode() {
    local version="$1"
    parse_version "$version" || die "Could not parse version '$version'."

    decode_patch "$VER_PATCH"
    echo "Version:          $version"
    printf "Build date:       20%02d-%02d-%02d\n" "$PATCH_YY" "$PATCH_MM" "$PATCH_DD"
    echo "Build revision:   $PATCH_REV"
    printf "OfficialBuildId:  20%02d%02d%02d.%d\n" "$PATCH_YY" "$PATCH_MM" "$PATCH_DD" "$PATCH_REV"

    if [[ "$version" == *"+"* ]]; then
        local sha="${version#*+}"
        echo "Commit SHA:       $sha"
    fi
}

cmd_list() {
    local versions
    versions=$(get_feed_versions "$TOOL")
    local prefix
    prefix=$(resolve_major_minor "$versions")

    local filtered
    filtered=$(echo "$versions" | grep "^${prefix}\." | while IFS= read -r v; do
        if parse_version "$v"; then
            echo "$VER_PATCH:$v"
        fi
    done | sort -t: -k1 -n | tail -"$LAST_COUNT")

    echo "Recent $TOOL $prefix.x versions on dotnet-tools feed:"
    echo ""
    printf "%-20s  %-16s  %s\n" "Version" "Build Date" "OfficialBuildId"
    printf "%-20s  %-16s  %s\n" "--------------------" "----------------" "---------------"

    echo "$filtered" | while IFS=: read -r patch ver; do
        decode_patch "$patch"
        printf "%-20s  20%02d-%02d-%02d        20%02d%02d%02d.%d\n" \
            "$ver" "$PATCH_YY" "$PATCH_MM" "$PATCH_DD" \
            "$PATCH_YY" "$PATCH_MM" "$PATCH_DD" "$PATCH_REV"
    done
}

# --- Argument parsing ---
POSITIONAL=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        --tool)       TOOL="$2"; validate_tool "$TOOL"; shift 2 ;;
        --major-minor) MAJOR_MINOR="$2"; shift 2 ;;
        --last)       LAST_COUNT="$2"; shift 2 ;;
        --help|-h)
            echo "Usage: tool-version-lookup.sh <command> [ref] [options]"
            echo ""
            echo "Commands:"
            echo "  decode <version>     Decode a tool version to its build date"
            echo "  list                 List recent versions on the feed"
            echo ""
            echo "Options:"
            echo "  --tool <name>        Tool name (default: dotnet-trace)"
            echo "  --major-minor <M.m>  Filter to specific major.minor (auto-detected)"
            echo "  --last <N>           Number of versions to show in list (default: 10)"
            exit 0
            ;;
        *)            POSITIONAL+=("$1"); shift ;;
    esac
done

COMMAND="${POSITIONAL[0]:-}"
REF_ARG="${POSITIONAL[1]:-}"

case "$COMMAND" in
    decode)
        [ -n "$REF_ARG" ] || die "Usage: tool-version-lookup.sh decode <version>"
        cmd_decode "$REF_ARG"
        ;;
    list)
        cmd_list
        ;;
    "")
        die "No command specified. Use --help for usage."
        ;;
    *)
        die "Unknown command '$COMMAND'. Use --help for usage."
        ;;
esac
