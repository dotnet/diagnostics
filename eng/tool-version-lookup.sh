#!/usr/bin/env bash
# Correlates diagnostics tool versions with git commits and build dates.
#
# Usage:
#   eng/tool-version-lookup.sh decode 10.0.715501

set -euo pipefail

# Arcade SDK epoch constant from Version.BeforeCommonTargets.targets.
# If Arcade changes this value, it must be updated here as well.
VERSION_BASE_SHORT_DATE=19000

die() { echo "Error: $*" >&2; exit 1; }

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

# --- Argument parsing ---
COMMAND="${1:-}"
REF_ARG="${2:-}"

case "$COMMAND" in
    decode)
        [ -n "$REF_ARG" ] || die "Usage: tool-version-lookup.sh decode <version>"
        cmd_decode "$REF_ARG"
        ;;
    "")
        die "No command specified. Use --help for usage."
        ;;
    *)
        die "Unknown command '$COMMAND'. Use --help for usage."
        ;;
esac
