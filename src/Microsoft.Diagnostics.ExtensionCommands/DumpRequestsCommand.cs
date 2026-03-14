// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumprequests", Aliases = new string[] { "DumpRequests" }, Help = "Displays all currently active incoming HTTP requests.")]
    public class DumpRequestsCommand : ClrRuntimeCommandBase
    {
        public override void Invoke()
        {
            List<(ulong Address, string Method, string Protocol, string Url)> requests = new();
            if (Runtime.Heap.CanWalkHeap)
            {
                foreach (ClrObject obj in Runtime.Heap.EnumerateObjects())
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    if (!obj.IsValid || obj.IsNull)
                    {
                        continue;
                    }

                    if (obj.Type?.Name?.Equals("Microsoft.AspNetCore.Http.DefaultHttpContext") ?? false)
                    {
                        ClrObject collection = obj.ReadValueTypeField("_features").ReadObjectField("<Collection>k__BackingField");
                        if (!collection.IsNull && collection.IsValid)
                        {
                            if (!collection.TryReadStringField("<Method>k__BackingField", default, out string method))
                            {
                                method = collection.ReadStringField("_methodText") ?? "";
                            }

                            string scheme = collection.ReadStringField("<Scheme>k__BackingField") ?? "";
                            string path = collection.ReadStringField("<Path>k__BackingField") ?? "";
                            string query = collection.ReadStringField("<QueryString>k__BackingField") ?? "";
                            requests.Add((obj.Address, method, $"{scheme}", $"{path}{query}"));
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("The GC heap is not in a valid state for traversal.");
            }

            if (requests.Count > 0)
            {
                PrintRequests(requests);
                Console.WriteLine($"Found {requests.Count} active requests");
            }
            else
            {
                Console.WriteLine("No requests found");
            }
        }

        public void PrintRequests(List<(ulong Address, string Method, string scheme, string Url)> requests)
        {
            Column addressColumn = DumpObj.GetAppropriateWidth(requests.Select(r => r.Address), 7).WithAlignment(Align.Left);
            Column methodColumn = Text.GetAppropriateWidth(requests.Select(r => r.Method), 6).WithAlignment(Align.Left);
            Column schemeColumn = Text.GetAppropriateWidth(requests.Select(r => r.scheme), 6).WithAlignment(Align.Left);
            Column urlColumn = Text.GetAppropriateWidth(requests.Select(r => r.Url), 3).WithAlignment(Align.Left);
            Table output = new(Console, addressColumn, methodColumn, schemeColumn, urlColumn); ;
            output.WriteHeader("Address", "Method", "Scheme", "Url");

            foreach ((ulong address, string method, string scheme, string url) in requests)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                output.WriteRow(address, method, scheme, url);
            }
        }
    }
}
