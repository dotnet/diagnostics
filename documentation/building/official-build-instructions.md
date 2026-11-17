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

Weekly stable updates
---------------------

The `Update release/stable` GitHub workflow runs every Friday at 20:00 UTC and
supports manual dispatch. When main has commits missing from release/stable, it
creates `automation/release-sync` from release/stable if needed, then merges the
latest release/stable and main commits into that branch. It opens or updates an
`automation/release-sync` -> `release/stable` pull request for validation.

`global.json` and `eng/Versions.props` come from main. The automation restores
the darc-managed `eng/Version.Details.props` and `eng/Version.Details.xml` from
the captured release/stable commit before pushing, because main's subscriptions
flow builds (such as .NET 12 previews) that release/stable must not take. Those
files are updated on release/stable through Maestro subscriptions that target
release/stable.

The workflow does not modify main or release/stable directly, merge the pull
request, or publish release artifacts. Merge conflicts fail the workflow instead
of resetting or force-pushing the automation branch.
