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
capture is delegated to the dbgeng capturer child.

Dumps are cached by `(flavor, target, GC type, dump kind, core version)`.
The DAC is deliberately not a capture dimension: legacy DAC and cDAC analyze
the same dump, with DAC selection happening when the host opens it. Cached
dumps are reused only while newer than their debuggee.

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

Legacy retirement requires assertion and matrix equivalence, not command-name
overlap. This layer retires `DivZero.script`, `NestedExceptionTest.script`, and
`SimpleThrow.script` after moving their exact exception, source-line, stack,
thread, live/dump, and CLRMA behavior into focused tests. `Reflection.script`
remains active because its reflected target-invocation boundary is still a
specialized legacy scenario. [COVERAGE.md](COVERAGE.md) records the evidence and
all remaining retained scenarios and gaps.

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
| `SOSHARNESS_ARTIFACTS_CONFIG` | Override the build configuration embedded in the harness assembly. |
| `SOSHARNESS_MAX_LIVE` | Set the positive maximum number of concurrent live sessions. |
| `SOSHARNESS_LIVE_TIMEOUT` | Set the positive live LLDB command timeout in seconds. |
| `SOSHARNESS_LLDB_LOAD_TIMEOUT` | Set the positive LLDB target-load timeout in seconds. |
| `SOSHARNESS_LLDB_TRACE` | Enable LLDB protocol tracing and record its value in replay files. |
| `SOSHARNESS_DAC_DIR` | Override the legacy DAC directory used by the dbgeng engine host. |
| `SOSHARNESS_CDAC_DIR` | Override cDAC discovery with a directory containing the cDAC. |
| `SOSHARNESS_USECDAC` | Local global DAC clamp; overrides the matrix DAC selection and is not set in CI. |
| `LLDB_PATH` | Override LLDB discovery. Otherwise Xcode and then `PATH` are searched. |
| `NUGET_PACKAGES` | Override the NuGet package root used to locate runtime packs and cDAC assets. |

The harness sets the following implementation-owned values for child
processes; they are not supported user controls:

| Variable | Owner and purpose |
| --- | --- |
| `SOSHARNESS_CAPTURE_DIR`, `SOSHARNESS_DOTNET`, `SOSHARNESS_DOTNETDUMP_DLL`, `SOSHARNESS_DUMP_TYPE` | Tell a snapshot debuggee where and how to self-collect. |
| `SOSHARNESS_STATE` | LLDB stop-point protocol emitted by a live debuggee. |
| `_NT_SYMBOL_PATH` | Constrains child engines to the harness symbol cache. |
| `DOTNET_ROOT`, `DOTNET_ROOT(x86)`, `DOTNET_MULTILEVEL_LOOKUP` | Bind Core debuggees to the repository test-runtime installation. |
| `DOTNET_DbgEnableMiniDump`, `DOTNET_DbgMiniDumpType`, `DOTNET_DbgMiniDumpName`, `DOTNET_CreateDumpDiagnostics` | Configure createdump crash capture. |
| `DOTNET_DbgEnableElfDumpOnMacOS`, `TMPDIR` | Produce readable ELF dumps and a short diagnostics socket path on macOS. |
| `DOTNET_gcServer`, `DOTNET_GCHeapCount`, `DOTNET_GCDynamicAdaptationMode` | Create deterministic four-heap Server GC targets. |

## Output and artifacts

Passing tests print the normal Microsoft.Testing.Platform summary. A failing
test that acquired a target writes:

```text
SOS replay written to: artifacts/TestResults/SOS.Tests/<run>/<test>.replay.txt
```

The replay records the test and `TestConfig`, failure and stack, every dump and
ordered command, copy/paste host replay instructions, host stdout/stderr, LLDB
trace setting, and debugger-host crash dumps. Capture failures remain the
original test failure even if replay writing also fails.

Reusable targets, dumps, symbols, and host crash artifacts live under
`artifacts/tmp/sos-harness/<Configuration>`. Dumps can be large; remove that
scratch subtree when a clean recapture is required.
