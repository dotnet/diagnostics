#!/usr/bin/env bash

build_universal_dbi()
{
    local target_os="$1"
    local target_arch="$2"
    local config_triplet="$3"
    local artifacts_intermediates_dir="$4"
    local output_dir="$5"
    local extra_cmake_args="$6"
    local log_file="$7"
    local dbi_intermediates_dir="$artifacts_intermediates_dir/dbi/$config_triplet"
    local dbi_cmake_args="$extra_cmake_args -DCLR_CMAKE_TARGET_ARCH=$target_arch -DRUNTIME_GENERATED_INCLUDE_DIR=$artifacts_intermediates_dir"

    mkdir -p "$dbi_intermediates_dir"
    mkdir -p "$output_dir"

    build_native "$target_os" "$target_arch" "$__RepoRootDir/src/dbi" "$dbi_intermediates_dir" "mscordbi_universal" "$dbi_cmake_args" "universal DBI" | tee "$log_file"
    if [[ "${PIPESTATUS[0]}" != 0 ]]; then
        echo "Universal DBI build FAILED"
        return 1
    fi
    if [[ "$__ConfigureOnly" == 1 ]]; then
        return 0
    fi

    local dbi_name
    if [[ "$target_os" == "osx" ]]; then
        dbi_name="libmscordbi_universal.dylib"
    else
        dbi_name="libmscordbi_universal.so"
    fi

    local dbi_output_dir="$dbi_intermediates_dir/dlls/mscordbi_universal"
    if [[ ! -f "$dbi_output_dir/$dbi_name" ]]; then
        echo "Source-built universal DBI was not produced in '$dbi_output_dir'."
        return 1
    fi

    cp -f "$dbi_output_dir/$dbi_name" "$output_dir/$dbi_name"
    if [[ -f "$dbi_output_dir/$dbi_name.dbg" ]]; then
        cp -f "$dbi_output_dir/$dbi_name.dbg" "$output_dir/$dbi_name.dbg"
    fi
}
