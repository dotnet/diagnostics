# SOS.Tests

`SOS.Tests` validates the repository-built SOS across debugger hosts, runtime
flavors, runtime versions, dump kinds, GC modes, and DAC implementations. It is
an xUnit v3 Microsoft.Testing.Platform application. See
[COVERAGE.md](COVERAGE.md) for the legacy-to-new coverage audit and mutation
test evidence.

## Build and run

Build the repository before running the generated test application:

```sh
./build.sh -configuration Debug -architecture x64
./artifacts/bin/SOS.Tests/Debug/net10.0/SOS.Tests
```

```powershell
.\Build.cmd -configuration Debug -architecture x64
.\artifacts\bin\SOS.Tests\Debug\net10.0\SOS.Tests.exe
```

Use the matching `Release` paths after a Release build. `dotnet test` uses
VSTest unless the repository opts into Microsoft.Testing.Platform globally, so
it is not the supported entry point for this project. Microsoft.Testing.Platform
options can select a test or control parallelism:

```sh
./artifacts/bin/SOS.Tests/Debug/net10.0/SOS.Tests \
  --filter-method SOS.Tests.PrintExceptionTests.PrintException_Data \
  --parallel none --output Normal
```

For a small local smoke run:

```sh
SOSHARNESS_ONLY_HOSTS=DotnetDump \
SOSHARNESS_ONLY_FLAVORS=Core \
SOSHARNESS_ONLY_COREVERSIONS=Net10 \
./artifacts/bin/SOS.Tests/Debug/net10.0/SOS.Tests
```

## Architecture

The harness is split into four parts:

- `SOS.Tests` contains command-oriented xUnit theories and structured parsers
  for command-specific output.
- `SOS.TestHarness` owns matrix expansion, target acquisition, dump capture,
  host-neutral command execution, output assertions, and replay capture.
- `SOS.TestHarness.EngineHost` and `SOS.TestHarness.Capturer` isolate dbgeng and
  desktop dump capture in child processes.
- `SOS.TestHarness.SourceGen` mirrors deterministic constants from debuggee
  source into the generated `TestTargets` namespace.

### Matrix and configuration

Each theory receives one `TestConfig`, whose axes are:

| Axis | Values |
| --- | --- |
| Target | A named debuggee and its stop points from `TargetCatalog`. |
| Host | `Cdb`, `Lldb`, or `DotnetDump`, as supported by the platform. |
| Flavor | Framework-dependent `Core`, self-contained `SingleFile`, or Windows-only `Framework`. |
| Liveness | Post-mortem `Dump` or an exclusive `Live` process. |
| GC type | `Workstation` or opt-in `Server`. |
| Dump kind | `Heap`, opt-in `Mini`, or `Full`. Live rows collapse this axis to `Heap`. |
| Core version | Every built and installed supported runtime; out-of-support versions are opt-in. |
| DAC | `Legacy` or, for supported .NET 11+ Core rows, `CDac`. |

`TestConfig.BuildMatrix` forms the cross-product and removes invalid rows.
Notably, dotnet-dump is dump-only; cdb is Windows-only and LLDB is non-Windows;
Framework is Windows-only; Server GC is Core/SingleFile dump-only; single-file
Mini dumps and live LLDB navigation through stripped single-file images are
excluded; cDAC supports Core and SingleFile on .NET 11 or later.

Tests opt into expensive axes. The default is dump, workstation GC, and Heap.
Live, Server, Mini, and Full rows appear only where they exercise distinct
behavior.

### Targets, snapshots, and reuse

`SnapshotStore` acquires each `(flavor, target, core version)` once. Core and
single-file targets consume repository build outputs; desktop Framework targets
are built in the scratch tree. Snapshot stops self-collect through the
repository-built dotnet-dump, Core crash targets use createdump, and desktop
capture is delegated to the dbgeng capturer child. On Linux and macOS, the
matching runtime DAC is staged beside each single-file executable before launch
so createdump can honor Heap dump requests instead of falling back to Full.

