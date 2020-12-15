using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace DacCompareNativeTypes
{
    class Program
    {
        static bool IsIgnoreType(Type t)
        {
            string fullName = t.FullName;

            // List of types with known mismatches
            // Specifically for comparing Cross OS builds of DAC and DBI
            List<string> ignoreTypes = new List<string>()
            {
                // Internal types
                // Not expected to matter to DAC
                "_CONTEXT",
                "_DISPATCHER_CONTEXT",
                "_DOTNET_TRACE_CONTEXT",
                "_EVENT_DESCRIPTOR",
                "_IMAGE_IMPORT_DESCRIPTOR",
                "_IMAGE_RUNTIME_FUNCTION_ENTRY",
                "_IMAGE_TLS_DIRECTORY32",
                "_IMAGE_TLS_DIRECTORY64",
                "_KNONVOLATILE_CONTEXT_POINTERS",
                "_LARGE_INTEGER",
                "_NEON128",
                "_OVERLAPPED",
                "_SYSTEM_INFO",
                "_ULARGE_INTEGER",
                "_UNWIND_CODE",
                "_UNWIND_INFO",
                "_WER_RUNTIME_EXCEPTION_INFORMATION",

                // Host specific basic types
                // Not expected to matter to DAC
                "timespec",
                "stat",
                "tagCY",
                "tagDEC",
                "tagSTATSTG",
                "tagVARIANT",
                "tm",

                // long is 32/64 bit depending on OS
                // ignore since we are effectively comparing different types
                "ClrSafeInt<unsignedlong>",

                // arm DAC mismatches
                // Not expected to matter to DAC
                "FunctionSigBuilder",
                "GetActiveInternalFramesData",
                "ILStubLinker",

                // DBI Types
                // Layout not expected to matter since they are not runtime types
                "Cordb",
                "CordbAppDomain",
                "CordbArrayValue",
                "CordbAssembly",
                "CordbBoxValue",
                "CordbBreakpoint",
                "CordbClass",
                "CordbCode",
                "CordbCodeEnum",
                "CordbEnumFilter",
                "CordbEval",
                "CordbFrame",
                "CordbFunction",
                "CordbFunctionBreakpoint",
                "CordbGenericValue",
                "CordbHandleValue",
                "CordbHashTable",
                "CordbHashTableEnum",
                "CordbHeapEnum",
                "CordbInternalFrame",
                "CordbJITILFrame",
                "CordbMDA",
                "CordbModule",
                "CordbNativeCode",
                "CordbNativeFrame",
                "CordbObjectValue",
                "CordbProcess",
                "CordbRefEnum",
                "CordbReferenceValue",
                "CordbRegisterSet",
                "CordbReJitILCode",
                "CordbRuntimeUnwindableFrame",
                "CordbStackWalk",
                "CordbStepper",
                "CordbThread",
                "CordbType",
                "CordbTypeEnum",
                "CordbValue",
                "CordbValueEnum",
                "CordbVariableHome",
                "CordbVCObjectValue",
                "CordbWin32EventThread",
                "DacDbiInterfaceImpl",
                "DbgTransportSession",
                "RSLock",
                "RSPtrArray<CordbAppDomain,RSSmartPtr<CordbAppDomain>>",
                "RSPtrArray<CordbInternalFrame,RSSmartPtr<CordbInternalFrame>>",
                "RSPtrArray<CordbProcess,RSSmartPtr<CordbProcess>>",
                "Target_CLiteWeightStgdbRW",

                // DBI not used in cross OS
                "TwoWayPipe",

                // Shims
                // Not expected to matter
                "ShimProcess",
                "ShimRemoteDataTarget",

                // Libunwind types 3.1 crossdac uses a different libunwind version
                "cursor",
                "dwarf_cie_info",
                "dwarf_cursor",
                "dwarf_rs_cache",
                "map_iterator",
                "mempool",
                "ucontext",
                "unw_addr_space",
                "unw_tdep_save_loc"
            };

            return ignoreTypes.Exists(x => String.CompareOrdinal(x, fullName) == 0);
        }

        static bool IsTypeMismatch(Type dwarfType, Type pdbType)
        {
            var dwarfUniqueMembers =
                from m in dwarfType.Members.Values
                where !pdbType.Members.ContainsKey(m.Name)
                select m;

            var allKeys =
                from m in pdbType.Members.Values.Concat(dwarfUniqueMembers)
                where !m.Name.StartsWith('(')
                orderby m.Offset, m.Name
                select m.Name;

            foreach(var k in allKeys)
            {
                Member d = null;
                Member p = null;

                bool match = ((pdbType.Members.TryGetValue(k, out p)) &&
                              (dwarfType.Members.TryGetValue(k, out d)) &&
                              (p.Offset == d.Offset));

                if (!match)
                {
                  return true;
                }
            }

            return false;
        }

        static void PrintTypeComparison(Type dwarfType, Type pdbType)
        {
            var dwarfUniqueMembers =
                from m in dwarfType.Members.Values
                where !pdbType.Members.ContainsKey(m.Name)
                select m;

            var allKeys =
                from m in pdbType.Members.Values.Concat(dwarfUniqueMembers)
                where !m.Name.StartsWith('(')
                orderby m.Offset, m.Name
                select m.Name;

            Console.WriteLine($"Type Comparison: {dwarfType.FullName}");
            Console.WriteLine($"  {"pdb",5} {"",4} {"dwarf",-5} {"Member Name"}");
            foreach(var k in allKeys)
            {
                Member d = null;
                Member p = null;

                pdbType.Members.TryGetValue(k, out p);
                dwarfType.Members.TryGetValue(k, out d);

                string match = ((p != null) && (d != null) && (p.Offset == d.Offset)) ? "" : "****";

                Console.WriteLine($"  {p?.Offset.ToString() ?? "",5} {match,4} {d?.Offset.ToString() ?? "",-5} {k}");
            }
        }

        static bool AllPbdAlternatesMatch(Type dwarfType, Type pdbType)
        {
            foreach(var p in pdbType.Alternates.Values)
            {
                if (dwarfType.Alternates.ContainsKey(p.ToString()))
                    continue;

                bool match = false;
                foreach (var d in dwarfType.Alternates.Values)
                {
                    match = !IsTypeMismatch(d, p);
                    if (match)
                        break;
                }

                if (!match)
                    return false;
            }
            return true;
        }

        int ignored;
        int matched;
        int mismatched;
        int dwarfUnique;
        int pdbUnique;

        Program() {}

        void CompareTypes(Type dwarfType, Type pdbType)
        {
            if (IsIgnoreType(pdbType))
            {
                ignored++;
                return;
            }

            if (AllPbdAlternatesMatch(dwarfType, pdbType))
            {
                matched++;
                return;
            }

            mismatched++;

            foreach(var p in pdbType.Alternates.Values)
            {
                foreach (var d in dwarfType.Alternates.Values)
                {
                    PrintTypeComparison(d, p);
                }
            }
        }

        int Main(string pdbPath, string dwarfPath)
        {
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

            Console.WriteLine($"PDB unique types : {pdbTypes.Keys.Count}");

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

            Console.WriteLine($"Dwarf unique types : {dwarfTypes.Keys.Count}");

            foreach (Type type in dwarfTypes.Values.OrderBy(x => x.FullName))
            {
                if (pdbTypes.ContainsKey(type.FullName))
                {
                    CompareTypes(type, pdbTypes[type.FullName]);
                }
                else
                {
                    // Console.WriteLine($"dwarf unique type : {type.FullName}");
                    dwarfUnique++;
                }
            }

            foreach (Type type in pdbTypes.Values.OrderBy(x => x.FullName))
            {
                if (!dwarfTypes.ContainsKey(type.FullName))
                {
                    // Console.WriteLine($"PDB unique type : {type.FullName}");
                    pdbUnique++;
                }
            }

            Console.WriteLine($"Matches: {matched}");
            Console.WriteLine($"Ignored: {ignored}");
            Console.WriteLine($"Mismatched: {mismatched}");
            Console.WriteLine($"DwarfUnique: {dwarfUnique}");
            Console.WriteLine($"PdbUnique: {pdbUnique}");

            return mismatched + (matched == 0 ? 1 : 0);
        }

        static int Main(string[] args)
        {
            string pdbPath = @"pdb";
            string dwarfPath = @"dwarf";

            Program program = new Program();

            return program.Main(pdbPath, dwarfPath);
        }
    }
}
