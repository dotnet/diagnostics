// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrHeap
    {
        bool CanWalkHeap { get; }
        IClrType ExceptionType { get; }
        IClrType FreeType { get; }
        bool IsServer { get; }
        IClrType ObjectType { get; }
        IClrRuntime Runtime { get; }
        ImmutableArray<IClrSegment> Segments { get; }
        IClrType StringType { get; }
        ImmutableArray<IClrSubHeap> SubHeaps { get; }

        IEnumerable<MemoryRange> EnumerateAllocationContexts();
        IEnumerable<IClrValue> EnumerateFinalizableObjects();
        IEnumerable<IClrRoot> EnumerateFinalizerRoots();
        IEnumerable<IClrValue> EnumerateObjects();
        IEnumerable<IClrValue> EnumerateObjects(bool carefully);
        IEnumerable<IClrValue> EnumerateObjects(MemoryRange range, bool carefully = false);
        IEnumerable<IClrRoot> EnumerateRoots();
        IEnumerable<SyncBlock> EnumerateSyncBlocks();
        IClrValue FindNextObjectOnSegment(ulong address, bool carefully = false);
        IClrValue FindPreviousObjectOnSegment(ulong address, bool carefully = false);
        IClrValue GetObject(ulong objRef);
        IClrType? GetObjectType(ulong objRef);
        IClrSegment? GetSegmentByAddress(ulong address);
        IClrType? GetTypeByMethodTable(ulong methodTable);
        IClrType? GetTypeByName(IClrModule module, string name);
        IClrType? GetTypeByName(string name);
        bool IsObjectCorrupted(ulong objAddr, [NotNullWhen(true)] out IObjectCorruption? result);
        bool IsObjectCorrupted(IClrValue obj, [NotNullWhen(true)] out IObjectCorruption? result);
        IEnumerable<IObjectCorruption> VerifyHeap();
        IEnumerable<IObjectCorruption> VerifyHeap(IEnumerable<IClrValue> objects);
    }
}
