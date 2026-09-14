#!/usr/bin/env bash

set -euo pipefail

# Disable system core dumps for test debuggees that intentionally crash.
# The .NET createdump facility writes dumps directly and is not affected by ulimit.
ulimit -c 0

: "${HELIX_WORKITEM_UPLOAD_ROOT:?HELIX_WORKITEM_UPLOAD_ROOT is required}"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
upload="$HELIX_WORKITEM_UPLOAD_ROOT"
identity="all"

mkdir -p "$upload"

rid="$(sed -n '1p' "$root/.sos-test-payload")"
configuration="$(sed -n '2p' "$root/.sos-test-payload")"
extra_metadata="$(sed -n '3p' "$root/.sos-test-payload")"
if [[ -z "$rid" || -z "$configuration" || -n "$extra_metadata" ]]; then
  echo "The payload marker must contain the RID and configuration." >&2
  exit 3
fi

test_dlls=("$root/artifacts/bin/SOS.Tests/$configuration/"*/SOS.Tests.dll)
if [[ ${#test_dlls[@]} -ne 1 || ! -f "${test_dlls[0]}" ]]; then
  echo "Expected exactly one staged SOS.Tests.dll for $configuration." >&2
  exit 3
fi
test_dll="${test_dlls[0]}"

max_parallel_threads=""
if [[ "$(uname -s)" == "Darwin" ]]; then
  max_parallel_threads=2
elif [[ "$rid" == linux-musl-* || "$rid" == linux-arm64 ]]; then
  max_parallel_threads=1
fi

prepare_dotnet_root()
{
  dotnet_root="${HELIX_CORRELATION_PAYLOAD:?HELIX_CORRELATION_PAYLOAD is required}/dotnet-cli"
  if [[ ! -x "$dotnet_root/dotnet" ]]; then
    echo "The Helix-provisioned dotnet host was not found at '$dotnet_root/dotnet'." >&2
    exit 3
  fi
}

configure_lldb()
{
  if [[ "$(uname -s)" == "Darwin" ]]; then
    driver="$root/debugger/sos-lldb"
    if [[ ! -f "$driver" ]]; then
      echo "The SOS LLDB driver was not found at '$driver'." >&2
      exit 4
    fi

    chmod +x "$driver"

    developer_dir="${DEVELOPER_DIR:-$(xcode-select -p)}"
    shared_frameworks="$(cd "$developer_dir/../SharedFrameworks" && pwd)"
    if [[ ! -d "$shared_frameworks/LLDB.framework" ]]; then
      echo "LLDB.framework was not found under the selected Xcode at '$shared_frameworks'." >&2
      exit 4
    fi
    export DYLD_FRAMEWORK_PATH="$shared_frameworks${DYLD_FRAMEWORK_PATH:+:$DYLD_FRAMEWORK_PATH}"

    lldb_check="$("$driver" --no-lldbinit --batch \
      -o 'script print("__SOSHARNESS_LLDB_READY__")' 2>&1 || true)"
    if [[ "$lldb_check" != *"__SOSHARNESS_LLDB_READY__"* ]]; then
      echo "The SOS LLDB driver failed its Python interpreter preflight at '$driver'." >&2
      echo "$lldb_check" >&2
      exit 4
    fi
    echo "Using SOS LLDB driver at '$driver'."
    return
  fi

  if [[ "$(uname -s)" != "Linux" ]]; then
    return
  fi

  if [[ -z "${LLDB_PATH:-}" ]]; then
    for candidate in lldb-16 lldb16 lldb-15 lldb15 lldb-14 lldb14 lldb-13 lldb13 lldb-12 lldb12 lldb; do
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

  mkdir -p "$root/debugger"
  ln -sf "$resolved_lldb" "$root/debugger/lldb"
  LLDB_PATH="$root/debugger/lldb"

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

prepare_dotnet_root
dotnet="$dotnet_root/dotnet"
dotnet_arguments=("$test_dll")

if [[ "$(uname -s)" == "Darwin" ]]; then
  test_runtime_version="$("$dotnet" --list-runtimes | awk \
    '$1 == "Microsoft.NETCore.App" && index($2, "11.") == 1 { version = $2 } END { print version }')"
  if [[ -z "$test_runtime_version" ]]; then
    echo "Microsoft.NETCore.App 11.x was not found under '$dotnet_root'." >&2
    exit 3
  fi

  echo "Running SOS.Tests on Microsoft.NETCore.App $test_runtime_version."
  dotnet_arguments=(--fx-version "$test_runtime_version" "$test_dll")
fi

if [[ "$(uname -s)" == "Darwin" ]]; then
  entitlements="$root/eng/helix/sos/debuggee-entitlements.plist"
  while IFS= read -r publish_directory; do
    debuggee="$(basename "$(dirname "$(dirname "$(dirname "$(dirname "$publish_directory")")")")")"
    while IFS= read -r source_executable; do
      chmod +x "$source_executable"
      codesign --force --sign - --entitlements "$entitlements" "$source_executable"
    done < <(find "$root/artifacts/bin/$debuggee/$configuration" -type f -name "$debuggee")
  done < <(find "$root/artifacts/bin" -type d -path "*/$configuration/*/$rid/publish")
fi

if [[ "$rid" == linux-musl-* ]]; then
  target_arch="${rid##*-}"
  native_root="$root/artifacts/bin/linux.$target_arch.$configuration"
  if [[ -e "$native_root/libmscordaccore_universal.so" ]]; then
    chmod u+w "$native_root/libmscordaccore_universal.so"
  fi
fi

configure_lldb

export DOTNET_ROOT="$dotnet_root"
export DOTNET_ROOT_X64="$DOTNET_ROOT"
export DOTNET_MULTILEVEL_LOOKUP=0
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
