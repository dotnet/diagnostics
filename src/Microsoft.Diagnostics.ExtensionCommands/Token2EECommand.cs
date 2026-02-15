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
    [Command(Name = "token2ee", Aliases = new[] { "Token2EE" }, Help = "Displays the MethodTable structure and MethodDesc structure for the specified token and module.")]
    public class Token2EECommand : ClrRuntimeCommandBase
    {
        [Argument(Name = "arguments", Help = "module_name mdToken")]
        public string[] Arguments { get; set; }

        public override void Invoke()
        {
            if (Arguments == null || Arguments.Length != 2)
            {
                PrintUsage();
                return;
            }

            string moduleName = Arguments[0];
            if (!TryParseToken(Arguments[1], out int token))
            {
                WriteLine("Invalid token: {0}", Arguments[1]);
                PrintUsage();
                return;
            }

            if (string.IsNullOrEmpty(moduleName))
            {
                PrintUsage();
                return;
            }

            // Validate token type
            uint tokenType = (uint)token & 0xFF000000;
            if (tokenType != 0x02000000 /* mdtTypeDef */
                && tokenType != 0x01000000 /* mdtTypeRef */
                && tokenType != 0x06000000 /* mdtMethodDef */
                && tokenType != 0x04000000 /* mdtFieldDef */)
            {
                WriteLine("This token type is not supported");
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

            bool first = true;
            bool anyModule = false;

            foreach (ClrModule module in modules)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                anyModule = true;

                if (!first)
                {
                    WriteLine("--------------------------------------");
                }

                first = false;

                string fileName = GetModuleFileName(module);
                PrintModuleHeader(module, fileName);
                PrintTokenInfo(module, token);
            }

            if (!anyModule)
            {
                WriteLine("Failed to request module list.");
            }
        }

        private void PrintTokenInfo(ClrModule module, int token)
        {
            uint tokenType = (uint)token & 0xFF000000;

            switch (tokenType)
            {
                case 0x06000000: // mdtMethodDef
                    PrintMethodDefInfo(module, token);
                    break;
                case 0x02000000: // mdtTypeDef
                case 0x01000000: // mdtTypeRef
                    PrintTypeInfo(module, token);
                    break;
                case 0x04000000: // mdtFieldDef
                    PrintFieldInfo(module, token);
                    break;
            }
        }

        private void PrintMethodDefInfo(ClrModule module, int token)
        {
            // Find the method by token
            ClrMethod method = FindMethodByToken(module, token);

            WriteLine("Token:       {0:x8}", (uint)token);

            if (method != null && method.MethodDesc != 0)
            {
                if (Console.SupportsDml)
                {
                    Console.WriteDml($"MethodDesc:  <exec cmd=\"!dumpmd /d {method.MethodDesc:x}\">{method.MethodDesc:x16}</exec>\n");
                }
                else
                {
                    WriteLine("MethodDesc:  {0:x16}", method.MethodDesc);
                }

                // Use the full signature for the name (matches C++ behavior using GetMethodDescName)
                string name = method.Signature ?? method.Name ?? "<unknown>";
                WriteLine("Name:        {0}", name);

                if (method.NativeCode != 0)
                {
                    if (Console.SupportsDml)
                    {
                        Console.WriteDml($"JITTED Code Address: <exec cmd=\"!u {method.NativeCode:x}\">{method.NativeCode:x16}</exec>\n");
                    }
                    else
                    {
                        WriteLine("JITTED Code Address: {0:x16}", method.NativeCode);
                    }
                }
                else
                {
                    if (Console.SupportsDml)
                    {
                        Console.WriteDml($"Not JITTED yet. Use <exec cmd=\"!bpmd -md {method.MethodDesc:x}\">!bpmd -md {method.MethodDesc:x16}</exec> to break on run.\n");
                    }
                    else
                    {
                        WriteLine("Not JITTED yet. Use !bpmd -md {0:x16} to break on run.", method.MethodDesc);
                    }
                }
            }
            else if (method != null)
            {
                // Method found but MethodDesc not loaded
                WriteLine("MethodDesc:  <not loaded yet>");
                string name = method.Name ?? "<unknown>";
                WriteLine("Name:        {0}", name);
                WriteLine("Not JITTED yet.");
            }
            else
            {
                // Could not resolve the token - try to see if it's a valid token in the module
                WriteLine("<invalid module token>");
            }
        }

        private void PrintTypeInfo(ClrModule module, int token)
        {
            ClrType type = FindTypeByToken(module, token);

            WriteLine("Token:       {0:x8}", (uint)token);

            if (type != null && type.MethodTable != 0)
            {
                if (Console.SupportsDml)
                {
                    Console.WriteDml($"MethodTable: <exec cmd=\"!dumpmt /d {type.MethodTable:x}\">{type.MethodTable:x16}</exec>\n");
                }
                else
                {
                    WriteLine("MethodTable: {0:x16}", type.MethodTable);
                }

                // The C++ version also prints EEClass
                // ClrMD doesn't directly expose EEClass address, so we skip it
                // (consistent with how Name2EECommand.PrintTypeInfo works)
            }
            else
            {
                WriteLine("MethodTable: <not loaded yet>");
                WriteLine("EEClass:     <not loaded yet>");
            }

            string name = type?.Name ?? "<unknown>";
            WriteLine("Name:        {0}", name);
        }

        private void PrintFieldInfo(ClrModule module, int token)
        {
            ClrField field = FindFieldByToken(module, token);

            WriteLine("Token:       {0:x8}", (uint)token);

            if (field != null)
            {
                WriteLine("Field name:  {0}", field.Name ?? "<unknown>");
            }
            else
            {
                WriteLine("<invalid module token>");
            }
        }

        private ClrMethod FindMethodByToken(ClrModule module, int token)
        {
            foreach ((ulong mt, int _) in module.EnumerateTypeDefToMethodTableMap())
            {
                if (mt == 0)
                {
                    continue;
                }

                ClrType type = Runtime.GetTypeByMethodTable(mt);
                if (type == null)
                {
                    continue;
                }

                foreach (ClrMethod method in type.Methods)
                {
                    if (method.MetadataToken == token)
                    {
                        return method;
                    }
                }
            }

            return null;
        }

        private ClrType FindTypeByToken(ClrModule module, int token)
        {
            foreach ((ulong mt, int typeToken) in module.EnumerateTypeDefToMethodTableMap())
            {
                if (mt == 0)
                {
                    continue;
                }

                if (typeToken == token)
                {
                    return Runtime.GetTypeByMethodTable(mt);
                }
            }

            return null;
        }

        private ClrField FindFieldByToken(ClrModule module, int token)
        {
            foreach ((ulong mt, int _) in module.EnumerateTypeDefToMethodTableMap())
            {
                if (mt == 0)
                {
                    continue;
                }

                ClrType type = Runtime.GetTypeByMethodTable(mt);
                if (type == null)
                {
                    continue;
                }

                foreach (ClrInstanceField field in type.Fields)
                {
                    if (field.Token == token)
                    {
                        return field;
                    }
                }

                foreach (ClrStaticField field in type.StaticFields)
                {
                    if (field.Token == token)
                    {
                        return field;
                    }
                }
            }

            return null;
        }

        private void PrintModuleHeader(ClrModule module, string fileName)
        {
            if (Console.SupportsDml)
            {
                Console.WriteDml($"Module:      <exec cmd=\"!dumpmodule /d {module.Address:x}\">{module.Address:x16}</exec>\n");
            }
            else
            {
                WriteLine("Module:      {0:x16}", module.Address);
            }

            WriteLine("Assembly:    {0}", fileName);
        }

        private void PrintUsage()
        {
            WriteLine("Usage: !token2ee module_name mdToken");
            WriteLine("       You can pass * for module_name to search all modules.");
        }

        private static bool TryParseToken(string value, out int token)
        {
            token = 0;

            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            // Support hex with or without 0x prefix
            string toParse = value;
            if (toParse.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                toParse = toParse.Substring(2);
            }

            if (uint.TryParse(toParse, System.Globalization.NumberStyles.HexNumber, null, out uint parsed))
            {
                token = unchecked((int)parsed);
                return true;
            }

            return false;
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

            if (string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(module.Name);
            if (string.Equals(fileNameWithoutExt, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"Token2EE displays the MethodTable and MethodDesc for the specified metadata
token in the specified module. The specified module must be loaded in the process.

You can pass * as the module name to search all loaded managed modules.

    {prompt}token2ee unittest.exe 02000003
    Module:      00007ffe5fa20000
    Assembly:    unittest.exe
    Token:       02000003
    MethodTable: 00007ffe5fdc0388
    Name:        Unittest.TestClass

    {prompt}token2ee unittest.exe 06000001
    Module:      00007ffe5fa20000
    Assembly:    unittest.exe
    Token:       06000001
    MethodDesc:  00007ffe5fdc03b0
    Name:        Unittest.TestClass.Main(System.String[])
    JITTED Code Address: 00007ffe5ff01234
";
    }
}
