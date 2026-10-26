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

1. Open the **Sync universal DBI source** workflow.
2. Run it from the default branch.
3. Review the pull request created or updated by the workflow.
4. Merge the pull request after its normal validation completes.

The workflow uses runtime's
`src/coreclr/dlls/mscordbi_universal/SourceManifest.props` manifest.
