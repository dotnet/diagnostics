#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 6 ]]; then
  echo "usage: $0 <configuration> <rid> <shard-index> <shard-count> <Dump|Live> <test-tfm>" >&2
  exit 2
fi

configuration="$1"
rid="$2"
shard_index="$3"
shard_count="$4"
liveness="$5"
test_tfm="$6"

: "${HELIX_CORRELATION_PAYLOAD:?HELIX_CORRELATION_PAYLOAD is required}"
: "${HELIX_WORKITEM_UPLOAD_ROOT:?HELIX_WORKITEM_UPLOAD_ROOT is required}"

root="$HELIX_CORRELATION_PAYLOAD"
upload="$HELIX_WORKITEM_UPLOAD_ROOT"
dotnet="$root/artifacts/dotnet-test/dotnet"
test_dll="$root/artifacts/bin/SOS.Tests/$configuration/$test_tfm/SOS.Tests.dll"
identity="${liveness,,}-${shard_index}-of-${shard_count}"

mkdir -p "$upload"

if [[ ! -f "$test_dll" ]]; then
  echo "SOS.Tests.dll was not found at '$test_dll'." >&2
  exit 3
fi

chmod +x "$dotnet"
find "$root/artifacts/dotnet-test" -type f -name createdump -exec chmod +x {} +
for debuggee in NestedExceptionTest DivZero AsyncMain DynamicMethod Overflow LineNums SimpleThrow ReflectionTest SosHarnessScenarios; do
  find "$root/artifacts/bin/$debuggee/$configuration" -type f -name "$debuggee" -exec chmod +x {} +
done

export DOTNET_ROOT="$root/artifacts/dotnet-test"
export DOTNET_ROOT_X64="$DOTNET_ROOT"
export DOTNET_MULTILEVEL_LOOKUP=0
export NUGET_PACKAGES="$root/.packages"
export SOSHARNESS_REPO_ROOT="$root"
export SOSHARNESS_DOTNET_ROOT="$DOTNET_ROOT"
export SOSHARNESS_ARTIFACTS_CONFIG="$configuration"
export SOSHARNESS_USE_PREBUILT_TARGETS=1
export SOSHARNESS_SHARD_INDEX="$shard_index"
export SOSHARNESS_SHARD_COUNT="$shard_count"
export SOSHARNESS_ONLY_LIVENESS="$liveness"
export SOSHARNESS_LLDB_TRACE="$upload/SOS.Tests-${rid}-${configuration}-${identity}.lldb.log"

log="$upload/SOS.Tests-${rid}-${configuration}-${identity}.log"
set +e
"$dotnet" "$test_dll" \
  --results-directory "$upload" \
  --report-xunit-xml \
  --report-xunit-xml-filename "SOS.Tests-${rid}-${configuration}-${identity}.xml" \
  --report-xunit-html \
  --report-xunit-html-filename "SOS.Tests-${rid}-${configuration}-${identity}.html" \
  --report-trx \
  --report-trx-filename "SOS.Tests-${rid}-${configuration}-${identity}.trx" \
  --auto-reporters off 2>&1 | tee "$log"
exit_code=${PIPESTATUS[0]}
set -e

exit "$exit_code"
