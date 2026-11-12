Official Build Instructions
===========================

WARNING: These instructions will only work internally at Microsoft.

To kick off an official build, go to this build definition: https://dev.azure.com/dnceng/internal/_build?definitionId=528.

This signs and publishes the following packages to the tools feed (https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-tools/nuget/v3/index.json):
 - dotnet-dump
 - dotnet-gcdump
 - dotnet-sos
 - dotnet-trace
 - dotnet-counters
 - dotnet-monitor
 - Microsoft.Diagnostics.NETCore.Client
 - Microsoft.Diagnostics.DbgShim

To release the latest tools:

1) Merge all the commits for this release from the main branch to the release/stable branch.
2) Kick off an official build from the release/stable branch.
3) Change all the package version references in the documentation folder to this official build's package version to maintain the docs up to date.
4) Download the above packages from the successful official build under "Artifacts" -> "PackageArtifacts".
5) Upload these packages to nuget.org.
6) Create a new "release" in the [releases](https://github.com/dotnet/diagnostics/releases) diagnostics repo release tab with the package version (not the official build id) as the "tag". Add any release notes about known issues, issues fixed and new features.

## PR packaging

Public PR builds package unsigned Debug artifacts for all 11 build platforms. The
packaging job depends only on the native and managed build jobs, so it runs alongside
the test jobs. The three ARM Linux cross-builds are enabled for all builds to
supply all packaging inputs. Windows x64 build artifacts include the complete
managed output tree for Debug as well as Release.

Both PR and official builds use `eng/pipelines/package.yml` for packaging. Its
`isOfficialBuild` parameter is set explicitly by the callers in `diagnostics.yml`
and also controls internal NuGet source setup. Both modes run Arcade's
`-sign -publish` to process signing, upload build artifacts and generate asset
manifests. PRs explicitly pass
`OfficialBuild=false`, which makes signing a dry run. The downstream BAR
registration and Darc jobs, not the packaging template, control publishing to feeds.
PR builds include the job in the build stage; official builds include it in the existing
package stage. Both call `eng/ci-prepare-artifacts.cmd` to create tool NuGets, bundled
tools, DbgShim packages and InternalAssets packages. PRs select Debug, explicitly
disable official-build behavior and skip real signing and feed publishing. Arcade uploads
their packages as `PackageArtifacts`, along with blob/symbol assets and
`AssetManifests`. Bundles are also uploaded as `BundledTools`.
Official builds retain Release packaging, real signing and release publishing.

Both modes declare additional bundle and log artifacts through `templateContext.outputs`.
`eng/pipelines/templatePublic.yml` translates `pipelineArtifact` outputs into
public upload tasks after the job's steps, preserving upload conditions and error
handling. Internal pipelines leave output processing, including SBOM generation,
to 1ES. The public adapter supports `path`/`artifact` and
`targetPath`/`artifactName`, but does not generate SBOMs or support other output types.

## Weekly release rehearsal

PRs from `automation/release-staging` to `release/stable` select release dry-run mode
using runtime PR metadata. This branch is based on `release/stable` and merges
main, retaining the release branch's version settings. The PR pipeline validates
the proposed merge: it builds Release for all 11 platforms, runs the installed SOS
tests on the eight supported test platforms, and packages the complete tool set.

Packaging retains `OfficialBuild=true` for official versioning but passes
`ForceDryRunSigning=true`. The pipeline also exports that signing safeguard to
build jobs. It does not initialize MicroBuild signing for packaging, register assets
with BAR, promote builds with Darc, or run release-storage preparation. Its outputs
are downloadable build artifacts only.

The existing PR trigger is sufficient; push CI remains disabled. One build matrix
selects Debug for ordinary PRs and Release for weekly PRs and non-PR builds.
PR pipelines include both configurations at template expansion time; job conditions
run only the selected configuration, and dependent tests and packaging skip the
unused configuration. Weekly PRs run the installed SOS tests instead of ordinary
Debug tests and skip release preparation.
