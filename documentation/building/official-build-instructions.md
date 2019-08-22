Official Build Instructions
===========================

WARNING: These instructions will only work internally at Microsoft.

To kick off an official build, go to this build definition: https://dev.azure.com/dnceng/internal/_build?definitionId=528.

This signs and publishes dotnet-dump, dotnet-sos, dotnet-trace, dotnet-counters and Microsoft.Diagnostic.TestHepers packages to https://dotnetfeed.blob.core.windows.net/dotnet-core feed.

To release the latest tools:

1) Merge all the commits for this release from the master branch to the release/3.0 branch.
2) Kick off an official build from the release/3.0 branch.
3) Change all the package version references in the documentation folder to this official build's package version. Commit/merge to master and release/3.0.
4) Download the above packages from the successful official build under "Artifacts" -> "packages".
5) Upload these packages to nuget.org.
6) Create a new "release" in the [releases](https://github.com/dotnet/diagnostics/releases) diagnostics repo release tab with the package version (not the official build id) as the "tag". Add any release notes about known issues, issues fixed and new features.
