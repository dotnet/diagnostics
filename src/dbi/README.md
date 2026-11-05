# Mirrored universal DBI source

The source-sync workflow populates the `runtime` directory with the runtime
source needed to build `mscordbi_universal`.

dotnet/runtime remains the source of truth. `runtime-source.txt` records the
exact runtime commit used for the current copy.

The diagnostics native build invokes `build.cmd` or `build.sh`, which owns the
standalone DBI configuration, build, output validation, and installation. The
separate CMake build allows the universal DBI to retain its private runtime PAL.
The resulting DBI and symbols are copied into the normal diagnostics native
output directory. The cDAC transport package is not used.

## Updating the mirrored source

1. Open the [Sync cDAC and universal DBI source](https://github.com/dotnet/diagnostics/actions/workflows/sync-runtime-source.yml) workflow.
2. Run it from the default branch.
3. Review the pull request created or updated by the workflow.
4. Merge the pull request after its normal validation completes.

The workflow updates cDAC and the universal DBI together from the same runtime
`main` commit so their private DAC/DBI interface stays in sync. It uses runtime's
`src/native/managed/cdac/SourceManifest.props` and
`src/coreclr/dlls/mscordbi_universal/SourceManifest.props` manifests. It maintains
the `automation/sync-runtime-source` branch and reuses its existing pull request
when one is open.