Dumps are cached by `(flavor, target, GC type, dump kind, core version)`.
The DAC is deliberately not a capture dimension: legacy DAC and cDAC analyze
the same dump, with DAC selection happening when the host opens it. Cached
dumps are reused only while newer than their debuggee.

Helix submits one work item per installed .NET runtime. Each runtime shard sets
`SOSHARNESS_ONLY_COREVERSIONS` and runs the Core and SingleFile flavors.
Windows submits one additional Framework work item so desktop Framework
coverage runs once rather than being repeated in every runtime shard. Dump
reuse remains inside each work item's `SnapshotStore`, so hosts and DAC
implementations that analyze the same target share the captured dump.

`Targets.GetTargetAsync` returns a cheap cursor over shared, read-only dump
sessions. A session is memoized by host, target, stop, flavor, GC type, dump
kind, runtime, and DAC. Live targets are never shared because command execution
advances the process; every caller receives an isolated debuggee.

### Debugger hosts and isolation

Tests issue the same `target.Sos("command")` call through `IDebuggerHost`.
`HostFactory` routes it to:

- `ChildEngineClient` for cdb/dbgeng, keeping native engine faults outside the
  test process;
- `LldbCliHost` or `LldbLiveHost` for LLDB;
- `DotNetDumpHost` for the repository-built dotnet-dump.

LLDB runs with external symbol lookup disabled before loading a target. Dumps,
executables, SOS, and matching DACs come from local build artifacts, so network
symbol probing would only add nondeterministic startup delays.

Debugger stdout, stderr, command lines, and host crash dumps are retained by
`HostDiagnostics`. Dotnet-dump sessions use a single process slot because idle
REPL children busy-wait; dump sessions are otherwise safe to share. Live
sessions are bounded by `SOSHARNESS_MAX_LIVE`.

### Stable oracles and output parsing

Debuggees expose named stop points and deterministic objects instead of relying
on arbitrary heap ordering. The source generator reads debuggee source as
`AdditionalFiles` and mirrors public literal `const` and `static readonly`
values into `TestTargets`. A test can therefore compare SOS output with the
debuggee's own declared value without loading the debuggee assembly.

`SosOutput` preserves raw command text while exposing:

- `Name: value` fields through `output["Name"]`;
- aligned tables through `output.Table(...)`;
- reusable tokens such as `Sos.Addr`, `Sos.Hex`, and
  `Sos.ModuleFunctionWithOffset`;
- line, substring, and raw-regex assertions for output that is not naturally
  structured.

Command-specific parsers should round-trip addresses between commands and
assert exact values where possible. Regex remains an escape hatch, not the
default oracle.

## Canonical test anatomy

```csharp
public static TheoryData<TestConfig> Matrix =>
    TestConfig.BuildMatrix([TargetCatalog.NestedException]);

[SosTheory]
[MemberData(nameof(Matrix))]
public async Task PrintException_Data(TestConfig config)
{
    using Target target = await Targets.GetTargetAsync(config);
    target.GoToFirstStop();

    SosOutput output = target.Sos("printexception");
    Assert.Equal("System.InvalidOperationException", output["Exception type"]);
    output["Exception object"].AssertValid(Sos.Addr);

    SosTable frames = output.Table(
        ("SP", Sos.Addr),
        ("IP", Sos.Addr),
        ("Function", Sos.ModuleFunctionWithOffset));
    frames.AssertContainsRow(
        row => row["Function"].Contains("NestedExceptionTest.Program.Main"),
        "managed entry frame");
}
```

A canonical test defines the smallest valid matrix, acquires and disposes a
`Target`, navigates to a named stop, runs the product command, and asserts
structure plus product data. Keep host conditionals in the matrix or host
abstraction rather than duplicating the assertion body.

## Legacy coverage migration

