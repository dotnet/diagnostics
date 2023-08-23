// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Microsoft.Diagnostics.Runtime
{
    public sealed class GCRoot
    {
        private readonly ClrHeap _heap;
        private readonly HashSet<ulong> _seen = new();
        private readonly Dictionary<ulong, ChainLink> _found = new();
        private readonly Predicate<ClrObject>? _targetPredicate;

        public GCRoot(ClrHeap heap, IEnumerable<ulong> targets)
        {
            _heap = heap;
            foreach (ulong obj in targets)
            {
                _found[obj] = new() { Object = obj };
            }
        }

        public GCRoot(ClrHeap heap, Predicate<ClrObject> isTarget)
        {
            _heap = heap;
            _targetPredicate = isTarget;
        }

        public IEnumerable<(ClrRoot Root, ChainLink Path)> EnumerateRootPaths()
        {
            return EnumerateRootPaths(CancellationToken.None);
        }

        public IEnumerable<(ClrRoot Root, ChainLink Path)> EnumerateRootPaths(CancellationToken cancellation)
        {
            IEnumerable<ClrRoot> roots = _heap.EnumerateRoots();
            foreach (ClrRoot root in roots)
            {
                cancellation.ThrowIfCancellationRequested();
                ChainLink? path = FindPathFrom(root.Object, cancellation);
                if (path is not null)
                    yield return (root, path);
            }
        }

        private IEnumerable<ClrRoot> EnumerateRootsMultithreaded()
        {
            // TODO: Reenable when we track down the issue
            CancellationTokenSource source = new();
            try
            {
                BlockingCollection<ClrRoot> roots = new(4096);
                Thread t = new(() => WorkerThread(source.Token, roots));
                t.Start();

                foreach (ClrRoot root in roots)
                    yield return root;
            }
            finally
            {
                source.Cancel();
            }
        }

        private void WorkerThread(CancellationToken token, BlockingCollection<ClrRoot> queue)
        {
            try
            {
                foreach (ClrRoot root in _heap.EnumerateRoots())
                {
                    if (token.IsCancellationRequested)
                        break;

                    queue.Add(root);
                }
            }
            catch
            {
            }
            finally
            {
                queue.CompleteAdding();
            }
        }

        public ChainLink? FindPathFrom(ClrObject start)
        {
            return FindPathFrom(start, CancellationToken.None);
        }

        public ChainLink? FindPathFrom(ClrObject start, CancellationToken cancellation)
        {
            if (_found.TryGetValue(start, out ChainLink? link))
                return link;

            if (_targetPredicate is not null && _targetPredicate(start))
            {
                link = new ChainLink()
                {
                    Object = start,
                };

                _found.Add(start, link);
                return link;
            }

            if (start.Type is null || !start.Type.ContainsPointers)
                return null;

            List<byte[]> stack = new();
            link = WalkObject(stack, 0, start, cancellation);
            if (link is not null)
                return link;

            while (stack.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();

                ReferenceList curr = stack[stack.Count - 1];
                ulong currChild = curr.Next();
                if (currChild == 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                    curr.Dispose();
                    continue;
                }

                TraceConsidering(curr.Object, currChild);

                link = WalkObject(stack, curr.Object, currChild, cancellation);
                if (link is not null)
                    return CleanupAndGetResult(stack, link, curr);
            }

            return null;
        }

        private ChainLink CleanupAndGetResult(List<byte[]> stack, ChainLink link, ReferenceList currStorage)
        {
            stack.Reverse();

            ulong obj = currStorage.Object;
            ulong parent = currStorage.Parent;
            link = AddLink(link, obj);

            foreach (ReferenceList storage in stack)
            {
                // We can have multiple entries in stack for the same object
                if (storage.Object == obj)
                    continue;

                obj = storage.Object;
                if (storage.Object == parent)
                {
                    // This object is part of the chain that got us here
                    link = AddLink(link, obj);
                    parent = storage.Parent;
                }
                else
                {
                    // This object isn't part of the rooting chain, but we didn't
                    // finish processing it so we'll have to process it again next time
                    _seen.Remove(storage.Object);
                }
            }

            return link;
        }

        private ChainLink AddLink(ChainLink curr, ulong obj)
        {
            if (!_found.TryGetValue(obj, out ChainLink? next))
            {
                next = new()
                {
                    Next = curr,
                    Object = obj,
                };

                // Add found to the list of objects that point to our targets.
                _found[obj] = next;
            }

            // Remove obj from the seen list.  While it's not wrong to leave it there,
            // we want to minimize memory usage.
            _seen.Remove(obj);

            return next;
        }

        private ChainLink? WalkObject(List<byte[]> stack, ulong parent, ulong curr, CancellationToken cancellation)
        {
            ClrObject obj = _heap.GetObject(curr);
            TraceWalkObject(obj);

            if (_targetPredicate is not null && _targetPredicate(obj))
            {
                TraceFound(obj);

                ChainLink result = new()
                {
                    Object = curr,
                };

                _found[curr] = result;
                return result;
            }

            if (obj.Type is not null && obj.Type.ContainsPointers)
            {
                ReferenceList refList = default;
                byte offset = 0;

                foreach (ulong reference in obj.EnumerateReferenceAddresses())
                {
                    cancellation.ThrowIfCancellationRequested();

                    if (_found.TryGetValue(reference, out ChainLink? link))
                    {
                        ChainLink result = new()
                        {
                            Next = link,
                            Object = curr,
                        };

                        TraceFound(reference, link);

                        _found[curr] = result;
                        return result;
                    }
                    else if (!_seen.Add(reference))
                    {
                        TraceSeen(reference);

                        continue;
                    }

                    TraceReference(obj, reference);

                    if (refList.IsDefault)
                        refList = new ReferenceList(curr, parent, out offset);

                    offset = refList.Store(reference, offset);
                    if (offset == 0)
                    {
                        stack.Add(refList.Complete());
                        refList = new ReferenceList(curr, parent, out offset);
                        offset = refList.Store(reference, offset);
                        DebugOnly.Assert(offset != 0);
                    }
                }

                byte[]? item = refList.Complete();
                if (item is not null)
                    stack.Add(item);
            }

            return null;
        }

        [Conditional("GCROOT_TRACE")]
        private static void TraceReference(ClrObject obj, ulong reference)
        {
            Trace.WriteLine($"Reference: {obj.Address:x} {obj.Type?.Name} -> {reference:x}");
        }

        [Conditional("GCROOT_TRACE")]
        private static void TraceSeen(ulong reference)
        {
            Trace.WriteLine($"Seen: {reference:x} (ignoring)");
        }

        [Conditional("GCROOT_TRACE")]
        private static void TraceFound(ulong reference, ChainLink link)
        {
            Trace.WriteLine($"FOUND: {reference:x} -> {link.Object:x}");
        }

        [Conditional("GCROOT_TRACE")]
        private static void TraceFound(ClrObject obj)
        {
            Trace.WriteLine($"FOUND: {obj.Address:x} {obj.Type?.Name}");
        }

        [Conditional("GCROOT_TRACE")]
        public static void TraceConsidering(ulong curr, ulong child)
        {
            Trace.WriteLine($"Considering: {curr:x} -> {child:x}.");
        }

        [Conditional("GCROOT_TRACE")]
        public static void TraceWalkObject(ClrObject obj)
        {
            if (obj.Type is null || !obj.IsValid)
                Trace.WriteLine($"WalkObject {obj.Address:x} (invalid object)");
            else
                Trace.WriteLine($"WalkObject {obj.Address:x} {obj.Type.Name}{(obj.Type.ContainsPointers ? "" : " (no gc pointers)")}:");
        }

        // A way to track object references without generating too much garbage.
        private struct ReferenceList : IDisposable
        {
            // This was tested with 32, 64, and 128 bytes.  The overall perf was pretty similar
            // (with 128 being the best) but setting down to 32 bytes drastically lowers the
            // overall memory usage.
            private static int DataSize = 32;

            private const ulong PointerMask = ~(ControlBits);
            private const ulong ControlBits = Partial5PointerBit | Partial6PointerBit;

            private const ulong Partial6PointerBit = 1;
            private const ulong UlongPartial6Mask = 0x0000ffffffffffff;
            private const ulong Partial5PointerBit = 2;
            private const ulong UlongPartial5Mask = 0x000000ffffffffff;
            private byte[]? _data;

            public bool IsDefault => _data == null;

            public ReferenceList()
            {
            }

            public ReferenceList(ulong obj, ulong parent, out byte offset)
            {
                _data ??= ArrayPool<byte>.Shared.Rent(DataSize);
                offset = Store(obj, 1);
                offset = Store(parent, offset);
                _data[0] = offset;
            }

            public ReferenceList(byte[] data)
            {
                _data = data;
            }

            public ulong Object
            {
                get
                {
                    byte offset = 1;
                    return ReadAt(ref offset);
                }
            }

            public ulong Parent
            {
                get
                {
                    byte offset = 1;
                    ReadAt(ref offset);
                    return ReadAt(ref offset);
                }
            }

            private readonly ulong ReadAt(ref byte offset)
            {
                if (offset >= _data!.Length)
                    return 0;

                return (_data[offset] & ControlBits) switch
                {
                    0 => ReadAt(ref offset, 8),
                    Partial6PointerBit => ReadAt(ref offset, 6),
                    Partial5PointerBit => ReadAt(ref offset, 5),
                    ControlBits => 0,
                    _ => throw new InvalidDataException(),
                };
            }

            private readonly unsafe ulong ReadAt(ref byte offset, int bytes)
            {
                Span<byte> data = _data.AsSpan(offset);
                if (data.Length < bytes)
                    return 0;

                ulong result = 0;
                data.Slice(0, bytes).CopyTo(new Span<byte>(&result, bytes));

                if (offset + bytes > byte.MaxValue)
                    offset = byte.MaxValue;
                else
                    offset += (byte)bytes;

                return result & PointerMask;
            }

            public ulong Next()
            {
                return ReadAt(ref _data![0]);
            }

            public unsafe byte Store(ulong pointer, byte offset)
            {
                if (offset == 0)
                    throw new IndexOutOfRangeException(nameof(offset));

                pointer &= PointerMask;
                if (pointer == 0)
                    return offset;

                if ((pointer & UlongPartial5Mask) == pointer)
                    return Store(pointer | Partial5PointerBit, offset, 5);

                if ((pointer & UlongPartial6Mask) == pointer)
                    return Store(pointer | Partial6PointerBit, offset, 6);

                return Store(pointer, offset, 8);
            }

            private unsafe byte Store(ulong value, byte offset, int bytes)
            {
                Span<byte> data = _data.AsSpan(offset);
                if (data.Length < bytes)
                    return 0;

                Span<byte> ptr = new(&value, bytes);

                ptr.CopyTo(data);
                data = data.Slice(ptr.Length);
                if (data.Length > 0)
                    data[0] = (byte)ControlBits;

                offset += (byte)bytes;
                return offset;
            }

            public void Dispose()
            {
                if (_data is not null)
                    ArrayPool<byte>.Shared.Return(_data);
            }

            public byte[] Complete()
            {
                byte[]? data = _data!;
                _data = null;
                return data;
            }

            public static implicit operator byte[]?(ReferenceList d) => d._data;
            public static implicit operator ReferenceList(byte[] data) => new(data);
        }

        /// <summary>
        /// An entry in the rooting chain.
        /// </summary>
        public class ChainLink
        {
            /// <summary>
            /// The address of the object.
            /// </summary>
            public ulong Object { get; set; }

            /// <summary>
            /// The next object in the sequence.
            /// </summary>
            public ChainLink? Next { get; set; }

            public override string ToString()
            {
                if (Next is not null)
                    return $"{Object:x} -> {Next.Object:x}";

                return Object.ToString("x");
            }
        }
    }
}
