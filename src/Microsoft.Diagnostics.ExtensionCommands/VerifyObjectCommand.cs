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
    [Command(Name = "verifyobj", Help = "Checks the given object for signs of corruption.")]
    public sealed class VerifyObjectCommand : CommandBase
    {
        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public IMemoryService Memory { get; set; }

        [Argument(Name = "ObjectAddress", Help = "The object to verify.")]
        public string ObjectAddress { get; set; }

        public override void Invoke()
        {
            if (!TryParseAddress(ObjectAddress, out ulong objAddress))
            {
                throw new ArgumentException($"Invalid object address: '{ObjectAddress}'", nameof(ObjectAddress));
            }

            bool isNotCorrupt = Runtime.Heap.FullyVerifyObject(objAddress, out IEnumerable<ObjectCorruption> corruptionEnum);
            if (isNotCorrupt)
            {
                Console.WriteLine($"object 0x{objAddress:x} is a valid object");
                return;
            }

            ObjectCorruption[] corruption = corruptionEnum.OrderBy(r => r.Offset).ToArray();

            Column offsetColumn = HexOffset.WithAlignment(Align.Left);
            offsetColumn = offsetColumn.GetAppropriateSize(corruption.Select(r => (object)r.Offset));

            Table output = new(Console, offsetColumn, Column.ForEnum<ObjectCorruptionKind>(), Text);
            output.WriteHeader("Offset", "Issue", "Description");
            foreach (ObjectCorruption oc in corruption)
            {
                output.WriteRow(oc.Offset, oc.Kind, VerifyHeapCommand.GetObjectCorruptionMessage(Memory, Runtime.Heap, oc));
            }

            Console.WriteLine();
            Console.WriteLine($"{corruption.Length:n0} error{(corruption.Length == 1 ? "" : "s")}  detected.");
        }
    }
}
