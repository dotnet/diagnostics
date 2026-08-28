# SOS.Tests

`SOS.Tests` validates the repository-built SOS across the debugger hosts, runtime flavors, and runtime
versions supported by the test harness. It is an xUnit v3 Microsoft.Testing.Platform application.

Build the repository first, then run the generated test application directly:

```powershell
.\Build.cmd -configuration Debug
.\artifacts\bin\SOS.Tests\Debug\net10.0\SOS.Tests.exe
```

Use the matching `Release` paths after a Release build. `dotnet test` uses VSTest unless the repository
opts into Microsoft.Testing.Platform globally, so it is not the supported entry point for this project.

## Matrix

The default matrix covers supported Core runtimes, the platform debugger (`cdb` or `lldb`),
`dotnet-dump`, Core/single-file/.NET Framework flavors where applicable, workstation GC, Heap dumps,
and both the legacy DAC and cDAC where supported. Live debugging, server GC, Mini dumps, and
out-of-support runtimes are opt-in to keep normal runs bounded.

The cDAC is excluded for single-file targets because its current single-file support does not expose
the execution metadata required by the shared SOS command suite. Single-file coverage uses the matching
legacy DAC. Live LLDB navigation is excluded for single-file snapshot targets because the statically
linked, stripped runtime does not expose the symbol needed to arm `bpmd`.

On Windows, reduced dumps of unsigned test runtimes require the machine-wide
`DisableAuxProviderSignatureCheck` setting. When it is absent, Heap requests use Full dumps instead.
Mini rows are skipped because replacing a Mini dump with a Full dump would not test Mini behavior.

## Local matrix filters

The following environment variables narrow or extend a run:

| Variable | Purpose |
| --- | --- |
| `SOSHARNESS_ONLY_HOSTS` | Comma-separated hosts, such as `Cdb` or `DotnetDump`. |
| `SOSHARNESS_ONLY_FLAVORS` | Comma-separated flavors: `Core`, `SingleFile`, or `Framework`. |
| `SOSHARNESS_ONLY_LIVENESS` | `Dump` or `Live`. |
| `SOSHARNESS_ONLY_GCTYPE` | `Workstation` or `Server`. |
| `SOSHARNESS_ONLY_DUMPKIND` | `Heap`, `Mini`, or `Full`. |
| `SOSHARNESS_ONLY_COREVERSIONS` | Runtime versions, such as `Net8,Net11`. |
| `SOSHARNESS_ONLY_DAC` | `Legacy` or `CDac`. |
| `SOSHARNESS_TEST_OUT_OF_SUPPORT_CORE` | Set to `1` to include installed out-of-support runtimes. |
| `SOSHARNESS_ARTIFACTS_CONFIG` | Override the configuration embedded at build time. |
| `SOSHARNESS_MAX_LIVE` | Maximum concurrent live-debugging sessions. |
| `SOSHARNESS_LIVE_TIMEOUT` | Live LLDB command timeout in seconds. |
| `SOSHARNESS_LLDB_LOAD_TIMEOUT` | LLDB target-load timeout in seconds. |
| `SOSHARNESS_LLDB_TRACE` | Enable LLDB protocol tracing. |
| `SOSHARNESS_CDAC_DIR` | Override the directory containing the cDAC. |
| `SOSHARNESS_USECDAC` | Globally clamp cDAC selection for local investigation. |

Failures write replay instructions under `artifacts/TestResults/SOS.Tests`. Harness-generated targets,
dumps, and symbol caches are stored under `artifacts/tmp/sos-harness/<Configuration>`.

## Legacy SOS.UnitTests migration

Retirement is based on assertion and matrix equivalence, not command-name overlap. A legacy test remains
when it carries a distinct debugger path, target topology, option, or state transition. The first retired
group is covered by `PrintExceptionTests`, `ClrStackLinesTests`, `ClrThreadsTests`, and
`DiagnosticCommandTests` across the harness host/flavor/runtime/DAC matrix and both live and dump targets.

| Legacy test | Status | Modern coverage or remaining gap |
| --- | --- | --- |
| `StackTraceSoftwareExceptionFrame` | Retained | Requires an explicit `SoftwareExceptionFrame` assertion. |
| `StackTraceFaultingExceptionFrame` | Retained | Requires an explicit `FaultingExceptionFrame` assertion. |
| `StackTests` | Retained | Composite stack-command and exception-frame coverage is not fully mapped. |
| `ClrStackWithNumberOfFrames` | Retained | Plain `clrstack -c` is covered; `clrstack -i -c` and triage-dump behavior remain. |
| `DivZero` | Retired | Exact divide-by-zero `printexception`, `-nested`, `-lines`, source-stack, and live/dump coverage moved to `SOS.Tests`. |
| `SimpleThrow` | Retired | Exact invalid-operation `printexception`, `-nested`, `-lines`, source-stack, and live/dump coverage moved to `SOS.Tests`. |
| `NestedExceptionTest` | Retired | Outer/inner exception data, `-nested`, `-lines`, source-stack, `clrthreads`, and CLRMA exception-chain coverage moved to `SOS.Tests`; generic live `bpmd` is covered by `LiveBpmdTests`. |
| `TaskNestedException` | Retained | Preserves the three-level `AggregateException`/task frame and source-line chain. |
| `InterpreterStackTest` | Retained | Interpreter-only managed stack behavior. |
| `InterpreterStackInterleavedTest` | Retained | Interpreter/native interleaved stack behavior. |
| `Overflow` | Retained | Stack-overflow-specific unwind and exception behavior. |
| `GCTests` | Retained | Modern tests cover the commands and generation transitions, but the legacy live multi-stop sequence needs final parity review. |
| `GCPOHTests` | Retained | Requires direct `gcwhere` POH and pinned-root parity. |
| `FindRootsOlderGeneration` | Retained | Older-generation root search remains unique. |
| `DumpGCData` | Retained | Preserves the live zero-to-one pinned-object transition. |
| `DumpGen` | Retained | Basic generation output is covered; argument errors, filters, empty results, and all generation cases remain. |
| `MiniDumpLocalVarLookup` | Retained | Mini-dump local-variable lookup remains unique. |
| `ConcurrentDictionaries` | Retained | Preserves multiple generic dictionary layouts. |
| `OtherCommands` | Retained | Composite SymbolTestApp, stress-log history, and command-option coverage is not fully replaced. |
| `DynamicMethod` | Retained | Dynamic-method `clrstack -i -a` locals remain unique. |
| `Reflection` | Retired | Exact reflected outer/inner exceptions, HRESULTs, `-nested`, `-lines`, source-stack, and live/dump coverage moved to `SOS.Tests`. |
| `VarargPInvokeInteropMD` | Retained | CDB-only `InlinedCallFrame`, IL stub, and disassembly behavior remains unique. |
| `ThreadApartment` | Retained | Requires a target that guarantees both STA and MTA threads. |
| `LineNums` | Retained | `clrstack` source lines are covered; `printexception -lines` and its thread context remain. |
| `AsyncMain` | Retained | Preserves DML rendering of the escaped `<Main>` method name. |
| `TestExtensions` | Retained | Extension load and command behavior remains unique. |
| `WebApp3` | Retained | ASP.NET hosting topology remains unique. |
| `DualRuntimes` | Retained | Dual-runtime selection remains unique. |
| `StackAndOtherTests` | Retained | Multi-assembly SymbolTestApp and PDB-variant coverage is not fully replaced. |
| `LLDBPluginTests` | Retained | Python LLDB integration remains a separate test surface. |
