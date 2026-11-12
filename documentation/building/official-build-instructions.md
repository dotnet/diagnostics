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
`isOfficialBuild` parameter is set explicitly by the shared pipeline template
`eng/pipelines/diagnostics.yml`
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

The `Update release/stable` GitHub workflow runs Monday at 08:00 UTC and supports
manual dispatch. It creates `automation/release-staging` from stable if needed,
merges captured stable and main commits into it, and opens or reuses its PR to
`release/stable`. Conflicts fail rather than resetting or force-pushing staging.
It never merges the PR or modifies main or stable. It uses GitHub's automatic
token, not a PAT; the repository must allow GitHub Actions to create pull requests.

The dedicated `diagnostics-public-release-dry-run` Azure definition uses
`diagnostics-release-dry-run.yml` to validate all PRs targeting `release/stable`,
including the bot's `automation/release-staging` PR. That branch is based on stable
and merges main, retaining the release branch's version settings. The dry-run
pipeline validates the proposed merge: it builds Release for all 11 platforms,
runs the installed SOS tests on the eight supported test platforms, and packages
the complete tool set.

Packaging retains `OfficialBuild=true` for official versioning but passes
`ForceDryRunSigning=true`. The pipeline also exports that signing safeguard to
build jobs. It does not initialize MicroBuild signing for packaging, register assets
with BAR, promote builds with Darc, or run release-storage preparation. Its outputs
are downloadable build artifacts only.

Both definitions use the shared `eng/pipelines/diagnostics.yml` template. The
normal `diagnostics.yml` entry point keeps all PRs Debug-only; non-PR builds use
Release only. The dedicated entry point passes `isReleaseDryRun: true`, so its
configuration, job IDs and stage selection are all resolved at compile time.
Neither pipeline needs runtime PR branch inspection or a staging push trigger.
PRs targeting stable receive both the normal Debug check and the Release dry-run
check, regardless of their source branch.
