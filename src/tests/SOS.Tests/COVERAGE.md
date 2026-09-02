# SOS test coverage audit

This audit compares PR #5979 at `40ccc0262a199a1576ed3bec25d82152a32c1bc4`
with the legacy `SOS.UnitTests` entry points and 29 scripts present at the same
commit. The new project contains 89 command-focused test methods after the
focused `dumpgen` addition described below.

## Result

The repository's baseline coverage equals or exceeds the previous baseline:
PR #5979 does not delete or disable any legacy test, and adds the new matrix and
89 tests. The stronger claim that `SOS.Tests` alone replaces every legacy
scenario is not supported. Most command behavior is covered more strictly, but
specialized debuggees and debugger transitions remain in `SOS.UnitTests`.
Those tests must remain until their rows marked **gap** below are migrated.

Status meanings:

- **Covered**: the same observable command behavior has a direct new test.
- **Improved**: the new test adds a stronger oracle, more options, or broader
  host/flavor/runtime coverage.
- **Retained**: the specialized scenario remains intentionally owned by the
  legacy suite; the command may also have generic new coverage.
- **Gap**: `SOS.Tests` has no equivalent for a material legacy behavior. The
  legacy test is still active, so this is a migration gap rather than a
  regression in the PR baseline.

No legacy script is retired by #5979. Rows that are fully covered or improved
are candidates for later retirement only after CI proves the intended matrix
replacement.

## Legacy-to-new map

| Legacy script | Status | New evidence and remaining legacy value |
| --- | --- | --- |
| `AsyncMain.script` | Gap | General stack shape is covered by `ClrStackLinesTests` and `ClrStackAllThreadsTests`; the async-`Main` frame identity has no new oracle. |
| `ClrStackWithNumberOfFrames.script` | Improved | `ClrStackFrameCountTests.ClrStack_FrameCount` compares each `-c N` result with the exact prefix of an unlimited walk and checks an over-limit request across four crash targets. |
| `ConcurrentDictionaries.script` | Improved | `SpecializedInspectionTests.Dcd_DumpsConcurrentDictionary`, `DumpArrayTests`, and `ObjectFieldsTests` provide typed data assertions. Legacy still covers dcd argument errors and its wider generic key/value set. |
| `DivZero.script` | Improved | `PrintExceptionTests`, `ClrThreadsTests`, `ClrStackLinesTests`, `ClrStackICorDebugTests`, and frame-count tests split the monolithic script into data and structure oracles. |
| `DualRuntimes.script` | Retained | Generic stacks, threads, heaps, and runtime listing are covered; loading and switching between two runtimes in one process remains a specialized legacy scenario. |
| `DumpGCData.script` | Covered | `DiagnosticCommandTests.DumpGcData_ReportsGcStatistics` directly exercises `dumpgcdata`. |
| `DumpGen.script` | Improved | `GcInspectionTests.DumpGen_ListsGenerationObjects` asserts a known gen0 object. `DumpGen_ArgumentsAndFilters` adds missing/invalid generation and valid `-type`/`-mt` coverage. Exact legacy gen1/gen2/LOH/POH populations remain retained. |
| `DynamicMethod.script` | Retained | `DumpIlTests` validates IL addresses and instructions and ICorDebug is covered elsewhere; the emitted dynamic-method target remains a legacy scenario. |
| `FindRootsOlderGeneration.script` | Gap | `GcRoot_FindsRootsForLive_NoneForDead` improves ordinary root correctness, but the live `findroots -gen any` notification/continue sequence and older-generation result are not represented. |
| `GCPOH.script` | Improved | `DumpHeapGenerationsTests`, `DumpArrayTests`, `GcHandles`, `VerifyHeap`, `EeHeapTests`, and stack-root tests use deterministic objects and structured assertions; the original POH script remains active. |
| `GCTests.script` | Improved | Object fields, `gcwhere`, stack objects, heap statistics, roots, handles, finalization, and verification are split into focused tests with exact object oracles. |
| `InterpreterStackInterleavedTest.script` | Retained | No generic target can replace the interleaved interpreter/JIT frame sequence; legacy ownership is intentional. |
| `InterpreterStackTest.script` | Retained | Interpreter-frame ordering remains specialized legacy coverage. |
| `LineNums.script` | Improved | `ClrStackLinesTests` checks source file/line behavior and `PrintExceptionTests` checks exception structure/data across the matrix. |
| `MiniDumpLocalVarLookup.script` | Gap | `ClrStackArgsLocalsTests` and `ClrStackICorDebugTests` improve variable data checks, but they use full/heap dumps rather than proving local recovery from a Mini dump. |
| `NestedExceptionTest.script` | Improved | `PrintExceptionTests` verifies exact outer/inner types, messages, HResult, frame data, and inner-address round-trip; stack variants are independently covered. |
| `OtherCommands.script` | Improved | Its broad command set is split across object, module, domain, heap, runtime, memory, code-info, and diagnostic test classes with structured round-trips. |
| `Overflow.script` | Retained | Stack-overflow exception behavior remains a specialized crash/live legacy target; ordinary exception output is covered by `PrintExceptionTests`. |
| `Reflection.script` | Retained | Target-invocation exception and reflection boundary behavior remains legacy; generic nested exception and stack behavior is improved in the new project. |
| `SimpleThrow.script` | Covered | Exception fields, HResult, source lines, threads, and managed stack shape are directly covered by the new exception and stack classes on deterministic crash targets. |
| `StackAndOtherTests.script` | Improved | `RuntimeInfoTests` and the `ClrStack*Tests` classes separately cover runtime selection, plain/line/full/all/register/args/locals stack modes with stronger comparisons. |
| `StackTests.script` | Improved | `ClrStackTests`, `ClrStackFullTests`, `ClrStackAllThreadsTests`, `ClrStackArgsLocalsTests`, `StackInspectionTests`, and `RuntimeInfoTests` replace shape-only checks with tables and address round-trips. |
| `StackTraceFaultingExceptionFrame.script` | Gap | Exception and ordinary stack data are covered; no new assertion requires the synthetic `[FaultingExceptionFrame: ...]` row. |
| `StackTraceSoftwareExceptionFrame.script` | Gap | Exception and ordinary stack data are covered; no new assertion requires the synthetic `[SoftwareExceptionFrame: ...]` row. |
| `TaskNestedException.script` | Gap | New tests cover one inner-exception round-trip, not the AggregateException/task chain and source lines. |
| `TestExtensions.script` | Retained | This validates extension-command interception and dispatch rather than SOS product output; it remains a legacy harness integration test. |
| `ThreadApartment.script` | Gap | `ClrThreadsTests` validates thread rows and counts, but does not assert Windows STA/MTA apartment flags. |
| `VarargPInvokeInteropMD.script` | Retained | Vararg P/Invoke, IL stubs, native breakpointing, `ip2md`, and `clru -il` form one specialized interop scenario. Generic `ip2md` and `clru` have new structured tests. |
| `WebApp.script` | Retained | Timers, ASP.NET/WebApp stacks, args/registers, and GC stress-log behavior remain in the specialized Windows legacy target; generic command equivalents are covered. |

