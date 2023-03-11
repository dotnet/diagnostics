// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

using static Microsoft.Diagnostics.ExtensionCommands.TableOutput;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [DebugCommand(Name=nameof(SimulateGCHeapCorruption), Help = "Writes values to the GC heap in strategic places to simulate heap corruption.")]
    public class SimulateGCHeapCorruption : CommandBase
    {
        private static readonly List<Change> _changes = new();

        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [Argument]
        public string Command { get; set; }

        public override void Invoke()
        {
            switch (Command?.ToLowerInvariant())
            {
                case "revert":
                case "rollback":
                case "undo":
                    Rollback();
                    break;

                case "corrupt":
                    Corrupt();
                    break;

                default:
                    List();
                    break;
            }
        }

        private void Usage()
        {
            Console.WriteLine($"To simulate heap corruption, use: !sos {nameof(SimulateGCHeapCorruption)} corrupt");
            Console.WriteLine($"To revert heap corruption, use:   !sos {nameof(SimulateGCHeapCorruption)} revert");
        }

        private void List()
        {
            if (_changes.Count == 0)
            {
                Console.WriteLine("No changes written to the heap.");
            }
            else
            {
                TableOutput output = new(Console, (12, "x12"), (12, "x12"), (16, "x"), (16, "x"), (0, ""));
                output.WriteRow("Object", "ModifiedAddr", "Old Value", "New Value", "Expected Failure");

                foreach (Change change in _changes)
                    output.WriteRow(new DmlDumpObj(change.Object), change.AddressModified, change.OriginalValue.Reverse(), change.NewValue.Reverse(), change.ExpectedFailure);
            }

            Console.WriteLine();
            Usage();
        }

        private void Rollback()
        {
            if (_changes is null)
            {
                Console.WriteLine("No changes written to the heap.");
                Usage();
                return;
            }

            foreach (Change change in _changes)
            {
                if (!MemoryService.WriteMemory(change.AddressModified, change.OriginalValue, out int written))
                    Console.WriteLine($"Failed to restore memory at address: {change.AddressModified:x}, heap is still corrupted!");
                else if (written != change.OriginalValue.Length)
                    Console.WriteLine($"Failed to restore memory at address: {change.AddressModified:x}, only wrote {written} bytes out of {change.OriginalValue.Length}!");
            }

            _changes.Clear();
        }


        private void Corrupt()
        {
            if (_changes.Count > 0)
            {
                Console.WriteLine("Heap is already corrupted!");
                Usage();
                return;
            }

            ClrObject[] syncBlocks = FindObjectsWithSyncBlock().Take(2).ToArray();
            if (syncBlocks.Length >= 1)
                WriteValue(ObjectCorruptionKind.SyncBlockMismatch, syncBlocks[0], syncBlocks[0] - 4, (byte)0xcc);
            if (syncBlocks.Length >= 2)
                WriteValue(ObjectCorruptionKind.SyncBlockZero, syncBlocks[1], syncBlocks[1] - 4, 0x08000000);

            ClrObject[] withRefs = FindObjectsWithReferences().Take(3).ToArray();
            if (withRefs.Length >= 1)
            {
                (ulong Object, ulong FirstReference) entry = GetFirstReference(withRefs[0]);
                WriteValue(ObjectCorruptionKind.BadObjectReference, entry.Object, entry.FirstReference, 0xcccccccc);
            }
            if (withRefs.Length >= 2)
            {
                ulong free = Runtime.Heap.EnumerateObjects().FirstOrDefault(f => f.IsFree);
                if (free != 0)
                {
                    (ulong Object, ulong FirstReference) entry = GetFirstReference(withRefs[1]);
                    WriteValue(ObjectCorruptionKind.FreeObjectReference, entry.Object, entry.FirstReference, free);
                }
            }
            if (withRefs.Length >= 3)
            {
                (ulong Object, ulong FirstReference) entry = GetFirstReference(withRefs[2]);
                WriteValue(ObjectCorruptionKind.ObjectReferenceNotPointerAligned, entry.Object, entry.FirstReference, (byte)1);
            }

            ClrObject[] arrays = FindArrayObjects().Take(2).ToArray();
            if (arrays.Length >= 1)
                WriteValue(ObjectCorruptionKind.BadMethodTable, arrays[0], arrays[0], 0xcccccccc);
            if (arrays.Length >= 2)
                WriteValue(ObjectCorruptionKind.ObjectTooLarge, arrays[1], arrays[1] + (uint)MemoryService.PointerSize, 0xcccccccc);

            List();
        }

        private static (ulong Object, ulong FirstReference) GetFirstReference(ClrObject obj)
        {
            return (obj, obj.EnumerateReferenceAddresses().First());
        }

        private IEnumerable<ClrObject> FindObjectsWithSyncBlock()
        {
            foreach (SyncBlock sync in Runtime.Heap.EnumerateSyncBlocks())
            {
                ClrObject obj = Runtime.Heap.GetObject(sync.Object);

                if (_changes.Any(ch => ch.Object == obj))
                    continue;

                if (obj.IsValid && !obj.IsFree)
                    yield return obj;
            }
        }

        private IEnumerable<ClrObject> FindObjectsWithReferences()
        {
            foreach (ClrObject obj in Runtime.Heap.EnumerateObjects())
            {
                if (obj.IsFree || !obj.IsValid)
                    continue;

                if (_changes.Any(ch => ch.Object == obj))
                    continue;

                if (obj.EnumerateReferenceAddresses().Any())
                    yield return obj;
            }
        }

        private IEnumerable<ClrObject> FindArrayObjects()
        {
            foreach (ClrObject obj in Runtime.Heap.EnumerateObjects())
            {
                if (obj.IsFree || !obj.IsValid)
                    continue;

                if (_changes.Any(ch => ch.Object == obj))
                    continue;

                if (obj.IsArray)
                    yield return obj;
            }
        }

        private unsafe void WriteValue<T>(ObjectCorruptionKind kind, ulong obj, ulong address, T value)
            where T : unmanaged
        {
            byte[] old = new byte[sizeof(T)];

            Span<T> span = new(&value, 1);
            Span<byte> newBuffer = MemoryMarshal.Cast<T, byte>(span);

            if (!MemoryService.ReadMemory(address, old, old.Length, out int read) || read != old.Length)
                throw new Exception("Failed to read memory.");

            if (!MemoryService.WriteMemory(address, newBuffer, out int written) || written != newBuffer.Length)
                throw new Exception($"Failed to write to {address:x}");

            _changes.Add(new()
            {
                Object = obj,
                AddressModified = address,
                OriginalValue = old,
                NewValue = newBuffer.ToArray(),
                ExpectedFailure = kind
            });
        }

        private sealed class Change
        {
            public ulong Object { get; set; }
            public ulong AddressModified { get; set; }
            public byte[] OriginalValue { get; set; }
            public byte[] NewValue { get; set; }
            public ObjectCorruptionKind ExpectedFailure { get; set; }
        }
    }
}
