# Mirrored cDAC source

The `runtime` directory is a partial source snapshot of
[dotnet/runtime](https://github.com/dotnet/runtime), not a submodule. It contains
the managed reader, contracts, legacy interop layer, NativeAOT entry point,
source generator and explicitly linked sources. Runtime's tests, usage-analysis
tooling, contract documentation and native universal DBI source are not included.

`update-source.ps1` contains the explicit source paths, exclusions and temporary
transition patch used to produce the snapshot.

`runtime-source.txt` is generated provenance containing only the immutable
upstream commit. There is no separate inventory or snapshot hash. Diagnostics
owns the two ancestor `Directory.Build.*` adapter files in `src/cdac`.

## Updating

From the diagnostics root:

```powershell
.\src\cdac\update-source.ps1 -RuntimeRoot Q:\source\runtime
```

The script archives the runtime checkout's committed `HEAD`; uncommitted changes
are neither modified nor imported. Pass `-Commit <revision>` to reproduce a
different commit already available in that checkout. The generated metadata
records the resolved immutable commit.

The `runtime` directory is generated content and is replaced on each update.
Make intentional downstream changes in dotnet/runtime rather than editing the
snapshot. The script temporarily applies the corresponding runtime fix so it can
be tested before the mirrored runtime commit contains it. The replacement must
match exactly once; remove it when the source contains the fix.

## Building and validating

The default diagnostics build continues consuming the packaged reader. Opt in
to the source-built reader with `-p:BuildCDacFromSource=true`:

```powershell
.\.dotnet\dotnet.exe build src\cdac\dirs.proj -c Debug -p:BuildCDacFromSource=true
```

Bootstrap the repository's existing .NET SDK first if `.dotnet` is absent.
The adapters use the diagnostics SDK and latest C# language version; they do not upgrade the repository
toolchain or publish new managed packages.

`dirs.proj` builds and publishes the reader. All build outputs use the normal
diagnostics artifact directories.

## GitHub automation

`.github/workflows/sync-cdac.yml` runs daily at 08:00 UTC and can also be
manually dispatched. It uses `dotnet/actions-create-pull-request` with the
destination repository's `GITHUB_TOKEN`; no cross-repository write credential is
needed. The action stages only the generated snapshot and provenance file, then
creates or updates the PR. The resulting PR is validated by the repository's
normal CI.

The action maintains the fixed `automation/sync-cdac` branch using
force-with-lease. Existing PRs and review threads are retained, but update commits
may be replaced when the snapshot changes. There is no auto-merge.

The repository must allow Actions to create PRs. Token-created PRs can require
maintainer approval before GitHub Actions checks run; Azure Pipelines triggering
must also be verified during onboarding.
