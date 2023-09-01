// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Extensions
{
    internal sealed class MemoryRegionServiceFromDebuggerServices : IMemoryRegionService
    {
        private readonly IDebugClient5 _client;
        private readonly IDebugControl5 _control;

        public MemoryRegionServiceFromDebuggerServices(IDebugClient5 client, IDebugControl5 control)
        {
            _client = client;
            _control = control;
        }

        public IEnumerable<IMemoryRegion> EnumerateRegions()
        {
            bool foundHeader = false;
            bool skipped = false;

            (int hr, string text) = RunCommandWithOutput("!address");
            if (hr < 0)
            {
                throw new InvalidOperationException($"!address failed with hresult={hr:x}");
            }

            foreach (string line in text.Split('\n'))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                if (!foundHeader)
                {
                    // find the !address header
                    string[] split = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (split.Length > 0)
                    {
                        foundHeader = (split[0] == "BaseAddress" || split[0] == "BaseAddr") && split.Last() == "Usage";
                    }
                }
                else if (!skipped)
                {
                    // skip the ---------- line
                    skipped = true;
                }
                else
                {
                    string[] parts = ((line[0] == '+') ? line.Substring(1) : line).Split(new char[] { ' ' }, 6, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        continue;
                    }

                    if (!ulong.TryParse(parts[0].Replace("`", ""), System.Globalization.NumberStyles.HexNumber, null, out ulong start))
                    {
                        continue;
                    }

                    if (!ulong.TryParse(parts[1].Replace("`", ""), System.Globalization.NumberStyles.HexNumber, null, out ulong end))
                    {
                        continue;
                    }

                    int index = 3;
                    if (GetEnumValue(parts, index, out MemoryRegionType type))
                    {
                        index++;
                    }

                    if (GetEnumValue(parts, index, out MemoryRegionState state))
                    {
                        index++;
                    }

                    StringBuilder sbRemainder = new();
                    for (int i = index; i < parts.Length; i++)
                    {
                        if (i != index)
                        {
                            sbRemainder.Append(' ');
                        }

                        sbRemainder.Append(parts[i]);
                    }

                    string remainder = sbRemainder.ToString();
                    parts = remainder.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    MemoryRegionProtection protect = default;
                    index = 0;
                    while (index < parts.Length - 1)
                    {
                        if (Enum.TryParse(parts[index], ignoreCase: true, out MemoryRegionProtection result))
                        {
                            protect |= result;
                            if (parts[index + 1] == "|")
                            {
                                index++;
                            }
                        }
                        else
                        {
                            break;
                        }

                        index++;
                    }

                    string description = index < parts.Length ? parts[index++].Trim() : "";

                    // On Linux, !address is reporting this as MEM_PRIVATE or MEM_UNKNOWN
                    if (description == "Image")
                    {
                        type = MemoryRegionType.MEM_IMAGE;
                    }

                    // On Linux, !address is reporting this as nothing
                    if (type == MemoryRegionType.MEM_UNKNOWN && state == MemoryRegionState.MEM_UNKNOWN && protect == MemoryRegionProtection.PAGE_UNKNOWN)
                    {
                        state = MemoryRegionState.MEM_FREE;
                        protect = MemoryRegionProtection.PAGE_NOACCESS;
                    }

                    string image = null;
                    if (type == MemoryRegionType.MEM_IMAGE && index < parts.Length)
                    {
                        image = parts[index].Substring(1, parts[index].Length - 2);
                        index++;
                    }

                    if (description.Equals("<unknown>", StringComparison.OrdinalIgnoreCase))
                    {
                        description = "";
                    }

                    MemoryRegionUsage usage = description switch
                    {
                        "" => MemoryRegionUsage.Unknown,

                        "Free" => MemoryRegionUsage.Free,
                        "Image" => MemoryRegionUsage.Image,

                        "PEB" => MemoryRegionUsage.Peb,
                        "PEB32" => MemoryRegionUsage.Peb,
                        "PEB64" => MemoryRegionUsage.Peb,

                        "TEB" => MemoryRegionUsage.Teb,
                        "TEB32" => MemoryRegionUsage.Teb,
                        "TEB64" => MemoryRegionUsage.Teb,

                        "Stack" => MemoryRegionUsage.Stack,
                        "Stack32" => MemoryRegionUsage.Stack,
                        "Stack64" => MemoryRegionUsage.Stack,

                        "Heap" => MemoryRegionUsage.Heap,
                        "Heap32" => MemoryRegionUsage.Heap,
                        "Heap64" => MemoryRegionUsage.Heap,


                        "PageHeap" => MemoryRegionUsage.PageHeap,
                        "PageHeap64" => MemoryRegionUsage.PageHeap,
                        "PageHeap32" => MemoryRegionUsage.PageHeap,

                        "MappedFile" => MemoryRegionUsage.FileMapping,
                        "CLR" => MemoryRegionUsage.CLR,

                        "Other" => MemoryRegionUsage.Other,
                        "Other32" => MemoryRegionUsage.Other,
                        "Other64" => MemoryRegionUsage.Other,

                        _ => MemoryRegionUsage.Unknown
                    };

                    yield return new AddressMemoryRange()
                    {
                        Start = start,
                        End = end,
                        Type = type,
                        State = state,
                        Protection = protect,
                        Usage = usage,
                        Image = image
                    };
                }
            }

            if (!foundHeader)
            {
                throw new InvalidOperationException($"!address did not produce a standard header.\nThis may mean symbols could not be resolved for ntdll.\nPlease run !address and make sure the output looks correct.");
            }
        }

        private static bool GetEnumValue<T>(string[] parts, int index, out T type)
            where T : struct
        {
            if (index < parts.Length)
            {
                return Enum.TryParse(parts[index], ignoreCase: true, out type);
            }

            type = default;
            return false;
        }

        private (int hresult, string output) RunCommandWithOutput(string command)
        {
            using DbgEngOutputHolder dbgengOutput = new(_client);
            StringBuilder sb = new(1024);
            dbgengOutput.OutputReceived += (mask, text) => sb.Append(text);

            int hr = _control.ExecuteWide(DEBUG_OUTCTL.THIS_CLIENT, command, DEBUG_EXECUTE.DEFAULT);

            return (hr, sb.ToString());
        }

        private sealed class AddressMemoryRange : IMemoryRegion
        {
            public ulong Start { get; internal set; }

            public ulong End { get; internal set; }

            public ulong Size { get; internal set; }

            public MemoryRegionType Type { get; internal set; }

            public MemoryRegionState State { get; internal set; }

            public MemoryRegionProtection Protection { get; internal set; }

            public MemoryRegionUsage Usage { get; internal set; }

            public string Image { get; internal set; }
        }
    }
}
