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