Legacy retirement requires comparing assertions and matrices, not command-name
overlap; intentional matrix reductions are documented in the coverage audit.
Matching legacy live runs is not required for this migration; existing
live-enabled matrices remain in place.
This layer retires `DivZero.script`, `NestedExceptionTest.script`, and
`SimpleThrow.script` after moving their exact exception, source-line, stack,
thread, live/dump, and CLRMA behavior into focused tests.
`ClrStackWithNumberOfFrames.script` and `LineNums.script` are also retired after
adding dump-only ICorDebug frame-limit checks and focused LineNums source-line
assertions for `clrstack` and `printexception -lines`. Generic exception and
thread checks use existing targets. Frame-limit tests
retain the original Heap-dump matrix; live and additional dump-kind coverage
are deferred. Their shared debuggees remain.
The ICorDebug frame-limit test excludes Framework, as the legacy test did.
Temporary, narrowly scoped ICorDebug skips for .NET 10 Windows x86 legacy-DAC Heap and Mini dumps (not Full dumps)
and .NET 11 macOS ARM64 SingleFile/cDAC are defined in
[SOSTestSkips.cs](SOSTestSkips.cs), with inline investigation notes and removal criteria.
`AsyncMain.script` is retired by `ClrStackTests.ClrStack_DmlPreservesAsyncMainName`,
which runs `clrstack /d` and requires the literal `AsyncMainTest.<Main>(...)` frame.
`ThreadApartment.script` is retired by `ClrThreadsTests.ClrThreads_ReportsApartmentStates`,
which identifies the STA and MTA workers by their stacks and matches their OS thread IDs
to the expected apartment rows on Windows. Both additions use dump-only Heap
matrices and retain their debuggees. ThreadApartment now captures its unhandled
exception after both apartment threads are ready, instead of stopping at `Debugger.Break`;
the worker threads remain alive through capture. Its Core, SingleFile, and Framework
artifacts are included in the Windows harness payload.
`ClrStackRuntimeFramesTests` adds dump-only checks for `FaultingExceptionFrame`
on DivZero and `SoftwareExceptionFrame` on SimpleThrow. The latter runs on .NET 10+
Core/SingleFile; both preserve the legacy Windows x86 exclusion.
The DivZero faulting-frame check also skips ARM/ARM64 via `SOSTestSkips`: division by
zero uses a software throw there, and the legacy test did not run on those platforms.
The software-frame check remains enabled on ARM/ARM64.
`StackTraceFaultingExceptionFrame.script` and `StackTraceSoftwareExceptionFrame.script`
are retired; their shared debuggees remain.
`GCTests.script` and `GCPOH.script` are retired using existing heap coverage plus
known POH object location and roots, Core static reference fields, and native
`dumpobj -refs` coverage using the existing Scenarios target. These additions use dump-only
matrices. Framework shared-static field coverage is deferred. The reference
test uses the standard matrix, including SingleFile,
and checks identical reference oracles through `dumpobj -refs` in native hosts
or `dumpobj` plus `dumpobjgcrefs` in dotnet-dump, which lacks the callback bridge.
`DynamicMethod.script` is retired by `DynamicMethodTests`, which checks the emitted
Fibonacci IL and the ICorDebug local-to-object-to-IL round-trip. Its variable
matrix remains Core-only; heap inspection also covers SingleFile. Empty matrices
in excluded flavor shards skip only these two theories.
`Reflection.script` is retired using the existing
`PrintExceptionTests.PrintException_ReflectionInnerException` coverage without
adding assertions or duplicating generic stack/thread checks on this target.
Both retain their existing debuggees without changing the harness or CI.
`DumpGCData.script` is retired by
`DiagnosticCommandTests.DumpGcData_ReportsPinningAfterCollection`, which preserves
its zero-to-one pinning transition on .NET 10+ using an isolated two-stop target.
Workstation GC runs live and in dumps; server GC runs in dumps. Core-only
native and dotnet-dump rows retain the applicable host coverage.
The legacy runner remains active. [COVERAGE.md](COVERAGE.md) records the
evidence and all remaining retained scenarios and gaps.

## Controls

Comma-separated matrix allow-lists are case-insensitive enum names:

