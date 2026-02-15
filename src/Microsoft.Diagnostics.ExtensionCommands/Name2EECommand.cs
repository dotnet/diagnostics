// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "name2ee", Aliases = new[] { "Name2EE" }, Help = "Displays the MethodTable structure and EEClass structure for the specified type or method in the specified module.")]
    public class Name2EECommand : ClrRuntimeCommandBase
    {
        [Argument(Name = "arguments", Help = "module_name type_or_method_name (or module_name!type_or_method_name)")]
        public string[] Arguments { get; set; }

        private enum MatchKind
        {
            None,
            Type,
            Method,
            Field,
        }

        public override void Invoke()
        {
            if (Arguments == null || Arguments.Length == 0)
            {
                PrintUsage();
                return;
            }

            string moduleName;
            string itemName;

            if (Arguments.Length == 1)
            {
                // Try parsing "module!type" format
                string combined = Arguments[0];
                int bangIndex = combined.IndexOf('!');
                if (bangIndex > 0 && bangIndex != combined.Length - 1 && combined.IndexOf('!', bangIndex + 1) == -1)
                {
                    moduleName = combined[..bangIndex];
                    itemName = combined[(bangIndex + 1)..];
                }
                else
                {
                    PrintUsage();
                    return;
                }
            }
            else if (Arguments.Length == 2)
            {
                moduleName = Arguments[0];
                itemName = Arguments[1];
            }
            else
            {
                PrintUsage();
                return;
            }

            if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(itemName))
            {
                PrintUsage();
                return;
            }

            bool isWildcard = moduleName == "*";

            IEnumerable<ClrModule> modules;
            if (isWildcard)
            {
                modules = Runtime.EnumerateModules();
            }
            else
            {
                modules = Runtime.EnumerateModules().Where(m => MatchesModuleName(m, moduleName));
            }

            int matchCount = 0;
            int nonMatchCount = 0;

            foreach (ClrModule module in modules)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                string fileName = GetModuleFileName(module);
                bool foundInModule = SearchModule(module, itemName, isWildcard, fileName, ref matchCount);

                if (!foundInModule && isWildcard)
                {
                    nonMatchCount++;
                }
            }

            if (isWildcard && nonMatchCount > 0)
            {
                if (matchCount > 0)
                {
                    WriteLine("--------------------------------------");
                }

                WriteLine($"\nScanned {nonMatchCount} module{(nonMatchCount == 1 ? "" : "s")} which had no matches.");
            }

            if (matchCount == 0 && nonMatchCount == 0)
            {
                WriteLine($"Failed to find module matching '{moduleName}'.");
            }
        }

        /// <summary>
        /// Searches a module for the given item name. Returns true if any match was found.
        /// </summary>
        private bool SearchModule(ClrModule module, string itemName, bool isWildcard, string fileName, ref int matchCount)
        {
            // Normalize nested type separators (from the original C++ version)
            string normalizedName = itemName.Replace('/', '+');

            // Try to find as a type first, then as method/field
            // Walk all types via EnumerateTypeDefToMethodTableMap
            bool found = false;

            foreach ((ulong mt, int token) in module.EnumerateTypeDefToMethodTableMap())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (mt == 0)
                {
                    continue;
                }

                ClrType type = Runtime.GetTypeByMethodTable(mt);
                if (type == null)
                {
                    continue;
                }

                MatchKind matchKind = GetMatchKind(type, normalizedName, out ClrMethod matchedMethod, out ClrField matchedField);
                if (matchKind == MatchKind.None)
                {
                    continue;
                }

                // Found a match
                if (!found && !isWildcard)
                {
                    // Non-wildcard: print header for the module on first match
                    if (matchCount > 0)
                    {
                        WriteLine("--------------------------------------");
                    }
                    PrintModuleHeader(module, fileName);
                }
                else if (!found && isWildcard)
                {
                    // Wildcard: print header only when we find a match
                    if (matchCount > 0)
                    {
                        WriteLine("--------------------------------------");
                    }
                    PrintModuleHeader(module, fileName);
                }
                else
                {
                    // Multiple matches within the same module
                    WriteLine("-----------------------");
                }

                found = true;

                switch (matchKind)
                {
                    case MatchKind.Type:
                        PrintTypeInfo(type);
                        break;
                    case MatchKind.Method:
                        PrintMethodInfo(matchedMethod);
                        break;
                    case MatchKind.Field:
                        PrintFieldInfo(type);
                        break;
                }

                matchCount++;
            }

            if (!found && !isWildcard)
            {
                // Non-wildcard with no match: still print module header
                if (matchCount > 0)
                {
                    WriteLine("--------------------------------------");
                }
                PrintModuleHeader(module, fileName);
                matchCount++;
            }

            return found;
        }

        /// <summary>
        /// Determines what kind of match the given name is for this type.
        /// Checks: exact type name, method name, field name.
        /// </summary>
        private static MatchKind GetMatchKind(ClrType type, string name, out ClrMethod matchedMethod, out ClrField matchedField)
        {
            matchedMethod = null;
            matchedField = null;

            string typeName = type.Name;
            if (typeName == null)
            {
                return MatchKind.None;
            }

            // Normalize the type name (ClrMD uses '.' for nested types in Name, but we also handle '+')
            // Direct type match
            if (string.Equals(typeName, name, StringComparison.Ordinal))
            {
                return MatchKind.Type;
            }

            // Check if the name could be TypeName.MethodOrField by splitting on the last '.'
            // Handle the ".." case (explicit interface implementation) same as C++ version
            int dotIndex = name.LastIndexOf('.');
            if (dotIndex <= 0)
            {
                return MatchKind.None;
            }

            // Check for ".." (back up one more)
            if (dotIndex > 0 && name[dotIndex - 1] == '.')
            {
                dotIndex--;
            }

            string typePartOfName = name.Substring(0, dotIndex);
            string memberName = name.Substring(dotIndex + 1);

            // If the ".." case: memberName will start with the interface method
            // e.g., "MyType..InterfaceMethod" -> typePart="MyType", memberName=".InterfaceMethod"

            if (!string.Equals(typeName, typePartOfName, StringComparison.Ordinal))
            {
                return MatchKind.None;
            }

            // Check methods
            foreach (ClrMethod method in type.Methods)
            {
                if (method.Name != null && string.Equals(method.Name, memberName, StringComparison.Ordinal))
                {
                    matchedMethod = method;
                    return MatchKind.Method;
                }
            }

            // Check instance fields
            foreach (ClrInstanceField field in type.Fields)
            {
                if (field.Name != null && string.Equals(field.Name, memberName, StringComparison.Ordinal))
                {
                    matchedField = field;
                    return MatchKind.Field;
                }
            }

            // Check static fields
            foreach (ClrStaticField field in type.StaticFields)
            {
                if (field.Name != null && string.Equals(field.Name, memberName, StringComparison.Ordinal))
                {
                    matchedField = field;
                    return MatchKind.Field;
                }
            }

            return MatchKind.None;
        }

        private void PrintUsage()
        {
            WriteLine("Usage: !name2ee module_name item_name");
            WriteLine("  or   !name2ee module_name!item_name");
            WriteLine("       use * for module_name to search all loaded modules");
            WriteLine("Examples: !name2ee  mscorlib.dll System.String.ToString");
            WriteLine("          !name2ee *!System.String");
        }

        private void PrintModuleHeader(ClrModule module, string fileName)
        {
            if (Console.SupportsDml)
            {
                Console.WriteDml($"Module:      <exec cmd=\"!dumpmodule /d {module.Address:x}\">{module.Address:x16}</exec>\n");
            }
            else
            {
                WriteLine($"Module:      {module.Address:x16}");
            }

            WriteLine($"Assembly:    {fileName}");
        }

        private void PrintTypeInfo(ClrType type)
        {
            WriteLine($"Token:       {type.MetadataToken:x16}");

            if (type.MethodTable != 0)
            {
                if (Console.SupportsDml)
                {
                    Console.WriteDml($"MethodTable: <exec cmd=\"!dumpmt /d {type.MethodTable:x}\">{type.MethodTable:x16}</exec>\n");
                }
                else
                {
                    WriteLine($"MethodTable: {type.MethodTable:x16}");
                }
            }
            else
            {
                WriteLine("MethodTable: <not loaded yet>");
            }

            WriteLine($"Name:        {type.Name}");
        }

        private void PrintMethodInfo(ClrMethod method)
        {
            WriteLine($"Token:       {(uint)method.MetadataToken:x16}");

            if (method.MethodDesc != 0)
            {
                if (Console.SupportsDml)
                {
                    Console.WriteDml($"MethodDesc:  <exec cmd=\"!dumpmd /d {method.MethodDesc:x}\">{method.MethodDesc:x16}</exec>\n");
                }
                else
                {
                    WriteLine($"MethodDesc:  {method.MethodDesc:x16}");
                }
            }
            else
            {
                WriteLine("MethodDesc:  <not loaded yet>");
            }

            WriteLine($"Name:        {method.Signature ?? method.Name ?? "<unknown>"}");

            if (method.NativeCode != 0)
            {
                if (Console.SupportsDml)
                {
                    Console.WriteDml($"JITTED Code Address: <exec cmd=\"!u {method.NativeCode:x}\">{method.NativeCode:x16}</exec>\n");
                }
                else
                {
                    WriteLine($"JITTED Code Address: {method.NativeCode:x16}");
                }
            }
            else
            {
                if (method.MethodDesc != 0)
                {
                    if (Console.SupportsDml)
                    {
                        Console.WriteDml($"Not JITTED yet. Use <exec cmd=\"!bpmd -md {method.MethodDesc:x}\">!bpmd -md {method.MethodDesc:x16}</exec> to break on run.\n");
                    }
                    else
                    {
                        WriteLine($"Not JITTED yet. Use !bpmd -md {method.MethodDesc:x16} to break on run.");
                    }
                }
                else
                {
                    WriteLine("Not JITTED yet.");
                }
            }
        }

        private void PrintFieldInfo(ClrType type)
        {
            WriteLine("Field (mdToken token) of");
            PrintTypeInfo(type);
        }

        private static string GetModuleFileName(ClrModule module)
        {
            if (string.IsNullOrEmpty(module.Name))
            {
                return module.IsDynamic ? "<dynamic>" : "<unknown>";
            }

            return Path.GetFileName(module.Name);
        }

        private static bool MatchesModuleName(ClrModule module, string name)
        {
            if (string.IsNullOrEmpty(module.Name))
            {
                return false;
            }

            string fileName = Path.GetFileName(module.Name);

            // Match by filename (with or without extension)
            if (string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Try without .dll extension
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(module.Name);
            if (string.Equals(fileNameWithoutExt, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Match by full path
            if (string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"Name2EE displays the MethodTable and EEClass for the specified type or method
in the specified module. The specified module must be loaded in the process.

To get the proper type name, browse the module with the IL disassembler 
(ildasm.exe). You can also pass * as the module name parameter to search all
loaded managed modules. When using the wildcard, only matching modules are
displayed and non-matching modules are summarized at the end.

    {prompt}name2ee mscorlib.dll System.String.ToString
    Module:      00007ffe65744000
    Assembly:    System.Private.CoreLib.dll
    MethodDesc:  00007ffe65ff0bf0
    Name:        System.String.ToString(System.IFormatProvider)
    JITTED Code Address: 00007ffe660a1234

    {prompt}name2ee *!System.String
    Module:      00007ffe65744000
    Assembly:    System.Private.CoreLib.dll
    Token:       0000000002000XXX
    MethodTable: 00007ffe65ff0bf0
    Name:        System.String
    --------------------------------------

    Scanned 45 modules which had no matches.
";
    }
}
