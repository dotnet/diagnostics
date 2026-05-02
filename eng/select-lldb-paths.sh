#!/usr/bin/env bash
# Selects a consistent LLDB headers/library/binary triple for the SOS plugin
# build and tests.
#
# The SOS plugin must be linked against the same lldb it will be loaded into
# at test time (LLDB_LIB at build → LLDB_PATH at test). This helper makes that
# choice deterministically across build.sh, testsos.sh, and testsoscdac.sh.
#
# Selection:
#   * macOS: prefers Homebrew LLVM lldb (/opt/homebrew/opt/llvm on arm64,
#     /usr/local/opt/llvm on x64) when present. Apple's bundled lldb crashes
#     when running SOS extension commands on arm64, and the plugin built
#     against Apple's LLDB.framework fails to load into Homebrew's lldb;
#     using the Homebrew toolchain end-to-end avoids both issues.
#     Falls back to Xcode's bundled lldb if Homebrew LLVM is not installed.
#   * Linux: leaves LLDB_H/LLDB_LIB to CMake auto-discovery; defaults
#     LLDB_PATH to /usr/bin/lldb (matches the prior testsos.sh hardcode).
#
# Honors any caller-provided LLDB_H / LLDB_LIB / LLDB_PATH — if set, it is
# left alone, so a developer can override without editing this script.
#
# Argument:
#   $1 - repository root (used for the swift-4.0 vendored header fallback
#        when falling back to Xcode's lldb on macOS).

select_lldb_paths() {
    local repo_root="${1:-}"
    local uname_s uname_m
    uname_s=$(uname -s)
    uname_m=$(uname -m)

    if [[ "$uname_s" == "Darwin" ]]; then
        local prefix=""
        local p
        for p in /opt/homebrew/opt/llvm /usr/local/opt/llvm; do
            if [[ -x "$p/bin/lldb" && -d "$p/include/lldb" && -f "$p/lib/liblldb.dylib" ]]; then
                prefix="$p"
                break
            fi
        done

        if [[ -n "$prefix" ]]; then
            export LLDB_H="${LLDB_H:-$prefix/include}"
            export LLDB_LIB="${LLDB_LIB:-$prefix/lib/liblldb.dylib}"
            export LLDB_PATH="${LLDB_PATH:-$prefix/bin/lldb}"
        else
            local xcode
            xcode=$(xcode-select -p 2>/dev/null)
            if [[ -n "$repo_root" ]]; then
                export LLDB_H="${LLDB_H:-$repo_root/src/SOS/lldbplugin/swift-4.0}"
            fi
            export LLDB_LIB="${LLDB_LIB:-$xcode/../SharedFrameworks/LLDB.framework/LLDB}"
            export LLDB_PATH="${LLDB_PATH:-$xcode/usr/bin/lldb}"
        fi
    else
        # Linux: build.sh has its own version-probing logic for LLDB_PATH;
        # this default is for the test scripts that previously hardcoded it.
        export LLDB_PATH="${LLDB_PATH:-/usr/bin/lldb}"
    fi
}
