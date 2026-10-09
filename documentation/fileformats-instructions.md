# Microsoft.FileFormats and NativeAOT

Microsoft.FileFormats uses `LayoutManager` to compute target-dependent binary layouts and `Reader` to parse them.
NativeAOT support extends these existing types; it does not introduce a new binary format or parsing engine.
The package includes a modern .NET target with trimming and AOT annotations, alongside the existing .NET Framework
and .NET Standard targets.

## Built-in formats

The high-level PE, ELF, Mach-O, minidump, and PDB readers use registered layouts internally.
PDB reading supports both MSF and compressed MSFZ containers.

For direct access to built-in structure layouts, configure a manager using `AddPETypes`,
`AddELFTypes`, `AddMachTypes`, `AddMachFatHeaderTypes`,
`AddCrashDumpTypes`, or `AddPDBTypes`. Supply the input's endianness and word size
where required. These helpers also configure the primitive and dependent layouts needed by their format.

## Custom structures

Register each concrete structure. Its declared instance fields are parsed in **metadata order**:

```csharp
public class Header : TStruct
{
    public ushort Flags;
    public uint Length;
}

LayoutManager layouts = new LayoutManager()
    .AddPrimitives()
    .RegisterTStruct<Header>();

Reader reader = new(addressSpace, layouts);
Header header = reader.Read<Header>(0);
```

On modern .NET, reflection provides deterministic metadata order without accessing metadata tokens,
including under NativeAOT. Older targets sort by metadata token to preserve the previous ordering.
The existing alignment, `[Pack]`, `[If]`, and `[ArraySize]` rules
still determine offsets and size. Registration preserves field metadata and the public parameterless
constructor needed to read the type. Instances are constructed during reads, not registration.

Private and conditional instance fields are included automatically. Register the base type separately
when using inheritance. Register nested structures separately as well. Abstract base layouts are supported,
but an abstract structure cannot itself be read as an instance.

For conditional fields, supply the enabled defines:

```csharp
layouts.RegisterTStruct<OptionalHeader>(enabledDefines: new[] { "EXTENDED" });
```

Registration snapshots field order and enabled defines. Layouts are constructed lazily and cached, so dependent registrations
may follow their consumers as long as they are available before the layout is requested.
Missing dependencies produce `LayoutException`; duplicate type registrations are rejected.
Explicit registrations take precedence over general layout providers.

## Arrays and pointers

For fixed array fields, keep `[ArraySize]` on the field and register allocation for its element type:

```csharp
layouts.RegisterArray<Header>();
```

An array's element layout must also be available. `Reader.ReadArray<T>` and
`LayoutManager.GetArrayLayout<T>` allocate `T[]` directly and do not require
`RegisterArray<T>` merely to read a standalone array. These methods take an **element type**;
the non-generic `GetArrayLayout(Type, uint)` takes an **array type** and retains dynamic allocation.
The generic method uses `ArrayLayout<T>` with typed allocation and element assignment; the
non-generic method uses the legacy `ArrayLayout`.
Existing generic calls must change from, for example, `GetArrayLayout<byte[]>(count)` to
`GetArrayLayout<byte>(count)`.

Register the actual pointer class, target type, and storage type:

```csharp
public class HeaderPointer : Pointer<Header, SizeT> { }

layouts.AddSizeT(8)
    .RegisterPointer<HeaderPointer, Header, SizeT>();
```

Pointer storage must be `uint`, `ulong`, or `SizeT`. A registered pointer class needs a public
parameterless constructor. Pointer targets are resolved lazily, allowing recursive structures
connected through pointers. Reading a pointer preserves its concrete derived class; its target
layout must be available when it is read.

## Compatibility

The original reflection-discovery APIs remain available, including `AddTStructTypes`,
`AddPointerTypes`, the non-generic `GetArrayLayout(Type, uint)`, and
`Reader(IAddressSpace, bool)`. They are marked `RequiresDynamicCode` and
`RequiresUnreferencedCode` because metadata-token field ordering, dynamic array allocation,
and unregistered constructors cannot be guaranteed under NativeAOT or trimming.

The legacy `UInt32PointerLayout`, `UInt64PointerLayout`, and `SizeTPointerLayout` classes carry the same
annotations. Their reflection-based construction is retained for compatibility; use `RegisterPointer`
for the NativeAOT path.

For NativeAOT, use the registered APIs and `Reader(IAddressSpace, LayoutManager)`.
No source generator or warning suppression is required.

The existing built-in format setup helpers keep their names and signatures, but now register their
known structures instead of enabling general reflection discovery. They are NativeAOT-compatible
and do not require dynamic-code or trimming annotations.

### Extending readers returned by format parsers

Readers exposed by format parsers, such as `PEFile.RelativeVirtualAddressReader` and `PDBFile.GetStream`,
now contain registered built-in layouts instead of automatically discovering arbitrary custom models.
Custom parsing remains supported: register your models on the returned reader's existing manager.

```csharp
Reader reader = pe.RelativeVirtualAddressReader;
reader.LayoutManager.RegisterTStruct<Header>();
Header header = reader.Read<Header>(rva);
```

This is a migration requirement for callers that previously relied on implicit custom-model discovery,
including callers configuring their own managers with the built-in format setup helpers.
For untrimmed JIT applications, `reader.LayoutManager.AddTStructTypes()` opts back into legacy
structure discovery, and `AddPointerTypes()` opts into legacy pointer discovery when needed.
Pass any required conditional-field defines to `AddTStructTypes`. Explicit registrations remain preferred
and are required for the NativeAOT path. Existing `AddLayout` and `AddLayoutProvider` extension points
remain available; custom providers must themselves be compatible with trimming and NativeAOT.

## NativeAOT regression executable

The standalone executable shares custom-model scenarios with the unit tests and additionally parses
real PE, ELF32/ELF64, Mach-O, minidump32/minidump64, PDB, and MSFZ fixtures.
Managed regression tests run the same custom-model checks through explicit registration and legacy
reflection discovery, comparing values and layout metadata across endianness, pointer sizes, and
conditional-field configurations. They also cover mixed registered/reflection dependencies and
standalone custom-model arrays. The native executable runs the registered custom-model checks for
both endiannesses and pointer sizes, with conditional fields enabled and disabled.
It also constructs and reads every registered built-in layout across the supported word-size and
endianness configurations, including layouts not reached by those fixtures.
It is intentionally separate from the normal test traversal because publishing requires a native toolchain.

For Windows x64, from the repository root:

```powershell
$env:PATH = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer;' + $env:PATH
dotnet publish .\src\tests\Microsoft.FileFormats.UnitTests\NativeAot\FileFormats.NativeAot.csproj -r win-x64 -c Release
& .\artifacts\bin\FileFormats.NativeAot\Release\net8.0\win-x64\publish\FileFormats.NativeAot.exe
```

Use the repository's minimum .NET target in the output path if it changes, and choose the appropriate
runtime identifier and native toolchain when testing on another platform. The executable throws on
a failed scenario and reports success only after all scenarios finish.
