# Mirrored cDAC source

The `runtime` directory contains a mirrored copy of the managed cDAC source from
[dotnet/runtime](https://github.com/dotnet/runtime). The copy allows cDAC to
build and ship on the dotnet/diagnostics release cadence instead of waiting for
the corresponding runtime release.

dotnet/runtime remains the source of truth. `runtime-source.txt` records the
exact runtime commit used for the current copy.

## Updating the mirrored source

1. Open the [Sync cDAC and universal DBI source](https://github.com/dotnet/diagnostics/actions/workflows/sync-runtime-source.yml) workflow.
2. Select **Run workflow** and run it from the default branch.
3. Review the pull request created or updated by the workflow.
4. Merge the pull request after its normal validation completes.

The workflow updates cDAC and the universal DBI together from the same runtime
`main` commit so their private DAC/DBI interface stays in sync. It uses runtime's
`src/native/managed/cdac/SourceManifest.props` and
`src/coreclr/dlls/mscordbi_universal/SourceManifest.props` manifests. It maintains
the `automation/sync-runtime-source` branch and reuses its existing pull request
when one is open.
