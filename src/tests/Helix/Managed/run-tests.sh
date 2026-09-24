#!/usr/bin/env bash
set -euo pipefail

: "${HELIX_WORKITEM_UPLOAD_ROOT:?HELIX_WORKITEM_UPLOAD_ROOT is required}"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
work_item=""
test_dll=""
report_name=""
dotnet_root=""
runtime_version=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --helix-work-item|--test-dll|--report-name|--dotnet-root|--runtime-version)
      if [[ $# -lt 2 || -z "$2" ]]; then
        echo "Missing value for $1." >&2
        exit 2
      fi
      case "$1" in
        --helix-work-item) work_item="$2" ;;
        --test-dll) test_dll="$2" ;;
        --report-name) report_name="$2" ;;
        --dotnet-root) dotnet_root="$2" ;;
        --runtime-version) runtime_version="$2" ;;
      esac
      shift 2
      ;;
    --)
      shift
      break
      ;;
    *)
      echo "Unknown launcher argument '$1'. Pass test arguments after --." >&2
      exit 2
      ;;
  esac
done

if [[ -z "$test_dll" && -n "$work_item" ]]; then
  test_dll="$root/tests/$work_item/$work_item.dll"
fi
report_name="${report_name:-$work_item}"
if [[ -z "$test_dll" || -z "$report_name" ]]; then
  echo "Specify --helix-work-item or both --test-dll and --report-name." >&2
  exit 2
fi
if [[ -z "$dotnet_root" ]]; then
  dotnet_root="${HELIX_CORRELATION_PAYLOAD:?HELIX_CORRELATION_PAYLOAD is required}/dotnet-cli"
fi
dotnet="$dotnet_root/dotnet"
log="$HELIX_WORKITEM_UPLOAD_ROOT/$report_name.log"

if [[ ! -x "$dotnet" ]]; then
  echo "The Helix-provisioned dotnet host was not found at '$dotnet'." >&2
  exit 3
fi

if [[ ! -f "$test_dll" ]]; then
  echo "The test assembly was not found at '$test_dll'." >&2
  exit 3
fi

export DOTNET_ROOT="$dotnet_root"
mkdir -p "$HELIX_WORKITEM_UPLOAD_ROOT" || exit 3

dotnet_arguments=()
if [[ -n "$runtime_version" ]]; then
  dotnet_arguments+=(--fx-version "$runtime_version")
fi

set +e
"$dotnet" "${dotnet_arguments[@]}" "$test_dll" \
  --results-directory "$HELIX_WORKITEM_UPLOAD_ROOT" \
  --report-xunit \
  --report-xunit-filename "$report_name.xml" \
  --auto-reporters off "$@" 2>&1 | tee "$log"
exit_codes=("${PIPESTATUS[@]}")
set -e
exit_code="${exit_codes[0]}"
if [[ "$exit_code" -eq 0 ]]; then
  exit_code="${exit_codes[1]}"
fi
exit "$exit_code"
