// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpgen", Aliases = new string[] { "dg" }, Help = "Displays heap content for the specified generation.")]
    public class DumpGenCommand : ClrMDHelperCommandBase
    {
        private const string statsHeader32bits = "      MT    Count    TotalSize Class Name";
        private const string statsHeader64bits = "              MT    Count    TotalSize Class Name";
        private const string methodTableHeader32bits = " Address       MT     Size";
        private const string methodTableHeader64bits = "         Address               MT     Size";

        [Argument(Name = "generation", Help = "The GC generation to get heap data from.")]
        public string Generation { get; set; }

        [Option(Name = "-type", Help = "List only those objects whose type name is a substring match of the provided string.")]
        public string FilterByTypeName { get; set; }

        [Option(Name = "-mt", Help = "The address pointing on a Method table.")]
        public string MethodTableAddress { get; set; }

        public override void Invoke()
        {
            GCGeneration generation = ParseGenerationArgument(Generation);
            if (generation != GCGeneration.NotSet)
            {
                DumpGen dumpGen = new(Helper, generation);

                if (string.IsNullOrEmpty(MethodTableAddress))
                {
                    IEnumerable<DumpGenStats> dumpGenResult = dumpGen.GetStats(FilterByTypeName);
                    WriteStatistics(dumpGenResult);
                }
                else if (TryParseAddress(MethodTableAddress, out ulong address))
                {
                    IEnumerable<ClrObject> objects = dumpGen.GetInstances(address);
                    WriteInstances(objects);
                }
                else
                {
                    throw new DiagnosticsException("Hexadecimal address expected for -mt option");
                }
            }
            WriteLine(string.Empty);
        }

        private void WriteInstances(IEnumerable<ClrObject> objects)
        {
            ulong objectsCount = 0UL;
            WriteLine(Helper.Is64Bits() ? methodTableHeader64bits : methodTableHeader32bits);
            foreach (ClrObject obj in objects)
            {
                objectsCount++;
                if (Helper.Is64Bits())
                {
                    WriteLine($"{obj.Address:x16} {obj.Type.MethodTable:x16} {obj.Size,8}");
                }
                else
                {
                    WriteLine($"{obj.Address:x8} {obj.Type.MethodTable:x8} {obj.Size,8}");
                }
            }
            WriteLine($"Total {objectsCount} objects");
        }

        private void WriteStatistics(IEnumerable<DumpGenStats> dumpGenResult)
        {
            ulong objectsCount = 0UL;
            WriteLine("Statistics:");
            WriteLine(Helper.Is64Bits() ? statsHeader64bits : statsHeader32bits);
            foreach (DumpGenStats typeStats in dumpGenResult)
            {
                objectsCount += typeStats.NumberOfOccurences;
                if (Helper.Is64Bits())
                {
                    WriteLine($"{typeStats.Type.MethodTable:x16} {typeStats.NumberOfOccurences,8} {typeStats.TotalSize,12} {typeStats.Type.Name}");
                }
                else
                {
                    WriteLine($"{typeStats.Type.MethodTable:x8} {typeStats.NumberOfOccurences,8} {typeStats.TotalSize,12} {typeStats.Type.Name}");
                }
            }
            WriteLine($"Total {objectsCount} objects");
        }

        private static GCGeneration ParseGenerationArgument(string generation)
        {
            if (string.IsNullOrEmpty(generation))
            {
                throw new DiagnosticsException("Generation argument is missing");
            }
            string lowerString = generation.ToLowerInvariant();
            GCGeneration result = lowerString switch
            {
                "gen0" => GCGeneration.Generation0,
                "gen1" => GCGeneration.Generation1,
                "gen2" => GCGeneration.Generation2,
                "loh" => GCGeneration.LargeObjectHeap,
                "poh" => GCGeneration.PinnedObjectHeap,
                "foh" => GCGeneration.FrozenObjectHeap,
                _ => GCGeneration.NotSet,
            };

            if (result == GCGeneration.NotSet)
            {
                throw new DiagnosticsException($"{generation} is not a supported generation (gen0, gen1, gen2, loh, poh, foh)");
            }
            return result;
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"-------------------------------------------------------------------------------
DumpGen
This command can be used for 2 use cases:
- Lists number of objects and total size for every objects on the heap, for a specified generation
  Acts like the 'dumpheap -stat' command for a specified generation and return data in the same format

- Lists object addresses corresponding to the method table passed in parameter (by providing the '-mt' option), for a specified generation
  Acts like the 'dumpheap -mt' command for a specified generation and return data in the same format

Generation number can take the following values (case insensitive):
- gen0
- gen1
- gen2
- loh
- poh
- foh

> dumpgen gen0
Statistics:
              MT    Count    TotalSize Class Name
00007ff9ea6601c8        1           24 System.Collections.Generic.GenericEqualityComparer<System.String>
00007ff9ea660338        1           24 System.Collections.Generic.NonRandomizedStringEqualityComparer
...
00007ff9ea69b268        7        33612 System.Char[]
00007ff9ea651e18      204        41154 System.String
Total 651 objects

As the original dumpheap command, we can pass an additional '-type' parameter to filter out on type name
> dumpgen gen2 -type Object
Statistics:
              MT    Count    TotalSize Class Name
00007ff9ea590af0       26          624 System.Object
00007ff9ea66f4e0        3          720 System.Collections.Generic.Dictionary<System.String, System.Object>+Entry[]
00007ff9ea596618       17         2080 System.Object[]
Total 46 objects

> dumpgen gen0 -mt 00007ff9ea6e75b8
         Address               MT     Size
00000184aa23e8a0 00007ff9ea6e75b8       40
00000184aa23e8f0 00007ff9ea6e75b8       40
00000184aa23e918 00007ff9ea6e75b8       40
Total 3 objects
";
    }
}
