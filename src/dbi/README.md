# Mirrored universal DBI source

The source-sync workflow populates the `runtime` directory with the runtime
source needed to build `mscordbi_universal`. Build integration will be added
separately after the first mirrored-source update.

dotnet/runtime remains the source of truth. `runtime-source.txt` records the
exact runtime commit used for the current copy.

## Updating the mirrored source

1. Open the **Sync universal DBI source** workflow.
2. Run it from the default branch.
3. Review the pull request created or updated by the workflow.
4. Merge the pull request after its normal validation completes.

The workflow uses runtime's
`src/coreclr/dlls/mscordbi_universal/SourceManifest.props` manifest.