The focused gap fixed in this review is `dumpgen` argument and filtering
behavior. The new test uses the source-generated scenario marker, resolves its
real MethodTable through `dumpobj`, and proves both `-type` and `-mt` select it.
The remaining gaps require purpose-built targets, Mini-dump policy, or
platform-specific debugger sequencing and are not appropriate baseline-harness
refactors.

## Product mutation experiment

### Selection

A fresh Copilot session performed selection independently from the harness
implementation. It formed eligible candidate IDs from real SOS product output
behaviors with assertions in both suites, excluding harness-only defects and
sites unavailable on macOS arm64. With seed
`0x40ccc0262a199a15`, it sorted candidates by:

```text
SHA256(seed + NUL + candidate-id)
```

and selected the first five:

1. `dumpmt-type-name`
2. `clrstack-frame-limit`
3. `ip2md-methoddesc`
4. `dumparray-element-count`
5. `printexception-hresult`

Each mutation was made in `src/SOS/Strike/strike.cpp`, native SOS was rebuilt,
and the mutated `libsos.dylib` was staged beside the repository-built
dotnet-dump. Mutations were tested one at a time and restored before the next.
None is part of the final diff.

### Host constraint and comparison validity

On this macOS arm64 machine, the unchanged legacy test driver enters mandatory
live/native LLDB phases that crash before reaching the script assertions. For
this experiment only, an uncommitted adapter selected one Core runtime row and
skipped those LLDB phases while leaving the legacy scripts, dotnet-dump
commands, dumps, and regex assertions unchanged. The corresponding new tests
were narrowed to Core, dotnet-dump, dump, workstation GC, Heap, and legacy DAC.
This compares the same repository-built SOS backend and equivalent product
behavior, without treating an environmental LLDB crash as mutation detection.

The unmodified legacy probes passed before mutation. Final baseline runs of the
new probes pass after all mutations are restored.

### Results

| Mutation | Temporary product defect | Legacy result | New result | Assessment |
| --- | --- | --- | --- | --- |
| `dumpmt-type-name` | Force `dumpmt`'s type name to print `<unknown>`. | `OtherCommands` failed because `Name: SymbolTestApp.Program` disappeared. | `DumpObj_Mt_Class_Md_Chain` failed because `ThinLockMarker` became `<unknown>`. | Equal detection; new test uses a deterministic named marker and command round-trip. |
| `clrstack-frame-limit` | Make `clrstack -c N` emit one extra frame. | `ClrStackWithNumberOfFrames` failed its output-line cardinality regex. | `ClrStack_FrameCount` failed all four selected targets; for `-c 1`, expected 1 row and observed 2. | Equal detection, with clearer new diagnostics and multi-target evidence. |
| `ip2md-methoddesc` | Print `pMD + 1` in the `ip2md` MethodDesc field. | `OtherCommands` failed its downstream code-size/method-info assertion after consuming the bad address. | `Ip2md_ResolvesJittedMethodWithSource` failed directly: expected `4535571072`, actual `4535571073`. | Equal detection; the new failure localizes the corrupted field. |
| `dumparray-element-count` | Report `dwNumComponents + 1` from `dumparray`. | `ConcurrentDictionaries` failed `Number of elements 4`. | `DumpArray_StructureStartLengthDetails` failed: expected 8, actual 9. | Equal detection; the new test also checks rank, type, listed rows, slicing, and details. |
| `printexception-hresult` | Report `HResult + 1`. | `NestedExceptionTest` failed `HResult: 80131509`. | `PrintException_Data` failed: expected `0x80131509`, actual `0x8013150a`. | Equal detection; the new test parses the field as a typed `UInt32`. |

All five new probes were at least as effective as the corresponding legacy
probe: every defect detected by a legacy assertion was also detected by the new
test, usually at a more specific field or row.
