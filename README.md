.NET Core Diagnostics Repo
==========================

This repository contains the source code for various .NET Core runtime diagnostic tools. It currently contains SOS, the managed portion of SOS, the lldb SOS plugin and various global diagnostic tools. The goals of this repo is to build SOS and the lldb SOS plugin for the portable (glibc based) Linux platform (Centos 7) and the platforms not supported by the portable (musl based) build (Centos 6, Alpine, and macOS) and to test across various indexes in a very large matrix: OSs/distros (Centos 6/7, Ubuntu, Alpine, Fedora, Debian, RHEL 7.2), architectures (x64, x86, arm, arm64), lldb versions (3.9 to 9.0) and .NET Core (all in-development and supported major versions).

Another goal to make it easier to obtain a version of lldb (currently 3.9) with scripts and documentation for platforms/distros like Centos, Alpine, Fedora, etc. that by default provide really old versions.

This repo will also allow out of band development of new SOS and lldb plugin features like symbol server support for the .NET Core runtime and solve the source build problem having SOS.NETCore (managed portion of SOS) in the runtime repo.

See the [GitHub Release tab](https://github.com/dotnet/diagnostics/releases) for notes on SOS and diagnostic tools releases.

--------------------------
## Building the Repository

The build depends on Git, CMake, Python and of course a C++ compiler.  Once these prerequisites are installed
the build is simply a matter of invoking the 'build' script (`build.cmd` or `build.sh`) at the base of the
repository.

The details of installing the components differ depending on the operating system.  See the following
pages based on your OS.  There is no cross-building across OS (only for ARM, which is built on x64).
You have to be on the particular platform to build that platform.

To install the platform's prerequisites and build:

 * [Windows Instructions](documentation/building/windows-instructions.md)
 * [Linux Instructions](documentation/building/linux-instructions.md)
 * [MacOS Instructions](documentation/building/osx-instructions.md)
 * [FreeBSD Instructions](documentation/building/freebsd-instructions.md)
 * [NetBSD Instructions](documentation/building/netbsd-instructions.md)
 * [Testing on private runtime builds](documentation/privatebuildtesting.md)

## Test execution

Projects providing a `CreateHelixPayload` target mark themselves for exclusion
from legacy CI runs in their `.csproj`:

```xml
<IsHelixTestProject>true</IsHelixTestProject>
```

The flag defaults to `false`. Normal CI test jobs pass `/p:SkipHelixTests=true`
to exclude Helix projects, regardless of their payload selection. Local test runs
do not set this flag and remain enabled. The flag controls only legacy CI exclusion.
`src/tests/dirs.proj` returns candidate paths from its existing project catalog,
without evaluating child properties. The sender skips projects without a
`CreateHelixPayload` target; there is no separate Helix project list.

Each project controls whether to contribute work items through its payload-target
import or target condition. Unselected projects contribute no items and do not stage files.
The four managed-only suites require only `TargetRid=linux-x64`, regardless of
queue name or configuration. They share the existing Linux x64 Helix jobs with
SOS in both Debug and Release instead of running in a separate managed-only job.
SOS retains its full matrix.

`src/tests/Directory.Build.targets` applies the legacy-CI skip flag.
Every project migrating to Helix sets `IsHelixTestProject=true` and defines or imports
a `CreateHelixPayload` target, which stages prebuilt artifacts and returns ready-to-run
`HelixWorkItem` items. The four managed-only projects explicitly import
`src/tests/Helix/Managed/HelixPayload.targets` when `TargetRid` is `linux-x64`.
This helper owns their common staging, validation, and work-item metadata.
It is not imported globally. SOS keeps its separate implementation in
`SOS.Tests/HelixPayload.targets`.

The managed projects explicitly pass `TestHostRuntimeVersion` to their launcher. This property
defaults to the .NET 10 servicing pin `MicrosoftNETCoreApp100Version` to match
`NetCoreAppTestTargetFramework`. This host version is independent of the SOS
debuggee runtime matrix, including private-build and internal servicing modes.
The helper includes this version in each work item's `RequiredRuntimeVersions`
metadata so the sender provisions it.

Projects specify native SDK metadata such as `Command`, `Timeout`, `PreCommands`,
and `PostCommands` on their returned items. They must provide `PayloadDirectory`
or `PayloadArchive`; the sender does not supply a payload or fill missing metadata.
Payloads, commands, and timeouts are passed through unchanged. Work-item identities
must be unique across the selected projects in a job.
`RequiredRuntimeVersions` metadata lists all runtime versions a work item needs,
separated by semicolons. It can be omitted when no additional runtimes are needed.
The sender deduplicates these versions into native `AdditionalDotNetPackage` items,
without distinguishing test-host and debuggee runtimes. Installing a runtime does not create
work items or change which tests execute. SDK-wide configuration such as package
feeds and correlation payloads belongs in the sender, not in test-project getters.
Use absolute paths for SDK payload files/directories (for example, paths under
`$(ArtifactsDir)`), since the SDK consumes these items in a separate project.

The managed helper stages each project's assembly output under `tests/<assembly>`
and copies its launcher to `runtests.sh`, then returns a directory for the SDK to
archive. It specifies the launcher arguments and a 30-minute timeout; the sender
adds no arguments.

`eng/helix/SendToHelix.proj` is a dedicated `Microsoft.DotNet.Helix.Sdk` project.
It gets candidate projects, calls `CreateHelixPayload` with
`SkipNonexistentTargets=true` in parallel, and submits their combined work items
as one job per queue. The SDK owns queue
fanout, runtime provisioning, reporting, and waiting. Each queue stages into its
own payload directory, preventing concurrent queue evaluations from overwriting
one another. Restore processes the candidate projects' package inputs, including
projects without Helix targets and SOS's Windows debugger package; payload creation
never builds or publishes.

Invoke `eng/helix/SendToHelix.proj /restore /t:Test` with
`/p:HelixTargetQueues=<queue>` and matching `TargetOS`, `TargetArch`, `TargetRid`,
and `Configuration` properties. To submit just one project, pass
`/p:HelixTestProject=<absolute-project-path>`. Its payload condition still applies,
and an entirely empty job is an error.
All Helix legs use `eng/pipelines/tests-helix.yml`.

SOS creates its own repository-shaped payload, on-disk ZIP, dedicated launcher, and one work
item per `RuntimeTestVersions` entry in `eng/Versions.props`, plus the Windows
Framework shard. SOS includes its test-host version and each shard's `RuntimeDownload`
version in `RequiredRuntimeVersions`; the managed-only suites do not expand this matrix.

The shared `src/tests/Helix/Managed/run-tests.cmd` and `run-tests.sh` launchers accept
`--helix-work-item <assembly>` for the default layout, or `--test-dll <path>`
and `--report-name <name>` for specialized payloads. Optional `--dotnet-root`
and `--runtime-version` select the host and runtime. Arguments after `--` are
forwarded to the test runner. SOS uses its dedicated
`src/tests/SOS.Tests/Helix/run-sos-tests.cmd` and `run-sos-tests.sh` launchers
for debugger preparation, runtime shard selection, and platform-specific cleanup.

## SOS and Other Diagnostic Tools

* [SOS](documentation/sos.md) - About the SOS debugger extension.
* [dotnet-dump](documentation/dotnet-dump-instructions.md) - Dump collection and analysis utility.
* [dotnet-gcdump](documentation/dotnet-gcdump-instructions.md) - Heap analysis tool that collects gcdumps of live .NET processes.
* [dotnet-trace](documentation/dotnet-trace-instructions.md) - Enable the collection of events for a running .NET Core Application to a local trace file.
* [dotnet-counters](documentation/dotnet-counters-instructions.md) - Monitor performance counters of a .NET Core application in real time. 

## Useful Links

* [FAQ](documentation/FAQ.md) - Frequently asked questions.
* [The LLDB Debugger](http://lldb.llvm.org/index.html) - More information about lldb.
* [SOS](https://msdn.microsoft.com/en-us/library/bb190764(v=vs.110).aspx) - More information about SOS.
* [Debugging CoreCLR](https://github.com/dotnet/runtime/blob/main/docs/workflow/debugging/coreclr/debugging-runtime.md) - Instructions for debugging .NET Core and the CoreCLR runtime.
* [dotnet/runtime](https://github.com/dotnet/runtime) - Source for the .NET Core runtime.
* [Official Build Instructions](documentation/building/official-build-instructions.md) - Internal official build instructions.

[//]: # (Begin current test results)

## Build Status

[![Build Status](https://dnceng.visualstudio.com/public/_apis/build/status/dotnet/diagnostics/diagnostics-public-ci?branchName=main)](https://dnceng.visualstudio.com/public/_build/latest?definitionId=72&branchName=main)

[//]: # (End current test results)


## License

The diagnostics repository is licensed under the [MIT license](LICENSE.TXT).
