// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpexceptions", Help = "Displays a list of all managed exceptions.")]
    public class DumpExceptionsCommand : ClrRuntimeCommandBase
    {
        [ServiceImport]
        public LiveObjectService LiveObjects { get; set; } = null!;

        [Option(Name = "-live")]
        public bool Live { get; set; }

        [Option(Name = "-dead")]
        public bool Dead{ get; set; }

        [Option(Name = "-gen")]
        public string? Generation { get; set; }

        [Option(Name = "-type")]
        public string? Type { get; set; }

        public override void ExtensionInvoke()
        {
            HeapWithFilters filteredHeap = ParseArguments();

            IEnumerable<ClrObject> exceptionObjects =
                filteredHeap.EnumerateFilteredObjects(Console.CancellationToken)
                    .Where(obj => obj.IsException);

            if (Live)
            {
                exceptionObjects = exceptionObjects.Where(obj => LiveObjects.IsLive(obj));
            }

            if (Dead)
            {
                exceptionObjects = exceptionObjects.Where(obj => !LiveObjects.IsLive(obj));
            }

            if (!string.IsNullOrWhiteSpace(Type))
            {
                string type = Type!;
                exceptionObjects = exceptionObjects.Where(obj => obj.Type!.Name!.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            PrintExceptions(exceptionObjects);
        }

        private void PrintExceptions(IEnumerable<ClrObject> exceptionObjects)
        {
            Table output = new(Console, ColumnKind.Pointer, ColumnKind.Pointer, ColumnKind.TypeName);
            output.WriteHeader("Address", "MethodTable", "Message", "Name");

            int totalExceptions = 0;
            foreach (ClrObject exceptionObject in exceptionObjects)
            {
                totalExceptions++;

                ClrException clrException = exceptionObject.AsException()!;
                output.WriteRow(exceptionObject.Address, exceptionObject.Type!.MethodTable, exceptionObject.Type!.Name);

                Console.Write("        Message: ");
                Console.WriteLine(clrException.Message ?? "<null>");

                ImmutableArray<ClrStackFrame> stackTrace = clrException.StackTrace;
                if (stackTrace.Length > 0)
                {
                    Console.Write("        StackFrame: ");
                    Console.WriteLine(stackTrace[0].ToString());
                }
            }

            Console.WriteLine();
            Console.WriteLine($"    Total: {totalExceptions} objects");
        }

        private HeapWithFilters ParseArguments()
        {
            HeapWithFilters filteredHeap = new(Runtime.Heap);

            if (Live && Dead)
            {
                Live = false;
                Dead = false;
            }

            // TODO: CR: this is the same as in dumpheap. Where to put this?
            if (!string.IsNullOrWhiteSpace(Generation))
            {
                Generation generation = Generation!.ToLowerInvariant() switch
                {
                    "gen0" => Diagnostics.Runtime.Generation.Generation0,
                    "gen1" => Diagnostics.Runtime.Generation.Generation1,
                    "gen2" => Diagnostics.Runtime.Generation.Generation2,
                    "loh" or "large" => Diagnostics.Runtime.Generation.Large,
                    "poh" or "pinned" => Diagnostics.Runtime.Generation.Pinned,
                    "foh" or "frozen" => Diagnostics.Runtime.Generation.Frozen,
                    _ => throw new ArgumentException($"Unknown generation: {Generation}. Only gen0, gen1, gen2, loh (large), poh (pinned) and foh (frozen) are supported")
                };

                filteredHeap.Generation = generation;
            }

            return filteredHeap;
        }
    }
}
