// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumphttprequests", Aliases = new string[] { "DumpHttpRequests" }, Help = "Shows all currently active incoming HTTP requests.")]
    public class DumpHttpRequestsCommand : ClrRuntimeCommandBase
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
                        if (!collection.IsNull)
                        {
                            string method = collection.ReadStringField("<Method>k__BackingField") ?? "";
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
            Column methodColumn = Text.GetAppropriateWidth(requests.Select(r => r.Method)).WithAlignment(Align.Left);
            Column schemeColumn = Text.GetAppropriateWidth(requests.Select(r => r.scheme)).WithAlignment(Align.Left);
            Column urlColumn = Text.GetAppropriateWidth(requests.Select(r => r.Url)).WithAlignment(Align.Left);
            Table output = new(Console, DumpObj.WithAlignment(Align.Left), methodColumn, schemeColumn, urlColumn); ;
            output.WriteHeader("Address", "Method", "Scheme", "Url");

            foreach ((ulong address, string method, string scheme, string url) in requests)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                output.WriteRow(address, method, scheme, url);
            }
        }
    }
}
