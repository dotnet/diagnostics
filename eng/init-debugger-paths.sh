#!/usr/bin/env bash
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

initialize_debugger_paths()
{
    local _major
    local _minor=
    local desired_version
    local majorVersion=
    local version
    local versions

    if [[ -z "${LLDB_PATH:-}" ]]; then
        check_version_exists()
        {
            desired_version=-1

            if command -v "lldb-$1.$2" > /dev/null; then
                desired_version="-$1.$2"
            elif command -v "lldb$1$2" > /dev/null; then
                desired_version="$1$2"
            elif command -v "lldb-$1$2" > /dev/null; then
                desired_version="-$1$2"
            fi

            echo "$desired_version"
        }

        versions="16 15 14 13 12 11 10 9 8 7 6.0 5.0 4.0 3.9"
        for version in $versions; do
            _major="${version%%.*}"
            [ -z "${version##*.*}" ] && _minor="${version#*.}"
            desired_version="$(check_version_exists "$_major" "$_minor")"
            if [ "$desired_version" != "-1" ]; then majorVersion="$_major"; break; fi
        done

        if [ -z "$majorVersion" ]; then
            export LLDB_PATH="$(command -v "lldb")"
        else
            export LLDB_PATH="$(command -v "lldb$desired_version")"
        fi
    fi

    if [[ -z "${GDB_PATH:-}" ]]; then
        export GDB_PATH="$(which gdb 2> /dev/null)"
    fi
}
