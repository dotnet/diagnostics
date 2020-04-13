using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace DacCompareNativeTypes
{
    class Program
    {
        static void Main(string[] args)
        {
            string pdbPath = @"pdb";
            string dwarfPath = @"dwarf";

            Dictionary<string, Type> pdbTypes = new Dictionary<string, Type>();
            Dictionary<string, Type> dwarfTypes = new Dictionary<string, Type>();

            foreach (Type type in PdbParser.Parse(File.ReadLines(pdbPath)))
            {
                if (pdbTypes.ContainsKey(type.FullName))
                {
                    if (!pdbTypes[type.FullName].Alternates.ContainsKey(type.ToString()))
                    {
                        pdbTypes[type.FullName].Alternates[type.ToString()] = type;
                    }
                }
                else
                {
                    pdbTypes[type.FullName] = type;
                    pdbTypes[type.FullName].Alternates[type.ToString()] = type;
                }
            }

            foreach (Type type in DwarfParser.Parse(File.ReadLines(dwarfPath)))
            {
                if (dwarfTypes.ContainsKey(type.FullName))
                {
                    if (!dwarfTypes[type.FullName].Alternates.ContainsKey(type.ToString()))
                    {
                        dwarfTypes[type.FullName].Alternates[type.ToString()] = type;
                    }
                }
                else
                {
                    dwarfTypes[type.FullName] = type;
                    dwarfTypes[type.FullName].Alternates[type.ToString()] = type;
                }
            }

            foreach (Type type in dwarfTypes.Values.OrderBy(x => x.FullName))
            {
                foreach (string alt in type.Alternates.Keys.OrderBy(x => x))
                {
                    // Console.WriteLine(alt);
                }

                if (pdbTypes.ContainsKey(type.FullName) && ! pdbTypes[type.FullName].Alternates.ContainsKey(type.ToString()))
                {
                    Console.WriteLine($"Type Mismatch: {type.FullName}\n{type}\n{type.SourceLine}");
                    foreach (string pdbType in pdbTypes[type.FullName].Alternates.Keys)
                    {
                        Console.WriteLine(pdbType);
                    }
                }
            }
        }
    }
}
