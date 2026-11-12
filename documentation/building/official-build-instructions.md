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

## Weekly stable updates

The `Update release/stable` GitHub workflow runs on Monday at 08:00 UTC and supports
manual dispatch. When main has commits missing from release/stable, it creates
`release/weekly-staging` from stable if needed, then merges the captured stable and
main commits into staging. It opens or reuses a same-repository
`release/weekly-staging` -> `release/stable` PR and links it in the run summary.
Merge conflicts fail the workflow rather than resetting or force-pushing staging.

Staging-branch pushes trigger Release dry-run validation in the public Azure
pipeline once its CI branch filter permits `release/weekly-staging` and the dry-run
pipeline changes are merged. An unchanged staging branch does not explicitly queue
another CI build.

The workflow never modifies main or release/stable, merges the stable-update PR,
or publishes release artifacts. It uses GitHub's automatic token with contents-write
and pull-requests-write permissions, not a PAT. The repository must allow GitHub
Actions to create pull requests.
