# Microsoft.FileFormats: custom-model discovery with NativeAOT support

## Version introduced

This change is planned for the `Microsoft.FileFormats` 1.1 package series, implemented by
[dotnet/diagnostics#6103](https://github.com/dotnet/diagnostics/pull/6103).
The project specifies `VersionPrefix` as `1.1.0`. The existing Arcade versioning scheme supplies
the patch component, producing packages named `1.1.<build>`. The exact first published version
will be determined by the release build.

This is a library package version, not a .NET runtime version. The .NET Framework and .NET Standard
targets remain available; the new modern .NET target supports NativeAOT and trimming.

## Description

Built-in format readers and layout setup helpers now register their known binary structures
instead of enabling reflection discovery of arbitrary types derived from `TStruct`.
Reading custom structures through those managers requires explicit registration or an opt-in
to legacy discovery.

The release retains major version 1, but this custom-model discovery change is a compatibility
exception. A public GitHub search found no confirmed affected consumer; it does not rule out
private or unindexed consumers.

## Previous behavior

Managers configured by built-in format helpers could discover custom `TStruct` models on demand.
Readers returned by format parsers also supported this discovery. For example, a caller could
define a binary model in its own assembly and read it without registering it:

```csharp
public class CustomHeader : TStruct
{
    public uint Signature;
    public ushort Flags;
}

Reader reader = pe.RelativeVirtualAddressReader;
CustomHeader header = reader.Read<CustomHeader>(rva);
```

## New behavior

The same read requires a layout for `CustomHeader` to be registered on the reader's manager.
Without registration or a discovery provider, the read throws `LayoutException` with a message
of the form:

```text
Unable to create layout for type <namespace>.CustomHeader
```

Built-in format structures remain registered, so callers using the normal high-level parser
APIs do not need to register those structures themselves.

The default `Reader(IAddressSpace, bool)` constructor still enables legacy reflection discovery.
Managers explicitly configured with `AddTStructTypes` or `AddReflectionTypes` also retain that
behavior. The change is specific to managers configured by built-in format helpers and readers
created by format parsers.

## Type of breaking change

- **Behavioral:** previously successful reads of unregistered custom models through built-in
  managers can now throw `LayoutException`.
- **Analyzer/source-build impact:** legacy APIs now carry `RequiresDynamicCode` and
  `RequiresUnreferencedCode` annotations. Recompiling consumers with the relevant AOT or trimming
  analyzers can produce `IL3050` or `IL2026` diagnostics. Builds treating those warnings as errors
  may require changes. The legacy APIs remain callable in ordinary untrimmed managed applications.

The existing public method signatures are retained. In particular, `GetArrayLayout<T>` still takes
an **array type**, such as `byte[]`, and retains its original behavior. The additive
`GetArrayLayoutForElement<T>` method takes an **element type**, such as `byte`, for NativeAOT-safe
array allocation.

## Reason for change

NativeAOT cannot generally generate arbitrary generic array or pointer implementations at runtime,
and trimming cannot preserve members for unknown custom models automatically. Explicit registrations
provide statically known types and the metadata needed for layout construction. The registered and
legacy paths share the same packing, alignment, and structure-offset calculations.

## Recommended action

### Register custom models explicitly

Add registration before requesting or reading the model's layout:

```csharp
Reader reader = pe.RelativeVirtualAddressReader;
reader.LayoutManager.RegisterTStruct<CustomHeader>();
CustomHeader header = reader.Read<CustomHeader>(rva);
```

The same approach applies to readers returned by `PDBFile.GetStream` and other format parsers.
Register base and nested structures separately. Register fixed-array element allocation with
`RegisterArray<T>` and concrete pointers with `RegisterPointer<TPointer, TTarget, TStorage>`.
Pass the appropriate enabled defines when registering models with `[If]` fields:

```csharp
reader.LayoutManager.RegisterTStruct<CustomOptionalHeader>(
    enabledDefines: new[] { "EXTENDED" });
```

### Opt into legacy discovery in untrimmed managed applications

For applications that do not use NativeAOT or trimming, add the discovery provider back:

```csharp
Reader reader = pe.RelativeVirtualAddressReader;
reader.LayoutManager.AddTStructTypes();
CustomHeader header = reader.Read<CustomHeader>(rva);
```

Pass any required conditional-field defines to `AddTStructTypes`. Add `AddPointerTypes()` if custom
models require reflection-based pointer discovery. This opt-in is not an AOT-safe workaround;
use explicit registrations for NativeAOT applications.

### Rebuild consumers and review assembly binding

The minor version also flows into the generated assembly identity through the existing build
versioning scheme. Rebuild applications and dependent libraries against the selected 1.1 package.
.NET Framework applications using a strong-named assembly may need binding redirects when
updating the assembly version.

## Affected APIs

The discovery change affects managers configured by these existing setup helpers:

- `AddPETypes`
- `AddELFTypes`
- `AddMachTypes`
- `AddMachFatHeaderTypes`
- `AddCrashDumpTypes`

It also affects custom-model reads through readers exposed by PE, ELF, Mach-O, minidump, and
PDB/MSFZ parsers, including `PEFile.RelativeVirtualAddressReader` and `PDBFile.GetStream`.
The new `AddPDBTypes` helper registers PDB built-ins but does not enable arbitrary discovery.

New analyzer annotations affect all overloads of `AddTStructTypes` and `AddReflectionTypes`,
`AddPointerTypes`, both legacy `GetArrayLayout` overloads, `Reader(IAddressSpace, bool)`, and the
legacy `UInt32PointerLayout`, `UInt64PointerLayout`, and `SizeTPointerLayout` classes.
`TLayout` additionally requires its runtime type argument to preserve a public parameterless
constructor when trimming.

## Release coordination

The repository has a `breaking change` label but no repository-local breaking-change process
document. Use the published [.NET breaking-change process](https://github.com/dotnet/runtime/blob/main/docs/project/breaking-change-process.md)
as the coordination reference:

1. Create or link a compatibility issue in `dotnet/diagnostics` containing the motivation,
   old and new behavior, affected versions and APIs, expected failures, and migration guidance.
   Apply the repository's `breaking change` label and reference the issue from the implementation PR.
2. Share the issue with affected stakeholders and `@dotnet/compat` for feedback before merge.
   Keep the implementation PR labeled as a breaking change.
3. After merge, coordinate a documentation issue using the
   [current dotnet/docs breaking-change template](https://github.com/dotnet/docs/issues/new?template=02-breaking-change.yml).
   Select **Other** for the version and specify the exact published `Microsoft.FileFormats` package
   version. Follow the template's notification instructions.
4. Close the compatibility issue after the change is merged, preferably after the package is
   publicly available. Update this notice with the first published version.

These steps do not constitute compatibility approval. Reviewers should consider the collected
feedback when deciding whether to merge and release the change.
