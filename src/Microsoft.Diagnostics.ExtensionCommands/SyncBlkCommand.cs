// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
using Microsoft.Diagnostics.DataContractReader.Legacy;
using static Microsoft.Diagnostics.ExtensionCommands.DacCommandHelpers;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "syncblk", Help = "Displays the SyncBlock holder info.")]
    public sealed class SyncBlkCommand(IServiceProvider services, RuntimeWrapper runtime, IMemoryService memory, IThreadService threads) : SosDacCommandBase(services, runtime)
    {
        [Argument<string>(Name = "index", Help = "Optional decimal sync block index.")]
        public string Index { get; set; }

        [Option<bool>(Name = "--all", Aliases = new[] { "-all" }, Help = "Display all sync blocks, including free entries.")]
        public bool All { get; set; }

        [Option<bool>(Name = "--dml", Aliases = new[] { "/d" }, Help = "Enable debugger markup links.")]
        public bool DmlEnabled { get; set; }

        public override unsafe void Invoke()
        {
            ulong requestedIndex = 0;
            if (Index != null && !ulong.TryParse(Index, NumberStyles.None, CultureInfo.InvariantCulture, out requestedIndex))
            {
                throw new DiagnosticsException($"Invalid sync block index: {Index}");
            }

            ISOSDacInterface dac = Dac;
            DacpSyncBlockData data = default;
            if (dac.GetSyncBlockData(1, &data) != 0)
            {
                WriteLine("Error requesting SyncBlk data");
                return;
            }

            uint count = data.SyncBlockCount;
            string padding = memory.PointerSize == 8 ? "        " : "";
            WriteLine($"Index{padding} SyncBlock MonitorHeld Recursion Owning Thread Info{padding}  SyncBlock Owner");
            uint free = 0, ccw = 0, rcw = 0, factories = 0;
            for (ulong index = requestedIndex == 0 ? 1 : requestedIndex; index <= count; index++)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                if (dac.GetSyncBlockData((uint)index, &data) != 0)
                {
                    WriteLine($"SyncBlock {(int)index} is invalid{(index == requestedIndex ? "" : ", continuing...")}");
                }
                else
                {
                    bool isFree = data.bFree != Microsoft.Diagnostics.DataContractReader.Legacy.Interop.BOOL.FALSE;
                    if (All || index == requestedIndex || (data.MonitorHeld > 0 && !isFree))
                    {
                        Write($"{(int)index,5} ");
                        if (!isFree || index != requestedIndex)
                        {
                            Write($"{FormatPointer(data.SyncBlockPointer)}  {(int)data.MonitorHeld,11} {(int)data.Recursion,9} {FormatPointer(data.HoldingThread)} ");
                            WriteOwningThread(dac, data.HoldingThread);
                            if (isFree)
                            {
                                Write($"  {0,8}");
                            }
                            else
                            {
                                Write("  ");
                                WriteDacAddressLink(Console, data.Object, memory.PointerSize, "DumpObj", DmlEnabled);
                                Write($" {GetObjectTypeName(dac, data.Object, memory.PointerSize)}");
                            }
                        }
                        WriteLine(isFree ? " Free" : "");
                    }

                    if (isFree)
                    {
                        free++;
                    }
                    else
                    {
                        switch (data.COMFlags)
                        {
                            case 1: ccw++; break;
                            case 2: rcw++; break;
                            case 4: factories++; break;
                        }
                    }
                }

                if (requestedIndex != 0)
                {
                    break;
                }
            }

            WriteLine("-----------------------------");
            WriteLine($"Total           {(int)count}");
            if (OperatingSystem.IsWindows())
            {
                WriteLine($"CCW             {(int)ccw}");
                WriteLine($"RCW             {(int)rcw}");
                WriteLine($"ComClassFactory {(int)factories}");
            }
            WriteLine($"Free            {(int)free}");
        }

        private string FormatPointer(ulong address) => FormatDacAddress(address, memory.PointerSize, upperCase: true);

        private unsafe void WriteOwningThread(ISOSDacInterface dac, ulong address)
        {
            if (address == (OperatingSystem.IsWindows() || memory.PointerSize == 4 ? uint.MaxValue : ulong.MaxValue))
            {
                Write(" orphaned ");
            }
            else if (address == 0)
            {
                Write("    none  ");
            }
            else
            {
                DacpThreadData thread = default;
                if (dac.GetThreadData(address, &thread) != 0)
                {
                    throw new DiagnosticsException($"Failed to request Thread at {FormatPointer(address)}");
                }

                string osId = thread.osThreadId.ToString("x", CultureInfo.InvariantCulture);
                Console.WriteDmlExec(osId, DmlEnabled ? $"~~[{osId}]s" : null);
                int threadIndex;
                try
                {
                    threadIndex = threads.GetThreadFromId(unchecked((uint)thread.osThreadId)).ThreadIndex;
                }
                catch (DiagnosticsException)
                {
                    Write(" XXX ");
                    return;
                }
                Write($"{threadIndex,4} ");
            }
        }
    }
}
