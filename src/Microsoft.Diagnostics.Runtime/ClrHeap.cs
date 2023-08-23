// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// A representation of the CLR heap.
    /// </summary>
    public sealed class ClrHeap : IClrHeap
    {
        private const int EnumerateBufferSize = 0x10000;
        private const int MaxGen2ObjectSize = 85000;

        private readonly IClrTypeFactory _typeFactory;
        private readonly IMemoryReader _memoryReader;
        private volatile Dictionary<ulong, ulong>? _allocationContexts;
        private volatile (ulong Source, ulong Target)[]? _dependentHandles;
        private volatile SyncBlockContainer? _syncBlocks;
        private volatile ClrSegment? _currSegment;
        private volatile SubHeapData? _subHeapData;
        private volatile ArrayPool<ObjectCorruption>? _objectCorruptionPool;
        private ulong _lastComFlags;

        internal ClrHeap(ClrRuntime runtime, IMemoryReader memoryReader, IClrHeapHelpers helpers)
        {
            Runtime = runtime;
            _memoryReader = memoryReader;
            Helpers = helpers;

            _typeFactory = helpers.CreateTypeFactory(this);
            FreeType = _typeFactory.FreeType;
            ObjectType = _typeFactory.ObjectType;
            StringType = _typeFactory.StringType;
            ExceptionType = _typeFactory.ExceptionType;
        }

        internal IClrHeapHelpers Helpers { get; }

        /// <summary>
        /// Gets the runtime associated with this heap.
        /// </summary>
        public ClrRuntime Runtime { get; }

        /// <summary>
        /// Returns true if the GC heap is in a consistent state for heap enumeration.  This will return false
        /// if the process was stopped in the middle of a GC, which can cause the GC heap to be unwalkable.
        /// Note, you may still attempt to walk the heap if this function returns false, but you will likely
        /// only be able to partially walk each segment.
        /// </summary>
        public bool CanWalkHeap => Helpers.AreGCStructuresValid;

        /// <summary>
        /// Returns the number of logical heaps in the process.
        /// </summary>
        public ImmutableArray<ClrSubHeap> SubHeaps => GetSubHeapData().SubHeaps;

        /// <summary>
        /// A heap is has a list of contiguous memory regions called segments.  This list is returned in order of
        /// of increasing object addresses.
        /// </summary>
        public ImmutableArray<ClrSegment> Segments => GetSubHeapData().Segments;

        /// <summary>
        /// Gets the <see cref="ClrType"/> representing free space on the GC heap.
        /// </summary>
        public ClrType FreeType { get; }

        /// <summary>
        /// Gets the <see cref="ClrType"/> representing <see cref="string"/>.
        /// </summary>
        public ClrType StringType { get; }

        /// <summary>
        /// Gets the <see cref="ClrType"/> representing <see cref="object"/>.
        /// </summary>
        public ClrType ObjectType { get; }

        /// <summary>
        /// Gets the <see cref="ClrType"/> representing <see cref="System.Exception"/>.
        /// </summary>
        public ClrType ExceptionType { get; }

        /// <summary>
        /// Gets a value indicating whether the GC heap is in Server mode.
        /// </summary>
        public bool IsServer => Helpers.IsServerMode;

        IClrType IClrHeap.ExceptionType => ExceptionType;

        IClrType IClrHeap.FreeType => FreeType;

        IClrType IClrHeap.ObjectType => ObjectType;

        IClrRuntime IClrHeap.Runtime => Runtime;

        ImmutableArray<IClrSegment> IClrHeap.Segments => Segments.CastArray<IClrSegment>();

        IClrType IClrHeap.StringType => StringType;

        ImmutableArray<IClrSubHeap> IClrHeap.SubHeaps => SubHeaps.CastArray<IClrSubHeap>();

        /// <summary>
        /// Gets a <see cref="ClrObject"/> for the given address on this heap.
        /// </summary>
        /// <remarks>
        /// The returned object will have a <see langword="null"/> <see cref="ClrObject.Type"/> if objRef does not point to
        /// a valid managed object.
        /// </remarks>
        /// <param name="objRef"></param>
        /// <returns></returns>
        public ClrObject GetObject(ulong objRef) => new(objRef, GetObjectType(objRef));

        /// <summary>
        /// Obtains the type of an object at the given address.  Returns <see langword="null"/> if objRef does not point to
        /// a valid managed object.
        /// </summary>
        public ClrType? GetObjectType(ulong objRef)
        {
            ulong mt = _memoryReader.ReadPointer(objRef);

            if (mt == 0)
                return null;

            return _typeFactory.GetOrCreateType(mt, objRef);
        }

        /// <summary>
        /// Enumerates all objects on the heap.
        /// </summary>
        /// <returns>An enumerator for all objects on the heap.</returns>
        public IEnumerable<ClrObject> EnumerateObjects() => EnumerateObjects(carefully: false);

        /// <summary>
        /// Enumerates all objects on the heap.
        /// </summary>
        /// <param name="carefully">Whether to continue walking objects on a segment where we've encountered
        /// a region of unwalkable memory.  Note that setting carefully = true may significantly increase the
        /// amount of time it takes to walk the heap if we encounter an error.</param>
        /// <returns>An enumerator for all objects on the heap.</returns>
        public IEnumerable<ClrObject> EnumerateObjects(bool carefully)
        {
            foreach (ClrSegment segment in Segments)
                foreach (ClrObject obj in EnumerateObjects(segment, carefully))
                    yield return obj;
        }

        /// <summary>
        /// Enumerates objects within the given memory range.
        /// </summary>
        public IEnumerable<ClrObject> EnumerateObjects(MemoryRange range, bool carefully = false)
        {
            foreach (ClrSegment seg in Segments)
            {
                if (!range.Overlaps(seg.ObjectRange))
                    continue;

                ulong start = seg.FirstObjectAddress;
                if (seg.ObjectRange.Contains(range.Start))
                    start = range.Start;

                foreach (ClrObject obj in EnumerateObjects(seg, start, carefully))
                {
                    if (obj < range.Start)
                        continue;

                    if (obj < range.End)
                        yield return obj;
                    else
                        break;
                }
            }
        }

        internal IEnumerable<ClrObject> EnumerateObjects(ClrSegment segment, bool carefully)
        {
            return EnumerateObjects(segment, segment.FirstObjectAddress, carefully);
        }

        /// <summary>
        /// Deeply verifies an object on the heap.  This goes beyond just ClrObject.IsValid and will
        /// check the object's references as well as certain internal CLR data structures.  Please note,
        /// however, that it is possible to pause a process in a debugger at a point where the heap is
        /// NOT corrupted, but does look inconsistent to ClrMD.  For example, the GC might allocate
        /// an array by writing a method table but the process might be paused before it had the chance
        /// to write the array length onto the heap.  In this case, IsObjectCorrupted may return true
        /// even if the process would have continued on fine.  As a result, this function acts more
        /// like a warning signal that more investigation is needed, and not proof-positive that there
        /// is heap corruption.
        /// </summary>
        /// <param name="objAddr">The address of the object to deeply verify.</param>
        /// <param name="result">Only non-null if this function returns true.  An object which describes the
        /// kind of corruption found.</param>
        /// <returns>True if the object is corrupted in some way, false otherwise.</returns>
        public bool IsObjectCorrupted(ulong objAddr, [NotNullWhen(true)] out ObjectCorruption? result)
        {
            ClrObject obj = GetObject(objAddr);
            ClrSegment? seg = GetSegmentByAddress(objAddr);
            if (seg is null || !seg.ObjectRange.Contains(objAddr))
            {
                result = new(obj, 0, ObjectCorruptionKind.ObjectNotOnTheHeap);
                return true;
            }

            ObjectCorruption[] array = RentObjectCorruptionArray();
            int count = Helpers.VerifyObject(GetSyncBlocks(), seg, obj, array.AsSpan(0, 1));
            result = count > 0 ? array[0] : null;

            ReturnObjectCorruptionArray(array);
            return count > 0;
        }

        /// <summary>
        /// Deeply verifies an object on the heap.  This goes beyond just ClrObject.IsValid and will
        /// check the object's references as well as certain internal CLR data structures.  Please note,
        /// however, that it is possible to pause a process in a debugger at a point where the heap is
        /// NOT corrupted, but does look inconsistent to ClrMD.  For example, the GC might allocate
        /// an array by writing a method table but the process might be paused before it had the chance
        /// to write the array length onto the heap.  In this case, IsObjectCorrupted may return true
        /// even if the process would have continued on fine.  As a result, this function acts more
        /// like a warning signal that more investigation is needed, and not proof-positive that there
        /// is heap corruption.
        /// </summary>
        /// <param name="objAddr">The address of the object to deeply verify.</param>
        /// <param name="detectedCorruption">An enumeration of all issues detected with this object.</param>
        /// <returns>True if the object is valid and fully verified, returns false if object corruption
        /// was detected.</returns>
        public bool FullyVerifyObject(ulong objAddr, out IEnumerable<ObjectCorruption> detectedCorruption)
        {
            ClrSegment? seg = GetSegmentByAddress(objAddr);
            ClrObject obj = GetObject(objAddr);
            if (seg is null || !seg.ObjectRange.Contains(objAddr))
            {
                detectedCorruption = new ObjectCorruption[] { new(obj, 0, ObjectCorruptionKind.ObjectNotOnTheHeap) };
                return false;
            }

            ObjectCorruption[] result = RentObjectCorruptionArray();
            int count = Helpers.VerifyObject(GetSyncBlocks(), seg, obj, result);
            if (count == 0)
            {
                ReturnObjectCorruptionArray(result);
                detectedCorruption = Enumerable.Empty<ObjectCorruption>();
                return true;
            }

            detectedCorruption = result.Take(count);
            return false;
        }

        private void ReturnObjectCorruptionArray(ObjectCorruption[] result)
        {
            _objectCorruptionPool ??= ArrayPool<ObjectCorruption>.Create(64, 4);
            _objectCorruptionPool.Return(result);
        }

        private ObjectCorruption[] RentObjectCorruptionArray()
        {
            _objectCorruptionPool ??= ArrayPool<ObjectCorruption>.Create(64, 4);
            ObjectCorruption[] result = _objectCorruptionPool.Rent(64);
            return result;
        }

        bool IClrHeap.IsObjectCorrupted(ulong obj, [NotNullWhen(true)] out IObjectCorruption? result)
        {
            ObjectCorruption? corruption;
            bool r = IsObjectCorrupted(obj, out corruption);
            result = corruption;
            return r;
        }

        bool IClrHeap.IsObjectCorrupted(IClrValue obj, [NotNullWhen(true)] out IObjectCorruption? result)
        {
            ObjectCorruption? corruption;
            bool r = IsObjectCorrupted(obj.Address, out corruption);
            result = corruption;
            return r;
        }

        /// <summary>
        /// Verifies the GC Heap and returns an enumerator for any corrupted objects it finds.
        /// </summary>
        public IEnumerable<ObjectCorruption> VerifyHeap() => VerifyHeap(EnumerateObjects(carefully: true));

        IEnumerable<IObjectCorruption> IClrHeap.VerifyHeap() => VerifyHeap().Cast<IObjectCorruption>();

        /// <summary>
        /// Verifies the given objects and returns an enumerator for any corrupted objects it finds.
        /// </summary>
        public IEnumerable<ObjectCorruption> VerifyHeap(IEnumerable<ClrObject> objects)
        {
            foreach (ClrObject obj in objects)
                if (IsObjectCorrupted(obj, out ObjectCorruption? result))
                    yield return result;
        }

        IEnumerable<IObjectCorruption> IClrHeap.VerifyHeap(IEnumerable<IClrValue> objects)
        {
            foreach (IClrValue obj in objects)
                if (IsObjectCorrupted(obj.Address, out ObjectCorruption? result))
                    yield return result;
        }

        internal IEnumerable<ClrObject> EnumerateObjects(ClrSegment segment, ulong startAddress, bool carefully)
        {
            if (!segment.ObjectRange.Contains(startAddress))
                yield break;

            uint pointerSize = (uint)_memoryReader.PointerSize;
            uint minObjSize = pointerSize * 3;
            uint objSkip = segment.Kind != GCSegmentKind.Large ? minObjSize : 85000;
            using MemoryCache cache = new(_memoryReader, segment);

            ulong obj = GetValidObjectForAddress(segment, startAddress);
            while (segment.ObjectRange.Contains(obj))
            {
                if (!cache.ReadPointer(obj, out ulong mt))
                {
                    if (!carefully)
                        break;

                    obj = FindNextValidObject(segment, pointerSize + objSkip, obj, cache);
                    continue;
                }

                ClrType? type = _typeFactory.GetOrCreateType(mt, obj);
                ClrObject result = new(obj, type);
                yield return result;
                if (type is null)
                {
                    if (!carefully)
                        break;

                    obj = FindNextValidObject(segment, pointerSize, obj + objSkip, cache);
                    continue;
                }

                SetMarkerIndex(segment, obj);

                ulong size;
                if (type.ComponentSize == 0)
                {
                    size = (uint)type.StaticSize;
                }
                else
                {
                    if (!cache.ReadUInt32(obj + pointerSize, out uint count))
                    {
                        if (!carefully)
                            break;

                        obj = FindNextValidObject(segment, pointerSize, obj + objSkip, cache);
                        continue;
                    }

                    // Strings in v4+ contain a trailing null terminator not accounted for.
                    if (StringType == type)
                        count++;

                    size = count * (ulong)type.ComponentSize + (ulong)type.StaticSize;
                }

                size = Align(size, segment);
                if (size < minObjSize)
                    size = minObjSize;

                obj += size;
                obj = SkipAllocationContext(segment, obj);
            }
        }

        private static void SetMarkerIndex(ClrSegment segment, ulong obj)
        {
            ulong segmentOffset = obj - segment.ObjectRange.Start;
            int index = GetMarkerIndex(segment, obj);
            if (index != -1 && index < segment.ObjectMarkers.Length && segment.ObjectMarkers[index] == 0 && segmentOffset <= uint.MaxValue)
                segment.ObjectMarkers[index] = (uint)segmentOffset;
        }

        private static int GetMarkerIndex(ClrSegment segment, ulong startAddress)
        {
            if (segment.ObjectMarkers.Length == 0)
                return -1;

            ulong markerStep = segment.ObjectRange.Length / ((uint)segment.ObjectMarkers.Length + 2);
            int result = (int)((startAddress - segment.FirstObjectAddress) / markerStep);
            if (result >= segment.ObjectMarkers.Length)
                result = segment.ObjectMarkers.Length - 1;

            return result;
        }

        private static ulong GetValidObjectForAddress(ClrSegment segment, ulong address, bool previous = false)
        {
            if (address == segment.FirstObjectAddress || segment.ObjectMarkers.Length == 0)
                return segment.FirstObjectAddress;

            int index = GetMarkerIndex(segment, address);
            if (index >= segment.ObjectMarkers.Length)
                index = segment.ObjectMarkers.Length - 1;

            for (; index >= 0; index--)
            {
                uint marker = segment.ObjectMarkers[index];
                if (marker == 0)
                    continue;

                ulong validObject = segment.FirstObjectAddress + marker;
                if (previous)
                {
                    if (validObject < address)
                        return validObject;
                }
                else
                {
                    if (validObject <= address)
                        return validObject;
                }
            }

            return segment.FirstObjectAddress;
        }

        private class MemoryCache : IDisposable
        {
            private readonly IMemoryReader _memoryReader;
            private readonly int _pointerSize;
            private readonly uint _requiredSize;
            private readonly byte[]? _cache;

            public MemoryCache(IMemoryReader reader, ClrSegment segment)
            {
                _memoryReader = reader;
                _pointerSize = reader.PointerSize;
                _requiredSize = (uint)_pointerSize * 3;
                if (segment.Kind != GCSegmentKind.Large)
                    _cache = ArrayPool<byte>.Shared.Rent(EnumerateBufferSize);
            }

            public void Dispose()
            {
                if (_cache is not null)
                    ArrayPool<byte>.Shared.Return(_cache);
            }


            public ulong Base { get; private set; }
            public int Length { get; private set; }

            public bool ReadPointer(ulong address, out ulong value)
            {
                if (!EnsureInCache(address))
                    return _memoryReader.ReadPointer(address, out value);

                int offset = (int)(address - Base);
                value = _cache.AsSpan().AsPointer(offset);
                return true;
            }

            public bool ReadUInt32(ulong address, out uint value)
            {
                if (!EnsureInCache(address))
                    return _memoryReader.Read(address, out value);

                int offset = (int)(address - Base);
                value = _cache.AsSpan().AsUInt32(offset);
                return true;
            }


            private bool EnsureInCache(ulong address)
            {
                if (_cache is null)
                    return false;

                ulong end = Base + (uint)Length;
                if (Base <= address && address + _requiredSize < end)
                    return true;

                Base = address;
                Length = _memoryReader.Read(address, _cache);
                return Length >= _requiredSize;
            }
        }

        private ulong FindNextValidObject(ClrSegment segment, uint pointerSize, ulong address, MemoryCache cache)
        {
            ulong obj = address;
            while (segment.ObjectRange.Contains(obj))
            {
                ulong ctxObj = SkipAllocationContext(segment, obj);
                if (obj < ctxObj)
                {
                    obj = ctxObj;
                    continue;
                }

                obj += pointerSize;

                if (!cache.ReadPointer(obj, out ulong mt))
                    return 0;

                if (mt > 0x1000)
                {
                    if (Helpers.IsValidMethodTable(mt))
                        break;
                }
            }

            return obj;
        }


        /// <summary>
        /// Finds the next ClrObject on the given segment.
        /// </summary>
        /// <param name="address">An address on any ClrSegment.</param>
        /// <param name="carefully">Whether to continue walking objects on a segment where we've encountered
        /// a region of unwalkable memory.  Note that setting carefully = true may significantly increase the
        /// amount of time it takes to walk the heap if we encounter an error.</param>
        /// <returns>An invalid ClrObject if address doesn't lie on any segment or if no objects exist after the given address on a segment.</returns>
        public ClrObject FindNextObjectOnSegment(ulong address, bool carefully = false)
        {
            ClrSegment? seg = GetSegmentByAddress(address);
            if (seg is null)
                return default;

            foreach (ClrObject obj in EnumerateObjects(seg, address, carefully))
                if (address < obj)
                    return obj;

            return default;
        }

        /// <summary>
        /// Finds the previous object on the given segment.
        /// </summary>
        /// <param name="address">An address on any ClrSegment.</param>
        /// <param name="carefully">Whether to continue walking objects on a segment where we've encountered
        /// a region of unwalkable memory.  Note that setting carefully = true may significantly increase the
        /// amount of time it takes to walk the heap if we encounter an error.</param>
        /// <returns>An enumerator for all objects on the heap.</returns>
        /// <returns>An invalid ClrObject if address doesn't lie on any segment or if address is the first object on a segment.</returns>
        public ClrObject FindPreviousObjectOnSegment(ulong address, bool carefully = false)
        {
            ClrSegment? seg = GetSegmentByAddress(address);
            if (seg is null || address <= seg.FirstObjectAddress)
                return default;

            ulong start = GetValidObjectForAddress(seg, address, previous: true);
            DebugOnly.Assert(start < address);

            ClrObject last = default;
            foreach (ClrObject obj in EnumerateObjects(seg, start, carefully))
            {
                if (obj >= address)
                    return last;

                last = obj;
            }

            if (last < address)
                return last;

            return default;
        }

        private ulong SkipAllocationContext(ClrSegment seg, ulong address)
        {
            if (seg.Kind is GCSegmentKind.Large or GCSegmentKind.Frozen)
                return address;

            Dictionary<ulong, ulong> allocationContexts = GetAllocationContexts();

            uint minObjSize = (uint)IntPtr.Size * 3;
            while (allocationContexts.TryGetValue(address, out ulong nextObj))
            {
                nextObj += Align(minObjSize, seg);

                if (address >= nextObj || address >= seg.End)
                    return 0;

                // Only if there's data corruption:
                if (address >= nextObj || address >= seg.End)
                    return 0;

                address = nextObj;
            }

            return address;
        }

        private static ulong Align(ulong size, ClrSegment seg)
        {
            ulong AlignConst;
            ulong AlignLargeConst = 7;

            if (IntPtr.Size == 4)
                AlignConst = 3;
            else
                AlignConst = 7;

            if (seg.Kind is GCSegmentKind.Large or GCSegmentKind.Pinned)
                return (size + AlignLargeConst) & ~AlignLargeConst;

            return (size + AlignConst) & ~AlignConst;
        }

        IEnumerable<IClrRoot> IClrHeap.EnumerateRoots() => EnumerateRoots().Cast<IClrRoot>();

        /// <summary>
        /// Enumerates all roots in the process.  Equivalent to the combination of:
        ///     ClrRuntime.EnumerateHandles().Where(handle => handle.IsStrong)
        ///     ClrRuntime.EnumerateThreads().SelectMany(thread => thread.EnumerateStackRoots())
        ///     ClrHeap.EnumerateFinalizerRoots()
        /// </summary>
        public IEnumerable<ClrRoot> EnumerateRoots()
        {
            // Handle table
            foreach (ClrHandle handle in Runtime.EnumerateHandles())
            {
                if (handle.IsStrong)
                    yield return handle;

                if (handle.RootKind == ClrRootKind.AsyncPinnedHandle && handle.Object.IsValid)
                {
                    (ulong address, ClrObject m_userObject) = GetObjectAndAddress(handle.Object, "m_userObject");

                    if (address != 0 && m_userObject.IsValid)
                    {
                        yield return new ClrHandle(handle.AppDomain, address, m_userObject, handle.HandleKind);

                        ClrElementType? arrayElementType = m_userObject.Type?.ComponentType?.ElementType;
                        if (m_userObject.IsArray && arrayElementType.HasValue && arrayElementType.Value.IsObjectReference())
                        {
                            ClrArray array = m_userObject.AsArray();
                            for (int i = 0; i < array.Length; i++)
                            {
                                ulong innerAddress = m_userObject + (ulong)(2 * IntPtr.Size + i * IntPtr.Size);
                                ClrObject innerObj = array.GetObjectValue(i);

                                if (innerObj.IsValid)
                                    yield return new ClrHandle(handle.AppDomain, innerAddress, innerObj, handle.HandleKind);
                            }
                        }
                    }
                }
            }

            // Finalization Queue
            foreach (ClrRoot root in EnumerateFinalizerRoots())
                yield return root;

            // Threads
            foreach (ClrThread thread in Runtime.Threads.Where(t => t.IsAlive))
                foreach (ClrRoot root in thread.EnumerateStackRoots())
                    yield return root;
        }

        private (ulong Address, ClrObject obj) GetObjectAndAddress(ClrObject containing, string fieldName)
        {
            if (containing.IsValid)
            {
                ClrInstanceField? field = containing.Type?.Fields.FirstOrDefault(f => f.Name == fieldName);
                if (field != null && field.Offset > 0)
                {
                    ulong address = field.GetAddress(containing.Address);
                    ulong objPtr = _memoryReader.ReadPointer(address);
                    ClrObject obj = GetObject(objPtr);

                    if (obj.IsValid)
                        return (address, obj);
                }
            }

            return (0ul, default);
        }

        /// <summary>
        /// Returns the GC segment which contains the given address.  This only searches ClrSegment.ObjectRange.
        /// </summary>
        public ClrSegment? GetSegmentByAddress(ulong address)
        {
            if (Segments.Length == 0)
                return null;

            ClrSegment? curr = _currSegment;
            if (curr is not null && curr.ObjectRange.Contains(address))
                return curr;

            if (Segments[0].FirstObjectAddress <= address && address < Segments[Segments.Length - 1].End)
            {
                int index = Segments.Search(address, (seg, value) => seg.ObjectRange.CompareTo(value));
                if (index == -1)
                    return null;

                curr = Segments[index];
                _currSegment = curr;
                return curr;
            }

            return null;
        }

        /// <summary>
        /// Enumerates all finalizable objects on the heap.
        /// </summary>
        public IEnumerable<ClrObject> EnumerateFinalizableObjects() => EnumerateFinalizers(SubHeaps.Select(heap => heap.FinalizerQueueObjects)).Select(f => f.Object);

        /// <summary>
        /// Enumerates all finalizable objects on the heap.
        /// </summary>
        public IEnumerable<ClrRoot> EnumerateFinalizerRoots() => EnumerateFinalizers(SubHeaps.Select(heap => heap.FinalizerQueueRoots));
        IEnumerable<IClrRoot> IClrHeap.EnumerateFinalizerRoots() => EnumerateFinalizerRoots().Cast<IClrRoot>();

        /// <summary>
        /// Enumerates all AllocationContexts for all segments.  Allocation contexts are locations on the GC
        /// heap which the GC uses to allocate new objects.  These regions of memory do not contain objects.
        /// AllocationContexts are the reason that you cannot simply enumerate the heap by adding each object's
        /// size to itself to get the next object on the segment, since if the address is an allocation context
        /// you will have to skip past it to find the next valid object.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<MemoryRange> EnumerateAllocationContexts()
        {
            Dictionary<ulong, ulong>? allocationContexts = GetAllocationContexts();
            if (allocationContexts is not null)
                foreach (KeyValuePair<ulong, ulong> kv in allocationContexts)
                    yield return new(kv.Key, kv.Value);
        }

        public IEnumerable<SyncBlock> EnumerateSyncBlocks() => GetSyncBlocks();

        internal SyncBlock? GetSyncBlock(ulong obj) => GetSyncBlocks().TryGetSyncBlock(obj);

        private SyncBlockContainer GetSyncBlocks()
        {
            SyncBlockContainer? container = _syncBlocks;
            if (container is null)
            {
                container = new SyncBlockContainer(Helpers.EnumerateSyncBlocks());
                Interlocked.CompareExchange(ref _syncBlocks, container, null);
            }

            return container;
        }

        internal ClrThinLock? GetThinlock(ulong address)
        {
            uint header = _memoryReader.Read<uint>(address - 4);
            if (header == 0)
                return null;

            return Helpers.GetThinLock(this, header);
        }

        /// <summary>
        /// Returns a string representation of this heap, including the size and number of segments.
        /// </summary>
        /// <returns>The string representation of this heap.</returns>
        public override string ToString()
        {
            long size = Segments.Sum(s => (long)s.Length);
            return $"GC Heap: {size.ConvertToHumanReadable()}, {Segments.Length} segments";
        }

        /// <summary>
        /// This is an implementation helper.  Use ClrObject.IsComCallWrapper and ClrObject.IsRuntimeCallWrapper instead.
        /// </summary>
        internal SyncBlockComFlags GetComFlags(ulong obj)
        {
            if (obj == 0)
                return SyncBlockComFlags.None;

            const ulong mask = ~0xe000000000000000;
            ulong lastComFlags = _lastComFlags;
            if ((lastComFlags & mask) == obj)
                return (SyncBlockComFlags)(lastComFlags >> 61);

            SyncBlock? syncBlk = GetSyncBlock(obj);
            SyncBlockComFlags flags = syncBlk?.ComFlags ?? SyncBlockComFlags.None;
            _lastComFlags = ((ulong)flags << 61) | (obj & mask);

            return flags;
        }

        /// <summary>
        /// This is an implementation helper.  Use ClrObject.Size instead.
        /// </summary>
        internal ulong GetObjectSize(ulong objRef, ClrType type)
        {
            ulong size;
            if (type.ComponentSize == 0)
            {
                size = (uint)type.StaticSize;
            }
            else
            {
                uint countOffset = (uint)IntPtr.Size;
                ulong loc = objRef + countOffset;

                uint count = _memoryReader.Read<uint>(loc);

                // Strings in v4+ contain a trailing null terminator not accounted for.
                if (StringType == type)
                    count++;

                size = count * (ulong)type.ComponentSize + (ulong)type.StaticSize;
            }

            uint minSize = (uint)IntPtr.Size * 3;
            if (size < minSize)
                size = minSize;
            return size;
        }

        /// <summary>
        /// This is an implementation helper.  Use <see cref="ClrObject.EnumerateReferences(bool, bool)">ClrObject.EnumerateReferences</see> instead.
        /// Enumerates all objects that the given object references.  This method is meant for internal use to
        /// implement ClrObject.EnumerateReferences, which you should use instead of calling this directly.
        /// </summary>
        /// <param name="obj">The object in question.</param>
        /// <param name="type">The type of the object.</param>
        /// <param name="considerDependantHandles">Whether to consider dependant handle mappings.</param>
        /// <param name="carefully">
        /// Whether to bounds check along the way (useful in cases where
        /// the heap may be in an inconsistent state.)
        /// </param>
        internal IEnumerable<ClrObject> EnumerateObjectReferences(ulong obj, ClrType type, bool carefully, bool considerDependantHandles)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));

            if (considerDependantHandles)
            {
                (ulong Source, ulong Target)[] dependent = GetDependentHandles();

                if (dependent.Length > 0)
                {
                    int index = dependent.Search(obj, (x, y) => x.Source.CompareTo(y));
                    if (index != -1)
                    {
                        while (index >= 1 && dependent[index - 1].Source == obj)
                            index--;

                        while (index < dependent.Length && dependent[index].Source == obj)
                        {
                            ulong dependantObj = dependent[index++].Target;
                            yield return new(dependantObj, GetObjectType(dependantObj));
                        }
                    }
                }
            }

            if (type.IsCollectible)
            {
                ulong la = _memoryReader.ReadPointer(type.LoaderAllocatorHandle);
                if (la != 0)
                    yield return new(la, GetObjectType(la));
            }

            if (type.ContainsPointers)
            {
                GCDesc gcdesc = type.GCDesc;
                if (!gcdesc.IsEmpty)
                {
                    ulong size = GetObjectSize(obj, type);
                    if (carefully)
                    {
                        ClrSegment? seg = GetSegmentByAddress(obj);
                        if (seg is null)
                            yield break;

                        bool large = seg.Kind is GCSegmentKind.Large or GCSegmentKind.Pinned;
                        if (obj + size > seg.End || (!large && size > MaxGen2ObjectSize))
                            yield break;
                    }

                    int intSize = (int)size;
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(intSize);
                    int read = _memoryReader.Read(obj, new Span<byte>(buffer, 0, intSize));
                    if (read > IntPtr.Size)
                    {
                        foreach ((ulong reference, int offset) in gcdesc.WalkObject(buffer, read))
                            yield return new(reference, GetObjectType(reference));
                    }

                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        /// <summary>
        /// This is an implementation helper.
        /// Enumerates all objects that the given object references.  This method is meant for internal use to
        /// implement ClrObject.EnumerateReferencesWithFields, which you should use instead of calling this directly.
        /// </summary>
        /// <param name="obj">The object in question.</param>
        /// <param name="type">The type of the object.</param>
        /// <param name="considerDependantHandles">Whether to consider dependant handle mappings.</param>
        /// <param name="carefully">
        /// Whether to bounds check along the way (useful in cases where
        /// the heap may be in an inconsistent state.)
        /// </param>
        internal IEnumerable<ClrReference> EnumerateReferencesWithFields(ulong obj, ClrType type, bool carefully, bool considerDependantHandles)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));

            if (considerDependantHandles)
            {
                (ulong Source, ulong Target)[] dependent = GetDependentHandles();

                if (dependent.Length > 0)
                {
                    int index = dependent.Search(obj, (x, y) => x.Source.CompareTo(y));
                    if (index != -1)
                    {
                        while (index >= 1 && dependent[index - 1].Source == obj)
                            index--;

                        while (index < dependent.Length && dependent[index].Source == obj)
                        {
                            ulong dependantObj = dependent[index++].Target;
                            ClrObject target = new(dependantObj, GetObjectType(dependantObj));
                            yield return ClrReference.CreateFromDependentHandle(target);
                        }
                    }
                }
            }

            if (type.ContainsPointers)
            {
                GCDesc gcdesc = type.GCDesc;
                if (!gcdesc.IsEmpty)
                {
                    ulong size = GetObjectSize(obj, type);
                    if (carefully)
                    {
                        ClrSegment? seg = GetSegmentByAddress(obj);
                        if (seg is null)
                            yield break;

                        bool large = seg.Kind is GCSegmentKind.Large or GCSegmentKind.Pinned;
                        if (obj + size > seg.End || (!large && size > MaxGen2ObjectSize))
                            yield break;
                    }

                    int intSize = (int)size;
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(intSize);
                    int read = _memoryReader.Read(obj, new Span<byte>(buffer, 0, intSize));
                    if (read > IntPtr.Size)
                    {
                        foreach ((ulong reference, int offset) in gcdesc.WalkObject(buffer, read))
                        {
                            ClrObject target = new(reference, GetObjectType(reference));

                            DebugOnly.Assert(offset >= IntPtr.Size);
                            yield return ClrReference.CreateFromFieldOrArray(target, type, offset - IntPtr.Size);
                        }
                    }
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        /// <summary>
        /// This is an implementation helper.
        /// Enumerates all objects that the given object references.  This method is meant for internal use to
        /// implement ClrObject.EnumerateReferenceAddresses which you should use instead of calling this directly.
        /// </summary>
        /// <param name="obj">The object in question.</param>
        /// <param name="type">The type of the object.</param>
        /// <param name="considerDependantHandles">Whether to consider dependant handle mappings.</param>
        /// <param name="carefully">
        /// Whether to bounds check along the way (useful in cases where
        /// the heap may be in an inconsistent state.)
        /// </param>
        internal IEnumerable<ulong> EnumerateReferenceAddresses(ulong obj, ClrType type, bool carefully, bool considerDependantHandles)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));

            if (considerDependantHandles)
            {
                (ulong Source, ulong Target)[] dependent = GetDependentHandles();

                if (dependent.Length > 0)
                {
                    int index = dependent.Search(obj, (x, y) => x.Source.CompareTo(y));
                    if (index != -1)
                    {
                        while (index >= 1 && dependent[index - 1].Source == obj)
                            index--;

                        while (index < dependent.Length && dependent[index].Source == obj)
                            yield return dependent[index++].Target;
                    }
                }
            }

            if (type.ContainsPointers)
            {
                GCDesc gcdesc = type.GCDesc;
                if (!gcdesc.IsEmpty)
                {
                    ulong size = GetObjectSize(obj, type);
                    if (carefully)
                    {
                        ClrSegment? seg = GetSegmentByAddress(obj);
                        if (seg is null)
                            yield break;

                        bool large = seg.Kind is GCSegmentKind.Large or GCSegmentKind.Pinned;
                        if (obj + size > seg.End || (!large && size > MaxGen2ObjectSize))
                            yield break;
                    }

                    int intSize = (int)size;
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(intSize);
                    int read = _memoryReader.Read(obj, new Span<byte>(buffer, 0, intSize));
                    if (read > IntPtr.Size)
                    {
                        foreach ((ulong reference, int offset) in gcdesc.WalkObject(buffer, read))
                        {
                            yield return reference;
                            DebugOnly.Assert(offset >= IntPtr.Size);
                        }
                    }
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        private Dictionary<ulong, ulong> GetAllocationContexts()
        {
            Dictionary<ulong, ulong>? result = _allocationContexts;
            if (result is not null)
                return result;

            result = new();
            foreach (MemoryRange allocContext in Helpers.EnumerateThreadAllocationContexts())
                result[allocContext.Start] = allocContext.End;

            foreach (ClrSubHeap subHeap in SubHeaps)
                if (subHeap.AllocationContext.Start < subHeap.AllocationContext.End)
                    result[subHeap.AllocationContext.Start] = subHeap.AllocationContext.End;

            Interlocked.CompareExchange(ref _allocationContexts, result, null);
            return result;
        }

        private (ulong Source, ulong Target)[] GetDependentHandles()
        {
            (ulong Source, ulong Target)[]? handles = _dependentHandles;
            if (handles is not null)
                return handles;

            handles = Helpers.EnumerateDependentHandles().OrderBy(r => r.Source).ToArray();

            Interlocked.CompareExchange(ref _dependentHandles, handles, null);
            return handles;
        }

        private IEnumerable<ClrRoot> EnumerateFinalizers(IEnumerable<MemoryRange> memoryRanges)
        {
            foreach (MemoryRange seg in memoryRanges)
            {
                for (ulong ptr = seg.Start; ptr < seg.End; ptr += (uint)IntPtr.Size)
                {
                    ulong obj = _memoryReader.ReadPointer(ptr);
                    if (obj == 0)
                        continue;

                    ulong mt = _memoryReader.ReadPointer(obj);
                    ClrType? type = _typeFactory.GetOrCreateType(mt, obj);
                    if (type != null)
                        yield return new ClrRoot(ptr, new ClrObject(obj, type), ClrRootKind.FinalizerQueue, isInterior: false, isPinned: false);
                }
            }
        }

        private SubHeapData GetSubHeapData()
        {
            SubHeapData? data = _subHeapData;
            if (data is not null)
                return data;

            data = new(Helpers.GetSubHeaps(this));
            Interlocked.CompareExchange(ref _subHeapData, data, null);
            return _subHeapData;
        }

        public ClrType? GetTypeByMethodTable(ulong methodTable) => _typeFactory.GetOrCreateType(methodTable, 0);

        public ClrType? GetTypeByName(string name) => Runtime.EnumerateModules().OrderBy(m => m.Name ?? "").Select(m => GetTypeByName(m, name)).Where(r => r != null).FirstOrDefault();

        public ClrType? GetTypeByName(ClrModule module, string name)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));

            if (name.Length == 0)
                throw new ArgumentException($"{nameof(name)} cannot be empty");

            return FindTypeName(module.EnumerateTypeDefToMethodTableMap(), name);
        }

        private ClrType? FindTypeName(IEnumerable<(ulong MethodTable, int Token)> map, string name)
        {
            // First, look for already constructed types and see if their name matches.
            List<ulong> lookup = new(256);
            foreach ((ulong mt, _) in map)
            {
                ClrType? type = _typeFactory.TryGetType(mt);
                if (type is null)
                    lookup.Add(mt);
                else if (type.Name == name)
                    return type;
            }

            // Since we didn't find pre-constructed types matching, look up the names for all
            // remaining types without constructing them until we find the right one.
            foreach (ulong mt in lookup)
            {
                string? typeName = _typeFactory.GetTypeName(mt);
                if (typeName == name)
                    return _typeFactory.GetOrCreateType(mt, 0);
            }

            return null;
        }

        internal ClrException? GetExceptionObject(ulong objAddress, ClrThread? thread)
        {
            if (objAddress == 0)
                return null;

            ClrObject obj = GetObject(objAddress);
            if (obj.IsValid && !obj.IsException)
                return null;
            return new ClrException(obj.Type?.Helpers ?? FreeType.Helpers, thread, obj);
        }

        IEnumerable<IClrValue> IClrHeap.EnumerateFinalizableObjects() => EnumerateFinalizableObjects().Cast<IClrValue>();

        IEnumerable<IClrValue> IClrHeap.EnumerateObjects(bool carefully) => EnumerateObjects(carefully).Cast<IClrValue>();

        IEnumerable<IClrValue> IClrHeap.EnumerateObjects() => EnumerateObjects().Cast<IClrValue>();

        IEnumerable<IClrValue> IClrHeap.EnumerateObjects(MemoryRange range, bool carefully) => EnumerateObjects(range, carefully).Cast<IClrValue>();

        IClrValue IClrHeap.FindNextObjectOnSegment(ulong address, bool carefully) => FindNextObjectOnSegment(address, carefully);

        IClrValue IClrHeap.FindPreviousObjectOnSegment(ulong address, bool carefully) => FindPreviousObjectOnSegment(address, carefully);

        IClrValue IClrHeap.GetObject(ulong objRef) => GetObject(objRef);

        IClrType? IClrHeap.GetObjectType(ulong objRef) => GetObjectType(objRef);

        IClrSegment? IClrHeap.GetSegmentByAddress(ulong address) => GetSegmentByAddress(address);

        IClrType? IClrHeap.GetTypeByMethodTable(ulong methodTable) => GetTypeByMethodTable(methodTable);

        IClrType? IClrHeap.GetTypeByName(string name) => GetTypeByName(name);

        IClrType? IClrHeap.GetTypeByName(IClrModule module, string name)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));

            if (name.Length == 0)
                throw new ArgumentException($"{nameof(name)} cannot be empty");

            return FindTypeName(module.EnumerateTypeDefToMethodTableMap(), name);
        }

        private sealed class SubHeapData
        {
            public ImmutableArray<ClrSubHeap> SubHeaps { get; }
            public ImmutableArray<ClrSegment> Segments { get; }

            public SubHeapData(ImmutableArray<ClrSubHeap> subheaps)
            {
                SubHeaps = subheaps;
                Segments = subheaps.SelectMany(s => s.Segments).OrderBy(s => s.FirstObjectAddress).ToImmutableArray();
            }
        }
    }
}