| Variable | Purpose |
| --- | --- |
| `SOSHARNESS_ONLY_HOSTS` | Select `Cdb`, `Lldb`, and/or `DotnetDump`. |
| `SOSHARNESS_ONLY_FLAVORS` | Select `Core`, `SingleFile`, and/or `Framework`. |
| `SOSHARNESS_ONLY_LIVENESS` | Select `Dump` and/or `Live`. |
| `SOSHARNESS_ONLY_GCTYPE` | Select `Workstation` and/or `Server`. |
| `SOSHARNESS_ONLY_DUMPKIND` | Select `Heap`, `Mini`, and/or `Full`. |
| `SOSHARNESS_ONLY_COREVERSIONS` | Select versions such as `Net8,Net11`; explicit selection also permits an out-of-support version. |
| `SOSHARNESS_ONLY_DAC` | Select `Legacy` and/or `CDac`. |
| `SOSHARNESS_TEST_OUT_OF_SUPPORT_CORE` | Set to `1` to include every installed out-of-support runtime. |
| `SOSHARNESS_HOST_RUNTIME_DIR` | Override the complete runtime layout used to host SOS's managed extension. |
| `SOSHARNESS_MAX_LIVE` | Set the positive maximum number of concurrent live sessions. |
| `SOSHARNESS_LIVE_TIMEOUT` | Set the positive live LLDB command timeout in seconds. |
| `SOSHARNESS_LLDB_LOAD_TIMEOUT` | Set the positive LLDB target-load timeout in seconds. |
| `SOSHARNESS_LLDB_TRACE` | Write the LLDB command trace to the specified file. |
| `SOSHARNESS_DAC_DIR` | Override the legacy DAC directory used by the dbgeng engine host. |
| `SOSHARNESS_CDAC_DIR` | Override cDAC discovery with a directory containing the matched universal cDAC and DBI binaries. |
| `SOSHARNESS_USECDAC` | Local global DAC clamp; overrides the matrix DAC selection and is not set in CI. |
| `LLDB_PATH` | Override LLDB discovery. Otherwise Xcode and then `PATH` are searched. |
| `NUGET_PACKAGES` | Override the NuGet package root used to locate runtime packs and cDAC assets. |

The Azure Linux Helix Alpine container runs the work item one test at a time to
avoid an intermittent .NET 8 createdump `PR_SET_PTRACER` race.

The harness sets the following implementation-owned values for child
processes; they are not supported user controls:

| Variable | Owner and purpose |
| --- | --- |
| `SOSHARNESS_CAPTURE_DIR`, `SOSHARNESS_DOTNET`, `SOSHARNESS_DOTNETDUMP_DLL`, `SOSHARNESS_DUMP_TYPE` | Tell a snapshot debuggee where and how to self-collect. |
| `SOSHARNESS_STATE` | LLDB stop-point protocol emitted by a live debuggee. |
| `_NT_SYMBOL_PATH` | Constrains child engines to the harness symbol cache. |
| `DOTNET_ROOT`, `DOTNET_ROOT(x86)`, `DOTNET_MULTILEVEL_LOOKUP` | Bind Core debuggees to the selected test-runtime installation. |
| `DOTNET_DbgEnableMiniDump`, `DOTNET_DbgMiniDumpType`, `DOTNET_DbgMiniDumpName`, `DOTNET_CreateDumpDiagnostics` | Configure createdump crash capture. |
| `DOTNET_DbgEnableElfDumpOnMacOS`, `TMPDIR` | Produce readable ELF dumps and a short diagnostics socket path on macOS. |
| `DOTNET_gcServer`, `DOTNET_GCHeapCount`, `DOTNET_GCDynamicAdaptationMode` | Create deterministic four-heap Server GC targets. |
| `SOS_LLDB_HARDWARE_JIT_BREAKPOINTS` | Set to `1` for live LLDB hosts on macOS arm64 so SOS plants `bpmd` breakpoints as hardware breakpoints. Debugserver writes a software breakpoint by copy-on-writing the `MAP_JIT` page, and running threads can then intermittently take a spurious `EXC_BAD_ACCESS` (`KERN_PROTECTION_FAILURE`) instruction fault. Hardware breakpoints are limited to a few per process (6 on Apple M-series), and setting more fails. |

