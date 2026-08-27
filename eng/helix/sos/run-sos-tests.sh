#!/usr/bin/env bash

set -euo pipefail

if [[ $# -lt 6 || $# -gt 7 ]]; then
  echo "usage: $0 <configuration> <rid> <shard-index> <shard-count> <Dump|Live> <test-tfm> [max-parallel-threads]" >&2
  exit 2
fi

configuration="$1"
rid="$2"
shard_index="$3"
shard_count="$4"
liveness="$5"
test_tfm="$6"
max_parallel_threads="${7:-}"
test_runtime_major="${SOSHARNESS_TEST_RUNTIME_MAJOR:-}"
liveness_name="$(printf '%s' "$liveness" | tr '[:upper:]' '[:lower:]')"

if [[ -n "$max_parallel_threads" && (! "$max_parallel_threads" =~ ^[1-9][0-9]*$) ]]; then
  echo "max-parallel-threads must be a positive integer; got '$max_parallel_threads'." >&2
  exit 2
fi

if [[ -n "$test_runtime_major" && (! "$test_runtime_major" =~ ^[1-9][0-9]*$) ]]; then
  echo "SOSHARNESS_TEST_RUNTIME_MAJOR must be a positive integer; got '$test_runtime_major'." >&2
  exit 2
fi

: "${HELIX_CORRELATION_PAYLOAD:?HELIX_CORRELATION_PAYLOAD is required}"
: "${HELIX_WORKITEM_UPLOAD_ROOT:?HELIX_WORKITEM_UPLOAD_ROOT is required}"

root="$HELIX_CORRELATION_PAYLOAD"
upload="$HELIX_WORKITEM_UPLOAD_ROOT"
test_dll="$root/artifacts/bin/SOS.Tests/$configuration/$test_tfm/SOS.Tests.dll"
identity="${liveness_name}-${shard_index}-of-${shard_count}"
work="$PWD/.sos-harness"

mkdir -p "$upload" "$work"

if [[ ! -f "$test_dll" ]]; then
  echo "SOS.Tests.dll was not found at '$test_dll'." >&2
  exit 3
fi

mirror_tree()
{
  source_root="$1"
  destination_root="$2"

  rm -rf "$destination_root"
  mkdir -p "$destination_root"

  while IFS= read -r source_dir; do
    relative_dir="${source_dir#"$source_root"}"
    mkdir -p "$destination_root$relative_dir"
  done < <(find "$source_root" -type d)

  while IFS= read -r source_file; do
    relative_file="${source_file#"$source_root"/}"
    ln -s "$source_file" "$destination_root/$relative_file"
  done < <(find "$source_root" ! -type d)
}

prepare_dotnet_root()
{
  source_root="$root/artifacts/dotnet-test"
  needs_overlay=0

  if [[ ! -x "$source_root/dotnet" ]]; then
    needs_overlay=1
  fi

  while IFS= read -r createdump; do
    if [[ ! -x "$createdump" ]]; then
      needs_overlay=1
      break
    fi
  done < <(find "$source_root" -type f -name createdump)

  if [[ "$needs_overlay" == "0" ]]; then
    printf '%s\n' "$source_root"
    return
  fi

  destination_root="$work/dotnet-test"
  echo "Creating writable executable overlay for dotnet-test." >&2
  mirror_tree "$source_root" "$destination_root"

  rm "$destination_root/dotnet"
  cp "$source_root/dotnet" "$destination_root/dotnet"
  chmod +x "$destination_root/dotnet"

  while IFS= read -r createdump; do
    relative_createdump="${createdump#"$source_root"/}"
    rm "$destination_root/$relative_createdump"
    cp "$createdump" "$destination_root/$relative_createdump"
    chmod +x "$destination_root/$relative_createdump"
  done < <(find "$source_root" -type f -name createdump)

  printf '%s\n' "$destination_root"
}

configure_lldb()
{
  if [[ "$(uname -s)" != "Linux" ]]; then
    return
  fi

  if [[ -z "${LLDB_PATH:-}" ]]; then
    for candidate in lldb-16 lldb16 lldb-15 lldb15 lldb-14 lldb14 lldb; do
      if command -v "$candidate" > /dev/null 2>&1; then
        LLDB_PATH="$(command -v "$candidate")"
        break
      fi
    done
  fi

  if [[ -z "${LLDB_PATH:-}" || ! -x "$LLDB_PATH" ]]; then
    echo "Could not locate an executable LLDB. Set LLDB_PATH or install LLDB on the Helix image." >&2
    exit 4
  fi

  lldb_python_module=""
  resolved_lldb="$(readlink -f "$LLDB_PATH" 2>/dev/null || printf '%s' "$LLDB_PATH")"
  lldb_version="${resolved_lldb##*-}"
  for llvm_root in "/usr/lib/llvm-$lldb_version" "/usr/lib/llvm$lldb_version" /usr/lib/llvm-* /usr/lib/llvm*; do
    if [[ ! -d "$llvm_root" ]]; then
      continue
    fi

    lldb_python_module="$(find "$llvm_root" -type f -path '*/lldb/embedded_interpreter.py' -print 2>/dev/null | head -n 1 || true)"
    if [[ -n "$lldb_python_module" ]]; then
      break
    fi
  done

  if [[ -n "$lldb_python_module" ]]; then
    lldb_python_root="$(dirname "$(dirname "$lldb_python_module")")"
    export PYTHONPATH="$lldb_python_root${PYTHONPATH:+:$PYTHONPATH}"
  fi

  lldb_check="$("$LLDB_PATH" --no-lldbinit --batch \
    -o 'script print("__SOSHARNESS_LLDB_READY__")' \
    -o quit 2>&1 || true)"
  if [[ "$lldb_check" != *"__SOSHARNESS_LLDB_READY__"* ]]; then
    echo "LLDB failed its Python interpreter preflight at '$LLDB_PATH'." >&2
    echo "$lldb_check" >&2
    exit 4
  fi

  echo "Using LLDB at '$LLDB_PATH'."
  export LLDB_PATH
}

dotnet_root="$(prepare_dotnet_root)"
dotnet="$dotnet_root/dotnet"
dotnet_arguments=("$test_dll")

if [[ -n "$test_runtime_major" ]]; then
  test_runtime_version="$("$dotnet" --list-runtimes | awk -v prefix="$test_runtime_major." \
    '$1 == "Microsoft.NETCore.App" && index($2, prefix) == 1 { version = $2 } END { print version }')"
  if [[ -z "$test_runtime_version" ]]; then
    echo "Microsoft.NETCore.App $test_runtime_major.x was not found under '$dotnet_root'." >&2
    exit 3
  fi

  echo "Running SOS.Tests on Microsoft.NETCore.App $test_runtime_version."
  dotnet_arguments=(--fx-version "$test_runtime_version" "$test_dll")
fi

if [[ "$(uname -s)" == "Darwin" ]]; then
  entitlements="$root/eng/helix/sos/debuggee-entitlements.plist"
  for debuggee in NestedExceptionTest DivZero AsyncMain DynamicMethod Overflow LineNums SimpleThrow ReflectionTest SosHarnessScenarios; do
    find "$root/artifacts/bin/$debuggee/$configuration" -type f -name "$debuggee" \
      -exec codesign --force --sign - --entitlements "$entitlements" {} \;
  done
  export SOSHARNESS_EXCLUDE_SINGLEFILE_SNAPSHOTS=1
fi

if [[ "$rid" == linux-musl-* ]]; then
  target_arch="${rid##*-}"
  native_source="$root/artifacts/bin/linux.$target_arch.$configuration"
  native_overlay="$work/native"
  mirror_tree "$native_source" "$native_overlay"
  if [[ -e "$native_source/libmscordaccore_universal.so" ]]; then
    rm "$native_overlay/libmscordaccore_universal.so"
    cp "$native_source/libmscordaccore_universal.so" "$native_overlay/libmscordaccore_universal.so"
  fi
  export SOSHARNESS_NATIVE_ROOT="$native_overlay"
fi

configure_lldb

export DOTNET_ROOT="$dotnet_root"
export DOTNET_ROOT_X64="$DOTNET_ROOT"
export DOTNET_MULTILEVEL_LOOKUP=0
export NUGET_PACKAGES="$root/.packages"
export SOSHARNESS_REPO_ROOT="$root"
export SOSHARNESS_DOTNET_ROOT="$DOTNET_ROOT"
export SOSHARNESS_DOTNET_TEST_ROOT="$DOTNET_ROOT"
export SOSHARNESS_EXECUTABLE_ROOT="$work/executables"
export SOSHARNESS_SCRATCH_ROOT="$work/scratch"
export SOSHARNESS_ARTIFACTS_CONFIG="$configuration"
export SOSHARNESS_USE_PREBUILT_TARGETS=1
export SOSHARNESS_SHARD_INDEX="$shard_index"
export SOSHARNESS_SHARD_COUNT="$shard_count"
export SOSHARNESS_ONLY_LIVENESS="$liveness"
export SOSHARNESS_LLDB_TRACE="$upload/SOS.Tests-${rid}-${configuration}-${identity}.lldb.log"

log="$upload/SOS.Tests-${rid}-${configuration}-${identity}.log"
run_tests()
{
  "$dotnet" "${dotnet_arguments[@]}" "$@" \
    --results-directory "$upload" \
    --report-xunit \
    --report-xunit-filename "SOS.Tests-${rid}-${configuration}-${identity}.xml" \
    --report-xunit-html \
    --report-xunit-html-filename "SOS.Tests-${rid}-${configuration}-${identity}.html" \
    --report-trx \
    --report-trx-filename "SOS.Tests-${rid}-${configuration}-${identity}.trx" \
    --auto-reporters off
}

set +e
if [[ -n "$max_parallel_threads" ]]; then
  run_tests --max-threads "$max_parallel_threads" 2>&1 | tee "$log"
  exit_code=${PIPESTATUS[0]}
else
  run_tests 2>&1 | tee "$log"
  exit_code=${PIPESTATUS[0]}
fi
set -e

exit "$exit_code"
