// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpexceptions", Help = "Displays a list of all managed exceptions.")]
    public class DumpExceptionsCommand : CommandBase
    {
        [ServiceImport]
        public ClrRuntime Runtime { get; set; } = null!;

        [ServiceImport]
        public LiveObjectService LiveObjects { get; set; } = null!;

        [Option(Name = "-live")]
        public bool Live { get; set; }

        [Option(Name = "-dead")]
        public bool Dead{ get; set; }

        [Option(Name = "-gen")]
        public string? Generation { get; set; }

        public override void Invoke()
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

            PrintExceptions(exceptionObjects);
        }

        private void PrintExceptions(IEnumerable<ClrObject> exceptionObjects)
        {
            // TODO: CR: not sure what to do with stacktraces. PrintException can show full stacktrace but here it can take up too much space
            const int maxCharsInMessage = 90; // this is somewhat arbitrary

            Column exceptionMessage = new(Align.Left, maxCharsInMessage, Formats.Text);
            Table output = new(Console, ColumnKind.Pointer, ColumnKind.Pointer, exceptionMessage, ColumnKind.TypeName);
            output.WriteHeader("Address", "MethodTable", "Message", "Name");

            int totalExceptions = 0;
            foreach (ClrObject exceptionObject in exceptionObjects)
            {
                totalExceptions++;

                ClrException clrException = exceptionObject.AsException()!;
                string message = FormatMessage(clrException.Message, maxCharsInMessage);
                output.WriteRow(exceptionObject.Address, exceptionObject.Type!.MethodTable, message, exceptionObject.Type!.Name);
            }

            Console.WriteLine();
            Console.WriteLine($"    Total: {totalExceptions} objects");
        }

        private static string FormatMessage(string? value, int charCount)
        {
            if (value is null)
            {
                return "<null>";
            }

            if (value.Length <= charCount)
            {
                return value;
            }

            const string endOfString = " ...";
            return $"{value.Substring(0, charCount - endOfString.Length)}{endOfString}";
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