## Output and artifacts

Passing tests print the normal Microsoft.Testing.Platform summary. A failing
test that acquired a target writes:

```text
SOS replay written to: artifacts/TestResults/SOS.Tests/SOS-replays/<run>/<test>.replay.txt
```

The replay records the test and `TestConfig`, failure and stack, every dump and
ordered command, copy/paste host replay instructions, host stdout/stderr, LLDB
trace setting, and debugger-host crash dumps. Capture failures remain the
original test failure even if replay writing also fails.

Reusable targets, dumps, and symbols live under
`artifacts/tmp/sos-harness/<Configuration>`. Replays and debugger-host crash
artifacts live under `artifacts/TestResults/SOS.Tests`. Dumps can be large;
remove the scratch subtree when a clean recapture is required.

When a Helix work item fails, its complete SOS harness dump directory is
compressed into the work-item upload root. This retains every dump from the
failed runtime/configuration leg without increasing artifacts for passing legs.

## Helix execution

`HelixPayload.targets` implements SOS's own `CreateHelixPayload` target.
`SOS.Tests.csproj` imports that implementation directly. Like every other Helix
test project, SOS owns file collection, staging, validation, and work-item metadata.
It creates one self-contained payload per OS, RID, configuration, and queue;
submission uses one work item per runtime, plus the Windows Framework work item.
Windows ARM64 runs on the Windows 11 ARM64 Helix queue with an ARM64 SDK,
runtime, DbgEng, native SOS, and debuggee payload.

The managed DbgEng engine and capture processes initialize SOS through
`HostServices.Initialize` after loading the native extension. This shares their
existing CoreCLR instead of starting another runtime or falling back to Desktop CLR.

The payload contains `SOS.Tests`, its harness subprocesses, native SOS, the
repository-built dotnet-dump, DbgEng on Windows, and all prebuilt Core,
SingleFile, and Framework debuggees needed by that platform. The exact runtime
versions are defined by `RuntimeTestVersions` in `eng/Versions.props`. The same
catalog drives debuggee publishing, harness metadata, and Helix installation.
Helix provisions the pinned .NET 10 SDK and overlays every test runtime into the
same correlation payload rather than copying a .NET installation into each
work-item payload. Each shard includes its host and debuggee runtime versions in
`RequiredRuntimeVersions` metadata returned by `CreateHelixPayload`.
The shared sender gathers all work items and deduplicates
their runtime requirements into the SDK's `AdditionalDotNetPackage` items.
This also ensures the test-host runtime is
available independently of the debuggee matrix. SOS retains its existing
platform-specific host selection, including .NET 11 on macOS.

`StageSOSHelixPayload` performs SOS staging without submitting; `CreateHelixPayload`
also prepares the runtime shards, creates the on-disk ZIP, and returns complete
work items. To submit SOS alone, invoke `eng/helix/SendToHelix.proj` with
`HelixTestProject` set to the absolute path of `SOS.Tests.csproj`.

Private runtime validation passes `PrivateBuildTesting=true` and
`LiveRuntimeDir` to the sender. The payload includes that complete runtime
layout and overlays it onto the matching Helix-provisioned runtime before the
test starts. Private runs submit only the configured runtime shard and select
the `Core` flavor; self-contained and Framework targets continue to use product
runtime packages and are therefore excluded.

Private universal cDAC and DBI binaries are staged as a matched pair under
`artifacts/cdac-override/<Configuration>`. Before creating a host, the harness
installs that pair beside native SOS for CDB and LLDB or in dotnet-dump's
`publish/<TargetRid>` native directory. The same path is used in Helix and local
runs, with `SOSHARNESS_CDAC_DIR` available to select a different source locally.

The payload includes a `.sos-test-payload` marker and preserves the repository
artifact layout. `RepoLayout` discovers that root and derives all tool,
debuggee, scratch, and upload paths. The platform launcher
only performs required machine preparation such as restoring executable bits,
macOS codesigning, LLDB discovery, and Windows signature-check setup.
