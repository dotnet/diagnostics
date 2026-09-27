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

Projects migrated to Helix provide a `CreateHelixPayload` target and mark
themselves for exclusion from legacy CI runs in their `.csproj`:

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
The four initially migrated managed suites require only `TargetRid=linux-x64`, regardless of
queue name or configuration. They share the existing Linux x64 Helix jobs with
SOS in both Debug and Release instead of running in a separate managed-only job.
SOS retains its full matrix.

`EventPipe.UnitTests` and `DotnetGCDump.UnitTests` participate on every configured
Helix platform because they exercise runtime diagnostics and, for EventPipe,
child-process execution. Both are excluded from legacy CI execution.
Helix includes macOS x64 in non-PR builds only.

The NETCore.Client, Monitoring.EventPipe, DotnetCounters, and DotnetTrace suites
also run on every configured Helix platform and are excluded from legacy CI.
They import `src/tests/CommonTestRunner/HelixPayload.targets`, which extends the
managed payload with all projects discovered under `CommonTestRunner/Debuggees`,
using the same project glob as the debuggee build, for every entry in
`RuntimeTestVersions`. Suites can use multiple debuggees without a per-suite
packaging list. DotnetTrace additionally sets `HelixIncludeDotNetTrace`
to stage the prebuilt tool and provision its runtime.

These payloads replace build-machine configuration with relative artifact paths
and use the launcher's `DOTNET_ROOT` for child processes. Each configured runtime
is provisioned through work-item metadata, preserving the tracee runtime matrix.
Tracees use `sdk.prebuilt` exclusively; missing inputs fail payload creation
rather than triggering a build or restore on the worker.

`src/tests/Directory.Build.targets` applies the legacy-CI skip flag.
Every project participating in Helix defines or imports a `CreateHelixPayload`
target, which stages prebuilt artifacts and returns ready-to-run
`HelixWorkItem` items. The four managed-only projects explicitly import
`src/tests/Helix/Managed/HelixPayload.targets` when `TargetRid` is `linux-x64`.
This helper owns their common staging, validation, and work-item metadata.
It is not imported globally. SOS keeps its separate implementation in
`SOS.Tests/HelixPayload.targets`.

The shared helper stages the entire assembly output, including `.deps.json`,
`.runtimeconfig.json`, and dependencies such as `Microsoft.DotNet.RemoteExecutor.dll`.
RemoteExecutor uses the running `dotnet` host and the test assembly's runtime
configuration to launch children; it does not require an SDK or a separately
installed runtime for its own package target framework.

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
and selects its launcher using `TargetOS`: `runtests.cmd` invoked with `call` for
`Windows_NT`, or `runtests.sh` invoked with `bash` otherwise. It then returns a
directory for the SDK to archive, with explicit launcher arguments and a
30-minute timeout. The four initially migrated suites remain limited to `linux-x64`;
EventPipe, DotnetGCDump, and the CommonTestRunner suites use the existing
cross-platform Helix matrix.

`eng/helix/SendToHelix.proj` is a dedicated `Microsoft.DotNet.Helix.Sdk` project.
It gets candidate projects, calls `CreateHelixPayload` with
`SkipNonexistentTargets=true` in parallel, and submits their combined work items
as one job per queue. The SDK owns queue
fanout, runtime provisioning, reporting, and waiting. Each queue stages into its
own payload directory, preventing concurrent queue evaluations from overwriting
one another. Restore processes the candidate projects' package inputs, including
projects without Helix targets and SOS's Windows debugger package. Candidate restores
run sequentially within each queue to avoid concurrent writes to shared dependency
outputs. The SDK's per-queue restore fan-out is unchanged. Payload creation never
builds or publishes.

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

The shared `src/tests/Helix/Managed/runtests.cmd` and `runtests.sh` launchers accept
`--helix-work-item <assembly>` for the default layout, or `--test-dll <path>`
and `--report-name <name>` for specialized payloads. Optional `--dotnet-root`
and `--runtime-version` select the host and runtime. Arguments after `--` are
forwarded to the test runner. SOS uses its dedicated
`src/tests/SOS.Tests/Helix/runtests.cmd` and `runtests.sh` launchers
for debugger preparation, runtime shard selection, and platform-specific cleanup.

Both sets of launchers emit xUnit XML reports with filenames ending in
`.testResults.xml`, which the Helix reporter recognizes and publishes to Azure
Pipelines. They do not emit TRX reports, avoiding duplicate result publication.
SOS also emits HTML reports for inspection.

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
