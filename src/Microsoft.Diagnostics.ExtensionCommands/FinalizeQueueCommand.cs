// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "finalizequeue", Aliases = new[] { "fq", "FinalizeQueue" }, Help = "Displays all objects registered for finalization.")]
    public class FinalizeQueueCommand : ClrRuntimeCommandBase
    {
        [Option(Name = "-detail", Help = "Will display extra information on any SyncBlocks that need to be cleaned up, and on any RuntimeCallableWrappers (RCWs) that await cleanup.  Both of these data structures are cached and cleaned up by the finalizer thread when it gets a chance to run.")]
        public bool Detail { get; set; }

        [Option(Name = "-allReady", Help = "Specifying this argument will allow for the display of all objects  that are ready for finalization, whether they are already marked by  the GC as such, or whether the next GC will.  The objects that are  not in the \"Ready for finalization\" list are finalizable objects that  are no longer rooted.  This option can be very expensive, as it  verifies whether all the objects in the finalizable queues are still  rooted or not.")]
        public bool AllReady { get; set; }

        [Option(Name = "-short", Help = "Limits the output to just the address of each object.  If used in conjunction with -allReady it enumerates all objects that have a finalizer that are no longer rooted.  If used independently it lists all objects in the finalizable and \"ready for finalization\" queues.")]
        public bool Short { get; set; }

        [Option(Name = "-mt", Help = "Limits the search for finalizable objects to only those matching the given MethodTable.")]
        public string MethodTable { get; set; }

        [Option(Name = "-stat", Aliases = new string[] { "-summary" }, Help = "Only print object statistics, not the list of all objects.")]
        public bool Stat { get; set; }

        [ServiceImport]
        public LiveObjectService LiveObjects { get; set; }

        [ServiceImport]
        public RootCacheService RootCache { get; set; }

        [ServiceImport]
        public DumpHeapService DumpHeap { get; set; }

        public override void Invoke()
        {
            ulong mt = 0;
            if (!string.IsNullOrWhiteSpace(MethodTable))
            {
                mt = ParseAddress(MethodTable) ?? throw new ArgumentException($"Could not parse MethodTable: '{MethodTable}'");
            }

            if (Short && Stat)
            {
                throw new ArgumentException("Cannot specify both -short and -stat.");
            }

            // If we are going to search for only live objects, be sure to print a warning first
            // in the output of the command instead of in between the rest of the output.
            if (AllReady)
            {
                LiveObjects.PrintWarning = true;
                LiveObjects.Initialize();
            }

            if (!Short)
            {
                PrintSyncBlockCleanupData();
                PrintRcwCleanupData();
                Console.WriteLine("----------------------------------");
                Console.WriteLine();

                PrintGenerationalRanges();

                if (AllReady)
                {
                    Console.WriteLine("Statistics for all finalizable objects that are no longer rooted:");
                }
                else
                {
                    Console.WriteLine("Statistics for all finalizable objects (including all objects ready for finalization):");
                }
            }

            IEnumerable<ClrObject> objects = EnumerateFinalizableObjects(AllReady, mt);
            DumpHeapService.DisplayKind displayKind = Short ? DumpHeapService.DisplayKind.Short : DumpHeapService.DisplayKind.Normal;

            DumpHeap.PrintHeap(objects, displayKind, Stat, printFragmentation: false);

        }

        private IEnumerable<ClrObject> EnumerateFinalizableObjects(bool allReady, ulong mt)
        {
            IEnumerable<ClrObject> result = EnumerateValidFinalizableObjectsWithTypeFilter(mt);

            if (allReady)
            {
                HashSet<ulong> rootedByFinalizer = new();
                foreach (ClrRoot root in Runtime.Heap.EnumerateFinalizerRoots())
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    ClrObject obj = root.Object;
                    if (obj.IsValid)
                    {
                        rootedByFinalizer.Add(obj);
                    }
                }

                // We are trying to find all objects that are ready to be finalized, which is essentially
                // all dead objects.  However, objects which were previously collected but waiting on
                // the finalizer thread to process them are considered "live" because they are rooted by
                // the finalizer queue.  So our result needs to be either dead objects or directly rooted
                // by the finalizer queue.
                result = result.Where(obj => rootedByFinalizer.Contains(obj) || !LiveObjects.IsLive(obj));
            }

            return result;
        }

        private IEnumerable<ClrObject> EnumerateValidFinalizableObjectsWithTypeFilter(ulong mt)
        {
            foreach (ClrObject obj in Runtime.Heap.EnumerateFinalizableObjects())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (!obj.IsValid)
                {
                    continue;
                }

                if (mt != 0 && obj.Type.MethodTable != mt)
                {
                    continue;
                }

                yield return obj;
            }
        }

        private void PrintSyncBlockCleanupData()
        {
            Table output = null;
            int total = 0;
            foreach (ClrSyncBlockCleanupData cleanup in Runtime.EnumerateSyncBlockCleanupData())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (output is null)
                {
                    output = new(Console, Pointer, Pointer, Pointer, Pointer);
                    output.WriteHeader("SyncBlock", "RCW", "CCW", "ComClassFactory");
                }

                output.WriteRow(cleanup.SyncBlock, cleanup.Rcw, cleanup.Ccw, cleanup.ClassFactory);
                total++;
            }

            Console.WriteLine($"SyncBlocks to be cleaned up: {total:n0}");
        }

        private void PrintRcwCleanupData()
        {
            Table output = null;
            int freeThreadedCount = 0;
            int mtaCount = 0;
            int staCount = 0;

            foreach (ClrRcwCleanupData cleanup in Runtime.EnumerateRcwCleanupData())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (output is null)
                {
                    output = new(Console, Pointer, Pointer, Thread, Text);
                    output.WriteHeader("RCW", "Context", "Thread", "Apartment");
                }

                string apartment;
                if (cleanup.IsFreeThreaded)
                {
                    freeThreadedCount++;
                    apartment = "(FreeThreaded)";
                }
                else if (cleanup.Thread == 0)
                {
                    mtaCount++;
                    apartment = "(MTA)";
                }
                else
                {
                    staCount++;
                    apartment = "(STA)";
                }

                output.WriteRow(cleanup.Rcw, cleanup.Context, cleanup.Thread, apartment);
            }

            Console.WriteLine($"Free-Threaded Interfaces to be released: {freeThreadedCount:n0}");
            Console.WriteLine($"MTA Interfaces to be released: {mtaCount:n0}");
            Console.WriteLine($"STA Interfaces to be released: {staCount:n0}");
        }

        private void PrintGenerationalRanges()
        {
            foreach (ClrSubHeap heap in Runtime.Heap.SubHeaps)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine($"Heap {heap.Index}");

                WriteGeneration(heap, 0);
                WriteGeneration(heap, 1);
                WriteGeneration(heap, 2);

                Console.WriteLine($"Ready for finalization {heap.FinalizerQueueRoots.Length / (uint)IntPtr.Size:n0} objects ({heap.FinalizerQueueRoots.Start:x}->{heap.FinalizerQueueRoots.End:x})");

                Console.WriteLine("------------------------------");
            }
        }

        private void WriteGeneration(ClrSubHeap heap, int gen)
        {
            MemoryRange range = heap.GenerationalFinalizableObjects[gen];
            Console.WriteLine($"generation {gen} has {range.Length / (uint)IntPtr.Size:n0} objects ({range.Start:x}->{range.End:x})");
        }
    }
}
