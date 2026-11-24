// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
using Microsoft.Diagnostics.DataContractReader.Legacy;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpgcdata", Aliases = new[] { "DumpGCData", "dgc" }, Help = "Displays information about the GC data.")]
    public sealed class DumpGcDataCommand(IServiceProvider services, RuntimeWrapper runtime) : SosDacCommandBase(services, runtime)
    {
        private static readonly string[] s_global =
        [
            "concurrent GCs", "compacting GCs", "promoting GCs", "GCs that did demotion", "card bundles", "elevation logic",
        ];

        private static readonly string[] s_points =
        [
            "pre short", "post short", "merged pins", "converted pins", "pre pin", "post pin",
            "pre and post pin", "pre short padded", "post short padded",
        ];

        private static readonly (string Name, char Kind)[] s_compact =
        [
            ("low on ephemeral space", 'M'), ("high fragmentation", 'W'), ("couldn't allocate gaps", 'M'),
            ("user specfied compact LOH", 'M'), ("last GC before OOM", 'M'), ("induced compacting GC", 'M'),
            ("fragmented gen0 (ephemeral GC)", 'W'), ("high memory load (ephemeral GC)", 'W'),
            ("high memory load and frag", 'M'), ("very high memory load and frag", 'M'), ("no gc mode", 'M'),
        ];

        private static readonly string[] s_expand =
        [
            "reused seg with normal fit", "reused seg with best fit", "expand promoting eph",
            "expand with a new seg", "no memory for a new seg", "expand in next full GC",
        ];

        private static readonly string[] s_other = ["using mark list", "demotion"];

        [Argument<string[]>(Name = "arguments", Help = "Unused arguments accepted for compatibility.")]
        public string[] Arguments { get; set; }

        public override unsafe void Invoke()
        {
            ISOSDacInterface dac = Dac;
            DacpGcHeapData heap = default;
            if (dac.GetGCHeapData(&heap) != 0)
            {
                WriteLine("GC Heap not initialized yet.");
                return;
            }

            ISOSDacInterface3 stats = dac as ISOSDacInterface3;
            DacpGCInterestingInfoData info = default;
            if (heap.bServerMode == 0)
            {
                // The native command prints the global counters even when the DAC cannot supply them.
                _ = stats?.GetGCGlobalMechanisms(&info.globalMechanisms[0]);
                for (int i = 0; i < s_global.Length; i++)
                {
                    WriteLine("{0,-30}: {1}", s_global[i], unchecked((int)info.globalMechanisms[i]));
                }

                WriteLine();
                WriteLine("[info per heap]");
                if (stats is null || stats.GetGCInterestingInfoStaticData(&info) != 0)
                {
                    throw new DiagnosticsException("Error requesting interesting GC info");
                }

                WriteHeapInfo(info);
                return;
            }

            WriteLine();
            WriteLine("[info per heap]");
            if (heap.HeapCount > uint.MaxValue / sizeof(ulong))
            {
                WriteLine("Failed to get GCHeaps:  integer overflow");
                return;
            }

            ClrDataAddress[] addresses = new ClrDataAddress[heap.HeapCount];
            if (dac.GetGCHeapList(heap.HeapCount, addresses, null) != 0)
            {
                WriteLine("Failed to get GCHeaps");
                return;
            }

            for (int i = 0; i < addresses.Length; i++)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                if (stats is null || stats.GetGCInterestingInfoData(addresses[i], &info) != 0)
                {
                    throw new DiagnosticsException($"Heap {i}: Error requesting interesting GC info");
                }

                WriteLine("--------info for heap {0}--------", i);
                WriteHeapInfo(info);
                WriteLine();
            }
        }

        private void WriteHeapInfo(DacpGCInterestingInfoData info)
        {
            WriteLine("Interesting data points");
            for (int i = 0; i < s_points.Length; i++)
            {
                WriteLine("{0,20}: {1}", s_points[i], unchecked((int)info.interestingDataPoints[i]));
            }

            WriteLine();
            WriteLine("Compacting reasons");
            for (int i = 0; i < s_compact.Length; i++)
            {
                WriteLine("[{0}]{1,35}: {2}", s_compact[i].Kind, s_compact[i].Name, unchecked((int)info.compactReasons[i]));
            }

            WriteLine();
            WriteLine("Expansion mechanisms");
            for (int i = 0; i < s_expand.Length; i++)
            {
                WriteLine("{0,30}: {1}", s_expand[i], unchecked((int)info.expandMechanisms[i]));
            }

            WriteLine();
            WriteLine("Other mechanisms enabled");
            for (int i = 0; i < s_other.Length; i++)
            {
                WriteLine("{0,20}: {1}", s_other[i], unchecked((int)info.bitMechanisms[i]));
            }
        }
    }
}
